using BodyCam.Services.Audio;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace BodyCam.Platforms.Windows.Audio;

/// <summary>
/// Discovers Bluetooth audio render endpoints via the MMDevice API and registers
/// them as <see cref="IAudioOutputProvider"/> instances with <see cref="AudioOutputManager"/>.
/// </summary>
public sealed class WindowsBluetoothOutputEnumerator : IDisposable
{
    private readonly AudioOutputManager _manager;
    private readonly MMDeviceEnumerator _enumerator;
    private DeviceNotificationClient? _notificationClient;

    public WindowsBluetoothOutputEnumerator(AudioOutputManager manager)
    {
        _manager = manager;
        _enumerator = new MMDeviceEnumerator();
    }

    /// <summary>
    /// Scan active render endpoints for BT devices and register them.
    /// </summary>
    public void ScanAndRegister()
    {
        var devices = _enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        foreach (var device in devices)
        {
            if (!IsBluetoothDevice(device)) continue;

            var providerId = $"bt-out:{device.ID}";
            if (_manager.Providers.Any(p => p.ProviderId == providerId))
                continue;

            var provider = new WindowsBluetoothAudioOutputProvider(device);
            _manager.RegisterProvider(provider);
        }
    }

    /// <summary>
    /// Start listening for audio device connect/disconnect events.
    /// </summary>
    public void StartListening()
    {
        _notificationClient = new DeviceNotificationClient(this);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    /// <summary>
    /// Stop listening for device events.
    /// </summary>
    public void StopListening()
    {
        if (_notificationClient is not null)
        {
            _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
            _notificationClient = null;
        }
    }

    private static bool IsBluetoothDevice(MMDevice device)
    {
        var id = device.ID ?? string.Empty;
        return id.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
            || id.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        StopListening();
        _enumerator.Dispose();
    }

    private sealed class DeviceNotificationClient : IMMNotificationClient
    {
        private readonly WindowsBluetoothOutputEnumerator _owner;

        public DeviceNotificationClient(WindowsBluetoothOutputEnumerator owner)
            => _owner = owner;

        public void OnDeviceAdded(string deviceId)
        {
            _owner.ScanAndRegister();
        }

        public void OnDeviceRemoved(string deviceId)
        {
            var providerId = $"bt-out:{deviceId}";
            _ = _owner._manager.UnregisterProviderAsync(providerId);
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (newState == DeviceState.Active)
                _owner.ScanAndRegister();
            else
                _ = _owner._manager.UnregisterProviderAsync($"bt-out:{deviceId}");
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
        }

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
        }
    }
}
