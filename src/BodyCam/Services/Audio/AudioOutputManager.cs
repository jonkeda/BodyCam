using BodyCam.Services;

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
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IAudioOutputProvider? _active;

    public AudioOutputManager(IEnumerable<IAudioOutputProvider> providers, ISettingsService settings, AppSettings appSettings)
    {
        _providers = providers.ToList();
        _settings = settings;
        _appSettings = appSettings;
    }

    /// <summary>All registered audio output providers.</summary>
    public IReadOnlyList<IAudioOutputProvider> Providers => _providers.AsReadOnly();

    /// <summary>Fires when providers are added or removed (BT connect/disconnect).</summary>
    public event EventHandler? ProvidersChanged;

    /// <summary>Currently active audio output provider.</summary>
    public IAudioOutputProvider? Active => _active;

    // IAudioOutputService implementation
    public bool IsPlaying => _active?.IsPlaying ?? false;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_active is null)
            await FallbackToDefaultAsync(ct);

        if (_active is not null)
            await _active.StartAsync(_appSettings.SampleRate, ct);
    }

    public async Task StopAsync()
    {
        if (_active is not null)
            await _active.StopAsync();
    }

    public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        return _active is not null
            ? _active.PlayChunkAsync(pcmData, ct)
            : Task.CompletedTask;
    }

    public void ClearBuffer()
    {
        _active?.ClearBuffer();
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

    private async Task SetActiveCoreAsync(string providerId, CancellationToken ct = default)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderId == providerId);
        if (provider is null) return;

        if (_active is not null)
        {
            _active.Disconnected -= OnProviderDisconnected;
            await _active.StopAsync();
        }

        _active = provider;
        _settings.ActiveAudioOutputProvider = providerId;
        _active.Disconnected += OnProviderDisconnected;
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
        if (_active is not null)
        {
            _active.Disconnected -= OnProviderDisconnected;
            await _active.StopAsync();
        }

        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }
    }
}
