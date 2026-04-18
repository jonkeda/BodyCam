namespace BodyCam.Services.Camera;

/// <summary>
/// Manages the active camera provider and exposes capture for the orchestrator.
/// </summary>
public sealed class CameraManager
{
    private readonly IReadOnlyList<ICameraProvider> _providers;
    private readonly ISettingsService _settings;
    private ICameraProvider? _active;

    public CameraManager(IEnumerable<ICameraProvider> providers, ISettingsService settings)
    {
        _providers = providers.ToList().AsReadOnly();
        _settings = settings;
    }

    /// <summary>All registered camera providers.</summary>
    public IReadOnlyList<ICameraProvider> Providers => _providers;

    /// <summary>Currently active camera provider.</summary>
    public ICameraProvider? Active => _active;

    /// <summary>
    /// Stop the current provider, start the new one, and persist the choice.
    /// </summary>
    public async Task SetActiveAsync(string providerId, CancellationToken ct = default)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderId == providerId);
        if (provider is null) return;

        if (_active is not null)
        {
            _active.Disconnected -= OnProviderDisconnected;
            await _active.StopAsync();
        }

        _active = provider;
        _settings.ActiveCameraProvider = providerId;
        _active.Disconnected += OnProviderDisconnected;
        await _active.StartAsync(ct);
    }

    /// <summary>
    /// Capture a frame from the active provider, falling back to phone if none is active.
    /// </summary>
    public async Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        if (_active is null)
        {
            await FallbackToPhoneAsync(ct);
        }

        return _active is not null
            ? await _active.CaptureFrameAsync(ct)
            : null;
    }

    /// <summary>
    /// Restore the last active provider or default to "phone".
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var providerId = _settings.ActiveCameraProvider ?? "phone";
        await SetActiveAsync(providerId, ct);
    }

    private async void OnProviderDisconnected(object? sender, EventArgs e)
    {
        await FallbackToPhoneAsync();
    }

    private async Task FallbackToPhoneAsync(CancellationToken ct = default)
    {
        var phone = _providers.FirstOrDefault(p => p.ProviderId == "phone");
        if (phone is not null && phone != _active)
        {
            await SetActiveAsync("phone", ct);
        }
    }
}
