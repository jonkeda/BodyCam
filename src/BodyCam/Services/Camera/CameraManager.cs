using BodyCam.Services.Glasses.HeyCyan;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Camera;

/// <summary>
/// Manages the active camera provider and exposes capture for the orchestrator.
/// </summary>
public sealed class CameraManager
{
    private readonly IReadOnlyList<ICameraProvider> _providers;
    private readonly ISettingsService _settings;
    private readonly ICameraProviderSelector _selector;
    private readonly IHeyCyanGlassesSession? _heyCyanSession;
    private readonly ILogger<CameraManager> _log;
    private ICameraProvider? _active;
    private CancellationTokenSource? _currentCaptureCts;

    public CameraManager(
        IEnumerable<ICameraProvider> providers,
        ISettingsService settings,
        ICameraProviderSelector selector,
        ILogger<CameraManager> log,
        IHeyCyanGlassesSession? heyCyanSession = null)
    {
        _providers = providers.ToList().AsReadOnly();
        _settings = settings;
        _selector = selector;
        _log = log;
        _heyCyanSession = heyCyanSession;

        // Hot-swap on HeyCyan session state change (Android-only; null on other platforms)
        if (_heyCyanSession is not null)
        {
            _heyCyanSession.StateChanged += (_, state) =>
            {
                _log.LogInformation("HeyCyan state changed to {State}; reselecting camera", state);
                _ = ReselectActiveProviderAsync();
            };
        }
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
        _active.Disconnected += OnProviderDisconnected;
        try
        {
            await _active.StartAsync(ct);
            _settings.ActiveCameraProvider = providerId;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _active.Disconnected -= OnProviderDisconnected;
            _active = null;
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Camera provider {ProviderId} failed to start; continuing without an active camera",
                providerId);
            _active.Disconnected -= OnProviderDisconnected;
            _active = null;

            try
            {
                await provider.StopAsync();
            }
            catch (Exception stopEx)
            {
                _log.LogDebug(stopEx, "Camera provider {ProviderId} failed to stop after start failure",
                    providerId);
            }
        }
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

        // Create a linked CTS so we can cancel in-flight captures on provider swap
        _currentCaptureCts?.Cancel();
        _currentCaptureCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            return _active is not null
                ? await _active.CaptureFrameAsync(_currentCaptureCts.Token)
                : null;
        }
        finally
        {
            _currentCaptureCts?.Dispose();
            _currentCaptureCts = null;
        }
    }

    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_active is null)
        {
            await FallbackToPhoneAsync(ct).ConfigureAwait(false);
        }

        if (_active is null)
            yield break;

        await foreach (var frame in _active.StreamFramesAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            yield return frame;
        }
    }

    /// <summary>
    /// Restore the last active provider or default to "phone".
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var providerId = _settings.ActiveCameraProvider ?? "phone";
        await SetActiveAsync(providerId, ct);
    }

    /// <summary>
    /// Re-run provider selection using the registered selector strategy.
    /// Used when HeyCyan session state changes (connected/disconnected).
    /// </summary>
    public async Task ReselectActiveProviderAsync(CancellationToken ct = default)
    {
        var selected = _selector.Select(_providers);
        if (selected != _active)
        {
            _log.LogInformation("Reselecting camera provider: {Old} -> {New}",
                _active?.ProviderId ?? "none", selected.ProviderId);

            // Cancel any in-flight capture
            _currentCaptureCts?.Cancel();

            // If demoting HeyCyan provider, exit transfer mode cleanly
            if (_active?.ProviderId == "heycyan-glasses" && _heyCyanSession is not null)
            {
                try
                {
                    await _heyCyanSession.ExitTransferModeAsync(ct);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to exit HeyCyan transfer mode during reselection");
                }
            }

            await SetActiveAsync(selected.ProviderId, ct);
        }
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
