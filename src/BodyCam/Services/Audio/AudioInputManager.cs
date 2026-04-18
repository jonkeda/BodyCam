using BodyCam.Services;

namespace BodyCam.Services.Audio;

/// <summary>
/// Manages the active audio input provider and implements <see cref="IAudioInputService"/>
/// for backward compatibility with VoiceInputAgent and other consumers.
/// </summary>
public sealed class AudioInputManager : IAudioInputService, IAsyncDisposable
{
    private readonly List<IAudioInputProvider> _providers;
    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IAudioInputProvider? _active;

    public event EventHandler<byte[]>? AudioChunkAvailable;

    /// <summary>Fires when providers are added or removed (hot-plug).</summary>
    public event EventHandler? ProvidersChanged;

    public AudioInputManager(IEnumerable<IAudioInputProvider> providers, ISettingsService settings)
    {
        _providers = providers.ToList();
        _settings = settings;
    }

    /// <summary>All registered audio input providers.</summary>
    public IReadOnlyList<IAudioInputProvider> Providers => _providers.AsReadOnly();

    /// <summary>Currently active audio input provider.</summary>
    public IAudioInputProvider? Active => _active;

    // IAudioInputService implementation
    public bool IsCapturing => _active?.IsCapturing ?? false;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_active is null)
            await FallbackToPlatformAsync(ct);

        if (_active is not null)
            await _active.StartAsync(ct);
    }

    public async Task StopAsync()
    {
        if (_active is not null)
            await _active.StopAsync();
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
        AudioChunkAvailable?.Invoke(this, chunk);
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
