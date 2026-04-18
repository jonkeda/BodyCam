using BodyCam.Services.Audio;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace BodyCam.Platforms.Windows.Audio;

/// <summary>
/// Discovers Bluetooth audio capture endpoints via the MMDevice API and registers
/// them as <see cref="IAudioInputProvider"/> instances with <see cref="AudioInputManager"/>.
/// </summary>
public sealed class WindowsBluetoothEnumerator : IDisposable
{
    private readonly AudioInputManager _manager;
    private readonly AppSettings _settings;
    private readonly MMDeviceEnumerator _enumerator;
    private DeviceNotificationClient? _notificationClient;

    public WindowsBluetoothEnumerator(AudioInputManager manager, AppSettings settings)
    {
        _manager = manager;
        _settings = settings;
        _enumerator = new MMDeviceEnumerator();
    }

    /// <summary>
    /// Scan active capture endpoints for BT devices and register them.
    /// </summary>
    public void ScanAndRegister()
    {
        var devices = _enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

        foreach (var device in devices)
        {
            if (!IsBluetoothDevice(device)) continue;

            var providerId = $"bt:{device.ID}";
            if (_manager.Providers.Any(p => p.ProviderId == providerId))
                continue;

            var provider = new WindowsBluetoothAudioProvider(device, _settings);
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
        private readonly WindowsBluetoothEnumerator _owner;

        public DeviceNotificationClient(WindowsBluetoothEnumerator owner)
            => _owner = owner;

        public void OnDeviceAdded(string deviceId)
        {
            // Rescan to pick up new BT devices
            _owner.ScanAndRegister();
        }

        public void OnDeviceRemoved(string deviceId)
        {
            // Check if this was a registered BT provider
            var providerId = $"bt:{deviceId}";
            _ = _owner._manager.UnregisterProviderAsync(providerId);
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (newState == DeviceState.Active)
                _owner.ScanAndRegister();
            else
                _ = _owner._manager.UnregisterProviderAsync($"bt:{deviceId}");
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // Not relevant for BT enumeration
        }

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
            // Not relevant
        }
    }
}
