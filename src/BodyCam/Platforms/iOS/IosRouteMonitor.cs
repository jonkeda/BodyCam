using AVFoundation;
using BodyCam.Services.Audio;
using Foundation;
using Microsoft.Extensions.Logging;

namespace BodyCam.Platforms.iOS;

/// <summary>
/// Monitors audio route changes on iOS via AVAudioSession notifications.
/// </summary>
public class IosRouteMonitor : IRouteMonitor
{
    private readonly ILogger<IosRouteMonitor> _logger;
    private NSObject? _routeChangeObserver;

    public bool IsHeadphonesConnected { get; private set; }
    public bool IsBluetoothAudioConnected { get; private set; }

    public event EventHandler? RouteChanged;

    public IosRouteMonitor(ILogger<IosRouteMonitor> logger)
    {
        _logger = logger;

        _routeChangeObserver = AVAudioSession.Notifications.ObserveRouteChange((sender, args) =>
        {
            RefreshRouteState();
            RouteChanged?.Invoke(this, EventArgs.Empty);
        });

        RefreshRouteState();
    }

    private void RefreshRouteState()
    {
        var session = AVAudioSession.SharedInstance();
        var outputs = session.CurrentRoute?.Outputs;
        if (outputs == null || outputs.Length == 0)
        {
            IsHeadphonesConnected = false;
            IsBluetoothAudioConnected = false;
            return;
        }

        IsHeadphonesConnected = outputs.Any(o =>
            o.PortType == AVAudioSession.PortHeadphones ||
            o.PortType == AVAudioSession.PortBluetoothA2DP ||
            o.PortType == AVAudioSession.PortBluetoothHfp ||
            o.PortType == AVAudioSession.PortBluetoothLE);

        IsBluetoothAudioConnected = outputs.Any(o =>
            o.PortType == AVAudioSession.PortBluetoothA2DP ||
            o.PortType == AVAudioSession.PortBluetoothHfp ||
            o.PortType == AVAudioSession.PortBluetoothLE);

        _logger.LogInformation("Route changed: Headphones={Headphones}, Bluetooth={Bluetooth}",
            IsHeadphonesConnected, IsBluetoothAudioConnected);
    }

    public ValueTask DisposeAsync()
    {
        _routeChangeObserver?.Dispose();
        _routeChangeObserver = null;
        return ValueTask.CompletedTask;
    }
}
