using BodyCam.Services.Audio.WebRtcApm;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Audio;

/// <summary>
/// Automatically disables AEC when headphones are connected (no acoustic echo path).
/// Enables AEC when speaker is active.
/// </summary>
public sealed class AecBypassManager : IAsyncDisposable
{
    private readonly IRouteMonitor _monitor;
    private readonly IAecProcessor _aec;
    private readonly ILogger<AecBypassManager> _logger;

    public AecBypassManager(IRouteMonitor monitor, IAecProcessor aec, ILogger<AecBypassManager> logger)
    {
        _monitor = monitor;
        _aec = aec;
        _logger = logger;

        _monitor.RouteChanged += OnRouteChanged;
        UpdateAecState(); // Initial state
    }

    private void OnRouteChanged(object? sender, EventArgs e)
    {
        UpdateAecState();
    }

    private void UpdateAecState()
    {
        // Disable AEC when headphones are connected (no acoustic echo)
        bool isolated = _monitor.IsHeadphonesConnected;
        _aec.IsEnabled = !isolated;
        _logger.LogInformation("Audio route changed; AEC IsEnabled={Enabled} (headphones={Headphones})",
            !isolated, isolated);
    }

    public async ValueTask DisposeAsync()
    {
        _monitor.RouteChanged -= OnRouteChanged;
        await _monitor.DisposeAsync();
    }
}
