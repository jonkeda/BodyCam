using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Audio.WebRtcApm;

/// <summary>
/// Processes microphone audio through WebRTC APM to remove echo, noise, and apply gain control.
/// Thread-safe — all native calls are serialized.
/// </summary>
public sealed class AecProcessor : IDisposable
{
    private const int ApmRate = 48000;
    private const int AppRate = 24000;
    private const int Channels = 1;
    private const int FrameSamples = 480; // 10ms at 48kHz
    private const int StreamDelayMs = 40;

    private readonly ILogger<AecProcessor> _logger;
    private readonly object _lock = new();

    private IntPtr _apm;
    private IntPtr _streamConfig;
    private bool _initialized;
    private bool _disposed;

    // Pre-allocated frame buffers (reused across calls to avoid GC pressure)
    private readonly float[] _srcFrame = new float[FrameSamples];
    private readonly float[] _destFrame = new float[FrameSamples];

    public bool IsEnabled { get; set; } = true;

    public AecProcessor(ILogger<AecProcessor> logger)
    {
        _logger = logger;
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
                WebRtcApmInterop.ConfigSetNoiseSuppression(config, 1, 2); // High
                WebRtcApmInterop.ConfigSetHighPassFilter(config, 1);
                WebRtcApmInterop.ConfigSetGainController1(config, 1, 1, 3, 9, 1);
                // mode=1 (AdaptiveDigital), target=-3dBFS, compression=9dB, limiter=on

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

            WebRtcApmInterop.SetStreamDelayMs(_apm, StreamDelayMs);

            _initialized = true;
            _logger.LogInformation("WebRTC APM initialized (mobileMode={Mobile})", mobileMode);
        }
    }

    /// <summary>
    /// Process a microphone capture chunk through AEC/NS/AGC.
    /// Input: PCM16 mono at 24kHz. Output: processed PCM16 mono at 24kHz.
    /// </summary>
    public byte[] ProcessCapture(byte[] pcm16At24k)
    {
        if (!IsEnabled || !_initialized || pcm16At24k.Length == 0)
            return pcm16At24k;

        lock (_lock)
        {
            if (_disposed) return pcm16At24k;

            try
            {
                // Resample 24k → 48k
                byte[] pcm16At48k = AudioResampler.Resample(pcm16At24k, AppRate, ApmRate);

                // Convert PCM16 → float
                int totalSamples = pcm16At48k.Length / 2;
                float[] allSamples = Pcm16ToFloat(pcm16At48k, totalSamples);

                // Process in 10ms frames
                int framesProcessed = 0;
                for (int offset = 0; offset + FrameSamples <= totalSamples; offset += FrameSamples)
                {
                    Array.Copy(allSamples, offset, _srcFrame, 0, FrameSamples);
                    ProcessStreamFrame(_srcFrame, _destFrame);
                    Array.Copy(_destFrame, 0, allSamples, offset, FrameSamples);
                    framesProcessed++;
                }

                // Convert float → PCM16
                byte[] resultAt48k = FloatToPcm16(allSamples, totalSamples);

                // Resample 48k → 24k
                return AudioResampler.Resample(resultAt48k, ApmRate, AppRate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AEC ProcessCapture failed, returning unprocessed audio");
                return pcm16At24k;
            }
        }
    }

    /// <summary>
    /// Feed speaker (render) audio as the echo reference signal.
    /// Input: PCM16 mono at 24kHz.
    /// </summary>
    public void FeedRenderReference(byte[] pcm16At24k)
    {
        if (!IsEnabled || !_initialized || pcm16At24k.Length == 0)
            return;

        lock (_lock)
        {
            if (_disposed) return;

            try
            {
                byte[] pcm16At48k = AudioResampler.Resample(pcm16At24k, AppRate, ApmRate);
                int totalSamples = pcm16At48k.Length / 2;
                float[] allSamples = Pcm16ToFloat(pcm16At48k, totalSamples);

                for (int offset = 0; offset + FrameSamples <= totalSamples; offset += FrameSamples)
                {
                    Array.Copy(allSamples, offset, _srcFrame, 0, FrameSamples);
                    ProcessReverseStreamFrame(_srcFrame, _destFrame);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AEC FeedRenderReference failed");
            }
        }
    }

    private void ProcessStreamFrame(float[] src, float[] dest)
    {
        GCHandle srcDataHandle = GCHandle.Alloc(src, GCHandleType.Pinned);
        GCHandle destDataHandle = GCHandle.Alloc(dest, GCHandleType.Pinned);
        try
        {
            IntPtr srcPtr = srcDataHandle.AddrOfPinnedObject();
            IntPtr destPtr = destDataHandle.AddrOfPinnedObject();

            // Native expects float** (array of channel pointers). For mono: &srcPtr.
            IntPtr[] srcPtrs = [srcPtr];
            IntPtr[] destPtrs = [destPtr];

            GCHandle srcArrayHandle = GCHandle.Alloc(srcPtrs, GCHandleType.Pinned);
            GCHandle destArrayHandle = GCHandle.Alloc(destPtrs, GCHandleType.Pinned);
            try
            {
                int err = WebRtcApmInterop.ProcessStream(
                    _apm,
                    srcArrayHandle.AddrOfPinnedObject(),
                    _streamConfig,
                    _streamConfig,
                    destArrayHandle.AddrOfPinnedObject());

                if (err != 0)
                    _logger.LogTrace("ProcessStream error {Error}", err);
            }
            finally
            {
                srcArrayHandle.Free();
                destArrayHandle.Free();
            }
        }
        finally
        {
            srcDataHandle.Free();
            destDataHandle.Free();
        }
    }

    private void ProcessReverseStreamFrame(float[] src, float[] dest)
    {
        GCHandle srcDataHandle = GCHandle.Alloc(src, GCHandleType.Pinned);
        GCHandle destDataHandle = GCHandle.Alloc(dest, GCHandleType.Pinned);
        try
        {
            IntPtr srcPtr = srcDataHandle.AddrOfPinnedObject();
            IntPtr destPtr = destDataHandle.AddrOfPinnedObject();

            IntPtr[] srcPtrs = [srcPtr];
            IntPtr[] destPtrs = [destPtr];

            GCHandle srcArrayHandle = GCHandle.Alloc(srcPtrs, GCHandleType.Pinned);
            GCHandle destArrayHandle = GCHandle.Alloc(destPtrs, GCHandleType.Pinned);
            try
            {
                int err = WebRtcApmInterop.ProcessReverseStream(
                    _apm,
                    srcArrayHandle.AddrOfPinnedObject(),
                    _streamConfig,
                    _streamConfig,
                    destArrayHandle.AddrOfPinnedObject());

                if (err != 0)
                    _logger.LogTrace("ProcessReverseStream error {Error}", err);
            }
            finally
            {
                srcArrayHandle.Free();
                destArrayHandle.Free();
            }
        }
        finally
        {
            srcDataHandle.Free();
            destDataHandle.Free();
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
