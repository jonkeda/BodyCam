using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, string> _deviceIdToProviderId = new();

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
            string? mac;
            if (IsBluetoothDevice(device))
            {
                mac = WindowsBluetoothEnumerator.ExtractMacFromDevice(device);
            }
            else
            {
                // Fallback: Intel SST routes BT audio through INTELAUDIO, not BTHENUM.
                mac = WindowsBluetoothEnumerator.TryGetMacFromPairedDeviceCache(device.FriendlyName);
            }

            if (mac is null) continue;

            var providerId = $"bt:{mac}";
            if (_manager.Providers.Any(p => p.ProviderId == providerId))
                continue;

            _deviceIdToProviderId[device.ID] = providerId;
            var provider = new WindowsBluetoothAudioOutputProvider(device, mac);
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
        try
        {
            var key = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 24);
            var value = device.Properties[key].Value?.ToString();
            return string.Equals(value, "BTHENUM", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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
            _ = RefreshAndScanAsync();
        }

        public void OnDeviceRemoved(string deviceId)
        {
            if (_owner._deviceIdToProviderId.TryRemove(deviceId, out var providerId))
                _ = _owner._manager.UnregisterProviderAsync(providerId);
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (newState == DeviceState.Active)
                _ = RefreshAndScanAsync();
            else if (_owner._deviceIdToProviderId.TryRemove(deviceId, out var providerId))
                _ = _owner._manager.UnregisterProviderAsync(providerId);
        }

        private async Task RefreshAndScanAsync()
        {
            await WindowsBluetoothEnumerator.RefreshPairedDeviceCacheAsync().ConfigureAwait(false);
            _owner.ScanAndRegister();
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
        }

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
        }
    }
}
