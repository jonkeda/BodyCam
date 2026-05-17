using Android.Content;
using Android.Media;

namespace BodyCam.Platforms.Android;

/// <summary>
/// Monitors audio route changes on Android via AudioDeviceCallback.
/// </summary>
public class AndroidRouteMonitor : BodyCam.Services.Audio.IRouteMonitor
{
    private readonly AudioManager _audioManager;
    private readonly DeviceCallback _callback;

    public bool IsHeadphonesConnected { get; private set; }
    public bool IsBluetoothAudioConnected { get; private set; }

    public event EventHandler? RouteChanged;

    public AndroidRouteMonitor()
    {
        _audioManager = (AudioManager)Platform.AppContext.GetSystemService(Context.AudioService)!;
        _callback = new DeviceCallback(this);

        _audioManager.RegisterAudioDeviceCallback(_callback, null);
        RefreshRouteState();
    }

    private void RefreshRouteState()
    {
        var devices = _audioManager.GetDevices(GetDevicesTargets.Outputs);
        IsHeadphonesConnected = devices.Any(d =>
            d.Type == AudioDeviceType.WiredHeadphones ||
            d.Type == AudioDeviceType.WiredHeadset ||
            d.Type == AudioDeviceType.UsbHeadset ||
            d.Type == AudioDeviceType.BluetoothA2dp ||
            d.Type == AudioDeviceType.BluetoothSco);

        IsBluetoothAudioConnected = devices.Any(d =>
            d.Type == AudioDeviceType.BluetoothA2dp ||
            d.Type == AudioDeviceType.BluetoothSco);
    }

    public ValueTask DisposeAsync()
    {
        _audioManager.UnregisterAudioDeviceCallback(_callback);
        return ValueTask.CompletedTask;
    }

    private class DeviceCallback : AudioDeviceCallback
    {
        private readonly AndroidRouteMonitor _monitor;

        public DeviceCallback(AndroidRouteMonitor monitor) => _monitor = monitor;

        public override void OnAudioDevicesAdded(AudioDeviceInfo[] addedDevices)
        {
            _monitor.RefreshRouteState();
            _monitor.RouteChanged?.Invoke(_monitor, EventArgs.Empty);
        }

        public override void OnAudioDevicesRemoved(AudioDeviceInfo[] removedDevices)
        {
            _monitor.RefreshRouteState();
            _monitor.RouteChanged?.Invoke(_monitor, EventArgs.Empty);
        }
    }
}
