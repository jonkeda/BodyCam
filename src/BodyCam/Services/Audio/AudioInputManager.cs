using System.Threading.Channels;
using BodyCam.Services;
using BodyCam.Services.Audio.WebRtcApm;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Audio;

/// <summary>
/// Manages the active audio input provider and implements <see cref="IAudioInputService"/>
/// for backward compatibility with VoiceInputAgent and other consumers.
/// </summary>
public sealed class AudioInputManager : IAudioInputService, IAsyncDisposable
{
    private readonly List<IAudioInputProvider> _providers;
    private readonly ISettingsService _settings;
    private readonly IAecProcessor? _aec;
    private readonly ILogger<AudioInputManager> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IAudioInputProvider? _active;

    // Bounded channel for decoupling capture thread from AEC processing
    private readonly Channel<byte[]> _aecChannel;
    private Task? _consumerTask;
    private CancellationTokenSource? _consumerCts;
    private long _droppedAecChunks;

    // Phase 6.3: WAV capture recorder (gated by DebugMode)
    private readonly MicCaptureRecorder? _recorder;

    public event EventHandler<byte[]>? AudioChunkAvailable;

    /// <summary>Fires when providers are added or removed (hot-plug).</summary>
    public event EventHandler? ProvidersChanged;

    /// <summary>
    /// Count of audio chunks dropped due to AEC processing backlog.
    /// Monitored by Phase 6 metrics.
    /// </summary>
    public long DroppedAecChunks => Interlocked.Read(ref _droppedAecChunks);

    public AudioInputManager(
        IEnumerable<IAudioInputProvider> providers,
        ISettingsService settings,
        ILogger<AudioInputManager> logger,
        IAecProcessor? aec = null)
    {
        _providers = providers.ToList();
        _settings = settings;
        _logger = logger;
        _aec = aec;

        // Capacity 10 chunks = 500ms at 50ms/chunk; DropOldest ensures realtime
        _aecChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        // Initialize recorder if DebugMode is on (Phase 6.3)
        if (_settings.DebugMode)
        {
            const int sampleRate = 48000; // Internal pipeline rate
            _recorder = new MicCaptureRecorder(sampleRate, seconds: 10);
            _logger.LogInformation("MicCaptureRecorder initialized (DebugMode=true, sampleRate={Rate})", sampleRate);
        }
    }

    /// <summary>All registered audio input providers.</summary>
    public IReadOnlyList<IAudioInputProvider> Providers => _providers.AsReadOnly();

    /// <summary>Currently active audio input provider.</summary>
    public IAudioInputProvider? Active => _active;

    /// <summary>Provider ID of the currently active audio input provider, or null if none.</summary>
    public string? ActiveProviderId => _active?.ProviderId;

    /// <summary>
    /// True if the active provider has platform-native AEC (e.g., iOS VoiceProcessingIO, Android AcousticEchoCanceler).
    /// When true, WebRTC APM should be bypassed to avoid double-processing.
    /// </summary>
    public bool IsPlatformAecActive
    {
        get
        {
#if IOS
            if (_active is BodyCam.Platforms.iOS.PlatformMicProvider iosMic)
                return iosMic.HasPlatformAec;
#elif ANDROID
            // Android PlatformMicProvider always has hardware AEC when available
            if (_active?.ProviderId == "platform")
                return true;
#endif
            return false;
        }
    }

    // IAudioInputService implementation
    public bool IsCapturing => _active?.IsCapturing ?? false;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_active is null)
            await FallbackToPlatformAsync(ct);

        if (_active is not null)
        {
            // Start AEC consumer task
            _consumerCts = new CancellationTokenSource();
            _consumerTask = Task.Run(() => ConsumerLoopAsync(_consumerCts.Token), _consumerCts.Token);

            await _active.StartAsync(ct);
        }
    }

    public async Task StopAsync()
    {
        if (_active is not null)
            await _active.StopAsync();

        // Stop AEC consumer task
        if (_consumerCts is not null)
        {
            _consumerCts.Cancel();
            if (_consumerTask is not null)
            {
                try { await _consumerTask; }
                catch (OperationCanceledException) { }
            }
            _consumerCts.Dispose();
            _consumerCts = null;
            _consumerTask = null;
        }
    }

    /// <summary>
    /// Stop the current provider, start the new one, and persist the choice.
    /// </summary>
    public async Task SetActiveAsync(string providerId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await SetActiveCoreAsync(providerId, ct);
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Stop the current provider, start the new one, and persist the choice.
    /// Throws <see cref="InvalidOperationException"/> if the provider ID is unknown.
    /// No-ops if the provider is already active.
    /// </summary>
    public async Task SetActiveProviderAsync(string providerId, CancellationToken ct = default)
    {
        if (_active?.ProviderId == providerId)
            return;

        await _lock.WaitAsync(ct);
        try
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderId == providerId);
            if (provider is null)
                throw new InvalidOperationException($"Audio input provider '{providerId}' not found.");

            await SetActiveCoreAsync(providerId, ct);
        }
        finally { _lock.Release(); }
    }

    private async Task SetActiveCoreAsync(string providerId, CancellationToken ct = default)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderId == providerId);
        if (provider is null) return;

        if (_active is not null)
        {
            _active.AudioChunkAvailable -= OnProviderChunk;
            _active.Disconnected -= OnProviderDisconnected;
            await _active.StopAsync();
        }

        _active = provider;
        _settings.ActiveAudioInputProvider = providerId;
        _active.AudioChunkAvailable += OnProviderChunk;
        _active.Disconnected += OnProviderDisconnected;
    }

    /// <summary>
    /// Restore the last active provider or default to "platform".
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var providerId = _settings.ActiveAudioInputProvider ?? "platform";
        await SetActiveAsync(providerId, ct);
    }

    /// <summary>
    /// Register a dynamically discovered provider (e.g. BT device connected after startup).
    /// Auto-switches if this is the user's saved preference and the current active is "platform".
    /// </summary>
    public void RegisterProvider(IAudioInputProvider provider)
    {
        _lock.Wait();
        try
        {
            if (_providers.Any(p => p.ProviderId == provider.ProviderId))
                return;

            _providers.Add(provider);
            ProvidersChanged?.Invoke(this, EventArgs.Empty);

            // Auto-switch if user's saved preference matches and we're on platform fallback
            if (provider.ProviderId == _settings.ActiveAudioInputProvider
                && (_active is null || _active.ProviderId == "platform"))
            {
                _ = SetActiveCoreAsync(provider.ProviderId);
            }
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Remove a dynamically discovered provider (e.g. BT device disconnected).
    /// If it was active, falls back to platform.
    /// </summary>
    public async Task UnregisterProviderAsync(string providerId)
    {
        await _lock.WaitAsync();
        try
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderId == providerId);
            if (provider is null) return;

            if (_active?.ProviderId == providerId)
            {
                await FallbackToPlatformCoreAsync();
            }

            _providers.Remove(provider);
            await provider.DisposeAsync();
            ProvidersChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _lock.Release(); }
    }

    private void OnProviderChunk(object? sender, byte[] chunk)
    {
        // Non-blocking write to AEC channel; drop oldest on overflow
        if (!_aecChannel.Writer.TryWrite(chunk))
        {
            Interlocked.Increment(ref _droppedAecChunks);
            _logger.LogWarning("AEC channel full, dropped chunk (total drops: {Drops})", _droppedAecChunks);
        }
    }

    /// <summary>
    /// Consumer loop that drains the AEC channel and processes audio.
    /// Runs on a dedicated task to decouple from capture thread.
    /// </summary>
    private async Task ConsumerLoopAsync(CancellationToken ct)
    {
        await foreach (var chunk in _aecChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                // Skip APM if platform has native AEC (iOS VoiceProcessingIO, Android hardware AEC)
                byte[] processed = (_aec is not null && !IsPlatformAecActive)
                    ? _aec.ProcessCapture(chunk)
                    : chunk;

                // Record to WAV buffer if DebugMode is on (Phase 6.3)
                _recorder?.RecordChunk(processed);

                AudioChunkAvailable?.Invoke(this, processed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AEC processing failed");
            }
        }
    }

    private async void OnProviderDisconnected(object? sender, EventArgs e)
    {
        try
        {
            await FallbackToPlatformAsync();
        }
        catch (Exception)
        {
            // Best-effort fallback
        }
    }

    /// <summary>
    /// Save the last 10 seconds of captured audio to a WAV file.
    /// Only available when DebugMode is enabled.
    /// Phase 6.3: A/B testing and regression analysis.
    /// </summary>
    public void SaveCaptureToWav(string path)
    {
        if (_recorder is null)
            throw new InvalidOperationException("WAV capture is disabled. Enable DebugMode in AppSettings.");
        _recorder.SaveToWav(path);
        _logger.LogInformation("Saved {Seconds}s of audio to {Path}", 10, path);
    }

    private async Task FallbackToPlatformAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await FallbackToPlatformCoreAsync(ct);
        }
        finally { _lock.Release(); }
    }

    private async Task FallbackToPlatformCoreAsync(CancellationToken ct = default)
    {
        var platform = _providers.FirstOrDefault(p => p.ProviderId == "platform");
        if (platform is not null && platform != _active)
        {
            await SetActiveCoreAsync("platform", ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Stop consumer first
        if (_consumerCts is not null)
        {
            _consumerCts.Cancel();
            if (_consumerTask is not null)
            {
                try { await _consumerTask; }
                catch (OperationCanceledException) { }
            }
            _consumerCts.Dispose();
        }

        _aecChannel.Writer.TryComplete();

        if (_active is not null)
        {
            _active.AudioChunkAvailable -= OnProviderChunk;
            _active.Disconnected -= OnProviderDisconnected;
            await _active.StopAsync();
        }

        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }
    }
}
