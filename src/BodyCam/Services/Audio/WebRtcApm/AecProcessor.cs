using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Audio.WebRtcApm;

/// <summary>
/// Processes microphone audio through WebRTC APM to remove echo, noise, and apply gain control.
/// Thread-safe — all native calls are serialized.
/// </summary>
public sealed class AecProcessor : IAecProcessor
{
    private const int ApmRate = 48000;
    private const int Channels = 1;
    private const int FrameSamples = 480; // 10ms at 48kHz
    private const int DesktopStreamDelayMs = 40;
    private const int MobileStreamDelayMs = 150; // Android/iOS have higher speaker-to-mic latency

    private readonly ILogger<AecProcessor> _logger;
    private readonly AppSettings? _settings;
    private readonly ClockDriftMonitor? _drift;
    private readonly object _lock = new();

    private IntPtr _apm;
    private IntPtr _streamConfig;
    private bool _initialized;
    private bool _disposed;
    private bool _mobileMode;

    // Pre-allocated frame buffers (reused across calls to avoid GC pressure)
    private readonly float[] _srcFrame = new float[FrameSamples];
    private readonly float[] _destFrame = new float[FrameSamples];

    // Residual buffers to prevent sample loss at chunk boundaries
    private readonly List<float> _captureResidual = new(FrameSamples);
    private readonly List<float> _renderResidual = new(FrameSamples);

    // Pre-allocated pointer arrays for unsafe frame processing
    private readonly IntPtr[] _srcPtrSlot = new IntPtr[1];
    private readonly IntPtr[] _destPtrSlot = new IntPtr[1];

    // Statistics tracking (Phase 6.1)
    private System.Timers.Timer? _statsTimer;
    private int _statsTickCount;

    public event EventHandler<ApmStatistics>? StatisticsUpdated;

    public bool IsEnabled { get; set; } = true;

    public AecProcessor(ILogger<AecProcessor> logger, AppSettings? settings = null, ClockDriftMonitor? drift = null)
    {
        _logger = logger;
        _settings = settings;
        _drift = drift;
        if (_settings is not null) IsEnabled = !_settings.DisableAec;
    }

    /// <summary>
    /// Initialize the APM with AEC + noise suppression + high-pass filter.
    /// Call once at startup. Safe to call multiple times (no-ops after first).
    /// </summary>
    public void Initialize(bool mobileMode = false)
    {
        lock (_lock)
        {
            if (_initialized) return;

            _mobileMode = mobileMode;

            _apm = WebRtcApmInterop.Create();
            if (_apm == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create WebRTC APM instance.");

            _streamConfig = WebRtcApmInterop.StreamConfigCreate(ApmRate, (nuint)Channels);
            if (_streamConfig == IntPtr.Zero)
            {
                WebRtcApmInterop.Destroy(_apm);
                _apm = IntPtr.Zero;
                throw new InvalidOperationException("Failed to create stream config.");
            }

            // Configure
            var config = WebRtcApmInterop.ConfigCreate();
            try
            {
                WebRtcApmInterop.ConfigSetEchoCanceller(config, 1, mobileMode ? 1 : 0);

                // Phase 5.2: Noise suppression with configurable level
                int nsLevel = Math.Clamp(_settings?.NoiseSuppressionLevel ?? 1, 0, 3);
                WebRtcApmInterop.ConfigSetNoiseSuppression(config, 1, nsLevel);

                WebRtcApmInterop.ConfigSetHighPassFilter(config, 1);

                // Phase 5.1: AGC with configurable target and compression
                int targetDbfs = Math.Abs(_settings?.AgcTargetLevelDbfs ?? -9);
                int compressionDb = _settings?.AgcCompressionGainDb ?? 6;
                WebRtcApmInterop.ConfigSetGainController1(config, 1, 1, targetDbfs, compressionDb, 1);
                // mode=1 (AdaptiveDigital), target=configurable dBFS, compression=configurable dB, limiter=on

                int err = WebRtcApmInterop.ConfigApply(_apm, config);
                if (err != 0)
                    _logger.LogWarning("APM ConfigApply returned error {Error}", err);
            }
            finally
            {
                WebRtcApmInterop.ConfigDestroy(config);
            }

            int initErr = WebRtcApmInterop.Initialize(_apm);
            if (initErr != 0)
                _logger.LogWarning("APM Initialize returned error {Error}", initErr);

            int delayMs = mobileMode ? MobileStreamDelayMs : DesktopStreamDelayMs;
            WebRtcApmInterop.SetStreamDelayMs(_apm, delayMs);

            _initialized = true;

            // Start statistics timer only if the native library exports GetStatistics (Phase 6.1)
            bool hasStatsExport = false;
            try
            {
                var libHandle = NativeLibrary.Load("webrtc-apm");
                hasStatsExport = NativeLibrary.TryGetExport(libHandle, "webrtc_apm_get_statistics", out _);
            }
            catch { /* library already loaded by P/Invoke, probe failed — skip stats */ }

            if (hasStatsExport)
            {
                _statsTimer = new System.Timers.Timer(1000) { AutoReset = true };
                _statsTimer.Elapsed += (_, _) =>
                {
                    if (GetStatistics() is { } s)
                    {
                        StatisticsUpdated?.Invoke(this, s);
                        if (++_statsTickCount % 10 == 0)
                            _logger.LogInformation(
                                "AEC: ERLE={ERLE:F1}dB ERLEnh={Enh:F1}dB delay={Delay}ms resEcho={Res:F2} divFilter={Div:F2}",
                                s.EchoReturnLossDb, s.EchoReturnLossEnhancementDb, s.DelayMs,
                                s.ResidualEchoLikelihood, s.DivergentFilterFraction);
                    }
                    // Drive clock drift monitor (Phase 6.2)
                    _drift?.Tick();
                };
                _statsTimer.Start();
            }
            else
            {
                _logger.LogDebug("webrtc_apm_get_statistics not exported — AEC statistics disabled");
                // Still drive clock drift monitor without stats
                if (_drift is not null)
                {
                    _statsTimer = new System.Timers.Timer(1000) { AutoReset = true };
                    _statsTimer.Elapsed += (_, _) => _drift.Tick();
                    _statsTimer.Start();
                }
            }

            _logger.LogInformation("WebRTC APM initialized (mobileMode={Mobile}, streamDelay={Delay}ms)", mobileMode, delayMs);
        }
    }

    /// <summary>
    /// Reset the render reference buffer, clearing any echo path estimate.
    /// Call this after interrupting playback to prevent phantom subtraction.
    /// </summary>
    public void ResetRenderReference()
    {
        if (!_initialized) return;
        lock (_lock)
        {
            if (_disposed) return;

            // Re-init clears the AEC echo path estimate and any buffered render frames
            int err = WebRtcApmInterop.Initialize(_apm);
            if (err != 0)
                _logger.LogWarning("APM re-init returned {Error}", err);

            int delayMs = _mobileMode ? MobileStreamDelayMs : DesktopStreamDelayMs;
            WebRtcApmInterop.SetStreamDelayMs(_apm, delayMs);

            // Clear residuals
            _captureResidual.Clear();
            _renderResidual.Clear();

            // Reset statistics tick count
            _statsTickCount = 0;

            _logger.LogDebug("APM render reference reset");
        }
    }

    /// <summary>
    /// Update the stream delay estimate for the APM echo canceller.
    /// Call when the active audio output changes or the route changes.
    /// </summary>
    public void UpdateStreamDelay(int totalDelayMs)
    {
        if (!_initialized) return;
        lock (_lock)
        {
            if (_disposed) return;
            int clamped = Math.Clamp(totalDelayMs, 10, 500);
            WebRtcApmInterop.SetStreamDelayMs(_apm, clamped);
            _logger.LogInformation("APM stream delay set to {DelayMs}ms", clamped);
        }
    }

    /// <summary>
    /// Process a microphone capture chunk through AEC/NS/AGC.
    /// Input: PCM16 mono at 48kHz. Output: processed PCM16 mono at 48kHz.
    /// </summary>
    public byte[] ProcessCapture(byte[] pcm16At48k)
    {
        if (!IsEnabled || !_initialized || pcm16At48k.Length == 0)
            return pcm16At48k;

        lock (_lock)
        {
            if (_disposed) return pcm16At48k;

            try
            {
                // Convert PCM16 → float
                int newSampleCount = pcm16At48k.Length / 2;
                float[] newSamples = Pcm16ToFloat(pcm16At48k, newSampleCount);

                // Prepend residual from previous call
                int totalSamples = _captureResidual.Count + newSampleCount;
                float[] allSamples = new float[totalSamples];
                _captureResidual.CopyTo(allSamples);
                Array.Copy(newSamples, 0, allSamples, _captureResidual.Count, newSampleCount);

                // Process in 10ms frames
                int processedSamples = 0;
                for (int offset = 0; offset + FrameSamples <= totalSamples; offset += FrameSamples)
                {
                    Array.Copy(allSamples, offset, _srcFrame, 0, FrameSamples);
                    ProcessStreamFrame(_srcFrame, _destFrame);
                    Array.Copy(_destFrame, 0, allSamples, offset, FrameSamples);
                    processedSamples += FrameSamples;
                }

                // Save tail for next call
                _captureResidual.Clear();
                int tailSamples = totalSamples - processedSamples;
                if (tailSamples > 0)
                {
                    for (int i = 0; i < tailSamples; i++)
                        _captureResidual.Add(allSamples[processedSamples + i]);
                }

                // Convert float → PCM16
                byte[] result = FloatToPcm16(allSamples, processedSamples);

                // Track for clock drift monitoring (Phase 6.2)
                _drift?.RecordCaptureSamples(result.Length / 2);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AEC ProcessCapture failed, returning unprocessed audio");
                return pcm16At48k;
            }
        }
    }

    /// <summary>
    /// Feed speaker (render) audio as the echo reference signal.
    /// Input: PCM16 mono at 48kHz.
    /// </summary>
    public void FeedRenderReference(byte[] pcm16At48k)
    {
        if (!IsEnabled || !_initialized || pcm16At48k.Length == 0)
            return;

        lock (_lock)
        {
            if (_disposed) return;

            try
            {
                // Convert PCM16 → float
                int newSampleCount = pcm16At48k.Length / 2;
                float[] newSamples = Pcm16ToFloat(pcm16At48k, newSampleCount);

                // Prepend residual from previous call
                int totalSamples = _renderResidual.Count + newSampleCount;
                float[] allSamples = new float[totalSamples];
                _renderResidual.CopyTo(allSamples);
                Array.Copy(newSamples, 0, allSamples, _renderResidual.Count, newSampleCount);

                // Process in 10ms frames
                int processedSamples = 0;
                for (int offset = 0; offset + FrameSamples <= totalSamples; offset += FrameSamples)
                {
                    Array.Copy(allSamples, offset, _srcFrame, 0, FrameSamples);
                    ProcessReverseStreamFrame(_srcFrame, _destFrame);
                    processedSamples += FrameSamples;
                }

                // Save tail for next call
                _renderResidual.Clear();
                int tailSamples = totalSamples - processedSamples;
                if (tailSamples > 0)
                {
                    for (int i = 0; i < tailSamples; i++)
                        _renderResidual.Add(allSamples[processedSamples + i]);
                }

                // Track for clock drift monitoring (Phase 6.2)
                _drift?.RecordRenderSamples(processedSamples);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AEC FeedRenderReference failed");
            }
        }
    }

    /// <summary>
    /// Get current APM statistics (ERLE, residual echo, etc.).
    /// Returns null if APM is not initialized, disposed, or if the native library doesn't export GetStatistics.
    /// </summary>
    public ApmStatistics? GetStatistics()
    {
        if (!_initialized) return null;
        lock (_lock)
        {
            if (_disposed) return null;
            try
            {
                int err = WebRtcApmInterop.GetStatistics(_apm, out var s);
                return err == 0 ? s : (ApmStatistics?)null;
            }
            catch (EntryPointNotFoundException)
            {
                // Native library doesn't export webrtc_apm_get_statistics
                return null;
            }
        }
    }

    private unsafe void ProcessStreamFrame(float[] src, float[] dest)
    {
        fixed (float* pSrc = src)
        fixed (float* pDest = dest)
        fixed (IntPtr* pSrcArr = _srcPtrSlot)
        fixed (IntPtr* pDestArr = _destPtrSlot)
        {
            _srcPtrSlot[0] = (IntPtr)pSrc;
            _destPtrSlot[0] = (IntPtr)pDest;

            int err = WebRtcApmInterop.ProcessStream(
                _apm,
                (IntPtr)pSrcArr,
                _streamConfig,
                _streamConfig,
                (IntPtr)pDestArr);

            if (err != 0)
                _logger.LogTrace("ProcessStream error {Error}", err);
        }
    }

    private unsafe void ProcessReverseStreamFrame(float[] src, float[] dest)
    {
        fixed (float* pSrc = src)
        fixed (float* pDest = dest)
        fixed (IntPtr* pSrcArr = _srcPtrSlot)
        fixed (IntPtr* pDestArr = _destPtrSlot)
        {
            _srcPtrSlot[0] = (IntPtr)pSrc;
            _destPtrSlot[0] = (IntPtr)pDest;

            int err = WebRtcApmInterop.ProcessReverseStream(
                _apm,
                (IntPtr)pSrcArr,
                _streamConfig,
                _streamConfig,
                (IntPtr)pDestArr);

            if (err != 0)
                _logger.LogTrace("ProcessReverseStream error {Error}", err);
        }
    }

    private static float[] Pcm16ToFloat(byte[] pcm16, int sampleCount)
    {
        var floats = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(pcm16, i * 2);
            floats[i] = sample / 32768f;
        }
        return floats;
    }

    private static byte[] FloatToPcm16(float[] floats, int sampleCount)
    {
        var pcm16 = new byte[sampleCount * 2];
        for (int i = 0; i < sampleCount; i++)
        {
            int value = (int)(floats[i] * 32768f);
            short clamped = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
            BitConverter.TryWriteBytes(pcm16.AsSpan(i * 2), clamped);
        }
        return pcm16;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            _statsTimer?.Stop();
            _statsTimer?.Dispose();
            _statsTimer = null;

            if (_streamConfig != IntPtr.Zero)
            {
                WebRtcApmInterop.StreamConfigDestroy(_streamConfig);
                _streamConfig = IntPtr.Zero;
            }

            if (_apm != IntPtr.Zero)
            {
                WebRtcApmInterop.Destroy(_apm);
                _apm = IntPtr.Zero;
            }

            _initialized = false;
            _logger.LogInformation("WebRTC APM disposed");
        }
    }
}
