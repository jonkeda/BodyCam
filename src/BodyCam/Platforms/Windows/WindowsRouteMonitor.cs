using BodyCam.Services.Audio;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace BodyCam.Platforms.Windows;

/// <summary>
/// Monitors audio route changes on Windows via MMDeviceEnumerator.
/// </summary>
public class WindowsRouteMonitor : IRouteMonitor
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly NotificationClient _notificationClient;

    public bool IsHeadphonesConnected { get; private set; }
    public bool IsBluetoothAudioConnected { get; private set; }

    public event EventHandler? RouteChanged;

    public WindowsRouteMonitor()
    {
        _enumerator = new MMDeviceEnumerator();
        _notificationClient = new NotificationClient(this);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
        RefreshRouteState();
    }

    private void RefreshRouteState()
    {
        try
        {
            var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (defaultDevice == null)
            {
                IsHeadphonesConnected = false;
                IsBluetoothAudioConnected = false;
                return;
            }

            // Heuristic: check device friendly name for common headphone/BT indicators
            string friendlyName = defaultDevice.FriendlyName ?? "";
            
            IsHeadphonesConnected = friendlyName.Contains("Headphones", StringComparison.OrdinalIgnoreCase) ||
                                    friendlyName.Contains("Headset", StringComparison.OrdinalIgnoreCase) ||
                                    friendlyName.Contains("Earbuds", StringComparison.OrdinalIgnoreCase) ||
                                    friendlyName.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
                                    friendlyName.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);

            IsBluetoothAudioConnected = friendlyName.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            IsHeadphonesConnected = false;
            IsBluetoothAudioConnected = false;
        }
    }

    public ValueTask DisposeAsync()
    {
        _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
        _enumerator.Dispose();
        return ValueTask.CompletedTask;
    }

    private class NotificationClient : IMMNotificationClient
    {
        private readonly WindowsRouteMonitor _monitor;

        public NotificationClient(WindowsRouteMonitor monitor) => _monitor = monitor;

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
            {
                _monitor.RefreshRouteState();
                _monitor.RouteChanged?.Invoke(_monitor, EventArgs.Empty);
            }
        }

        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
