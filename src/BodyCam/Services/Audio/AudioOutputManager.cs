using BodyCam.Services;
using BodyCam.Services.Audio.WebRtcApm;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Audio;

/// <summary>
/// Manages the active audio output provider and implements <see cref="IAudioOutputService"/>
/// for backward compatibility with VoiceOutputAgent.
/// </summary>
public sealed class AudioOutputManager : IAudioOutputService, IAsyncDisposable
{
    private readonly List<IAudioOutputProvider> _providers;
    private readonly ISettingsService _settings;
    private readonly AppSettings _appSettings;
    private readonly IAecProcessor? _aec;
    private readonly ILogger<AudioOutputManager> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IAudioOutputProvider? _active;
    private JitterBuffer? _jitterBuffer;
    private Task? _drainTask;
    private CancellationTokenSource? _drainCts;

    public AudioOutputManager(
        IEnumerable<IAudioOutputProvider> providers,
        ISettingsService settings,
        AppSettings appSettings,
        ILogger<AudioOutputManager> logger,
        IAecProcessor? aec = null)
    {
        _providers = providers.ToList();
        _settings = settings;
        _appSettings = appSettings;
        _aec = aec;
        _logger = logger;
    }

    /// <summary>All registered audio output providers.</summary>
    public IReadOnlyList<IAudioOutputProvider> Providers => _providers.AsReadOnly();

    /// <summary>Fires when providers are added or removed (BT connect/disconnect).</summary>
    public event EventHandler? ProvidersChanged;

    /// <summary>Currently active audio output provider.</summary>
    public IAudioOutputProvider? Active => _active;

    /// <summary>Provider ID of the currently active audio output provider, or null if none.</summary>
    public string? ActiveProviderId => _active?.ProviderId;

    // IAudioOutputService implementation
    public bool IsPlaying => _active?.IsPlaying ?? false;

    /// <summary>
    /// Jitter buffer metrics (null if disabled).
    /// </summary>
    public long JitterBufferUnderruns => _jitterBuffer?.Underruns ?? 0;
    public long JitterBufferOverflows => _jitterBuffer?.Overflows ?? 0;
    public int  JitterBufferTargetMs  => _jitterBuffer?.CurrentTargetMs ?? 0;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_active is null)
            await FallbackToDefaultAsync(ct);

        if (_active is not null)
        {
            await _active.StartAsync(_appSettings.SampleRate, ct);

            if (_appSettings.EnableJitterBuffer)
            {
                // Start jitter buffer drain task
                var loggerFactory = LoggerFactory.Create(builder => { });
                _jitterBuffer = new JitterBuffer(loggerFactory.CreateLogger<JitterBuffer>());
                _drainCts = new CancellationTokenSource();
                _drainTask = Task.Run(() => _jitterBuffer.DrainToProviderAsync(_active, _appSettings.SampleRate, _drainCts.Token), _drainCts.Token);
            }
        }
    }

    public async Task StopAsync()
    {
        // Stop drain task first
        if (_drainCts is not null)
        {
            _drainCts.Cancel();
            if (_drainTask is not null)
            {
                try { await _drainTask; }
                catch (OperationCanceledException) { }
            }
            _drainCts.Dispose();
            _drainCts = null;
            _drainTask = null;
        }

        _jitterBuffer?.Dispose();
        _jitterBuffer = null;

        if (_active is not null)
            await _active.StopAsync();
    }

    public async Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (_active is null) return;

        if (_jitterBuffer is not null)
            await _jitterBuffer.EnqueueAsync(pcmData, ct);
        else
            await _active.PlayChunkAsync(pcmData, ct);
    }

    public void ClearBuffer()
    {
        _jitterBuffer?.Clear();
        _active?.ClearBuffer();
    }

    /// <summary>
    /// Phase 5.4: Fade out buffered audio before clearing to prevent click on interruption.
    /// </summary>
    public async Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default)
    {
        _jitterBuffer?.Clear();
        if (_active is not null)
            await _active.FadeOutAndClearAsync(fadeMs, ct);
    }

    /// <summary>
    /// Stop the current provider, activate the new one, and persist the choice.
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
    /// Stop the current provider, activate the new one, and persist the choice.
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
                throw new InvalidOperationException($"Audio output provider '{providerId}' not found.");

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
            _active.Disconnected -= OnProviderDisconnected;
            _active.OutputRouteChanged -= OnOutputRouteChanged;
            await _active.StopAsync();
        }

        _active = provider;
        _settings.ActiveAudioOutputProvider = providerId;
        _active.Disconnected += OnProviderDisconnected;
        _active.OutputRouteChanged += OnOutputRouteChanged;

        // Update AEC stream delay
        _aec?.UpdateStreamDelay(_active.EstimatedOutputLatencyMs);
    }

    /// <summary>
    /// Restore the last active provider or default to the first available.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var providerId = _settings.ActiveAudioOutputProvider;
        if (providerId is not null)
        {
            await SetActiveAsync(providerId, ct);
        }
        else
        {
            await FallbackToDefaultAsync(ct);
        }
    }

    private async void OnProviderDisconnected(object? sender, EventArgs e)
    {
        try
        {
            await FallbackToDefaultAsync();
        }
        catch (Exception)
        {
            // Best-effort fallback
        }
    }

    private void OnOutputRouteChanged(object? sender, EventArgs e)
    {
        if (_active is not null)
            _aec?.UpdateStreamDelay(_active.EstimatedOutputLatencyMs);
    }

    private async Task FallbackToDefaultAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await FallbackToDefaultCoreAsync(ct);
        }
        finally { _lock.Release(); }
    }

    private async Task FallbackToDefaultCoreAsync(CancellationToken ct = default)
    {
        var fallback = _providers.FirstOrDefault(p => p.IsAvailable);
        if (fallback is not null && fallback != _active)
        {
            await SetActiveCoreAsync(fallback.ProviderId, ct);
        }
    }

    /// <summary>
    /// Register a dynamically discovered provider (e.g. BT speaker connected after startup).
    /// Auto-switches if this is the user's saved preference and the current active is a platform default.
    /// </summary>
    public void RegisterProvider(IAudioOutputProvider provider)
    {
        _lock.Wait();
        try
        {
            if (_providers.Any(p => p.ProviderId == provider.ProviderId))
                return;

            _providers.Add(provider);
            ProvidersChanged?.Invoke(this, EventArgs.Empty);

            // Auto-switch if user's saved preference matches and we're on platform fallback
            if (provider.ProviderId == _settings.ActiveAudioOutputProvider
                && (_active is null || _active.ProviderId == "windows-speaker" || _active.ProviderId == "phone-speaker"))
            {
                _ = SetActiveCoreAsync(provider.ProviderId);
            }
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Remove a dynamically discovered provider (e.g. BT speaker disconnected).
    /// If it was active, falls back to platform default.
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
                await FallbackToDefaultCoreAsync();
            }

            _providers.Remove(provider);
            await provider.DisposeAsync();
            ProvidersChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _lock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        // Stop drain task first
        if (_drainCts is not null)
        {
            _drainCts.Cancel();
            if (_drainTask is not null)
            {
                try { await _drainTask; }
                catch (OperationCanceledException) { }
            }
            _drainCts.Dispose();
        }

        _jitterBuffer?.Dispose();

        if (_active is not null)
        {
            _active.Disconnected -= OnProviderDisconnected;
            _active.OutputRouteChanged -= OnOutputRouteChanged;
            await _active.StopAsync();
        }

        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }
    }
}
