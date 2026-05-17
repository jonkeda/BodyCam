using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using BodyCam.Services.Audio;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Windows.Devices.Bluetooth;

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

    // Maps MMDevice.ID → ProviderId so disconnect handlers can find the right provider
    private readonly ConcurrentDictionary<string, string> _deviceIdToProviderId = new();

    // Cache of paired BT device names → formatted MACs. Used as fallback when the audio
    // endpoint is routed through Intel SST (INTELAUDIO) instead of BTHENUM.
    private static readonly ConcurrentDictionary<string, string> s_pairedBtNameToMac = new();

    public WindowsBluetoothEnumerator(AudioInputManager manager, AppSettings settings)
    {
        _manager = manager;
        _settings = settings;
        _enumerator = new MMDeviceEnumerator();
    }

    /// <summary>
    /// Raised when a new BT audio endpoint is registered, passing the MAC address.
    /// </summary>
    public event Action<string>? EndpointRegistered;

    /// <summary>
    /// Returns true if there is a registered BT capture endpoint matching the given MAC.
    /// </summary>
    public bool HasEndpointWithMac(string? mac)
    {
        if (mac is null) return false;
        var providerId = $"bt:{mac}";
        return _deviceIdToProviderId.Values.Contains(providerId)
            || _manager.Providers.Any(p => p.ProviderId == providerId);
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
            string? mac;
            if (IsBluetoothDevice(device))
            {
                mac = ExtractMacFromDevice(device);
            }
            else
            {
                // Fallback: Intel SST routes BT audio through INTELAUDIO, not BTHENUM.
                // Cross-reference FriendlyName against paired Bluetooth devices.
                mac = TryGetMacFromPairedDeviceCache(device.FriendlyName);
            }

            if (mac is null) continue;

            var providerId = $"bt:{mac}";
            if (_manager.Providers.Any(p => p.ProviderId == providerId))
                continue;

            _deviceIdToProviderId[device.ID] = providerId;
            var provider = new WindowsBluetoothAudioProvider(device, _settings, mac);
            _manager.RegisterProvider(provider);
            EndpointRegistered?.Invoke(mac);
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
            // DEVPKEY_Device_EnumeratorName — "BTHENUM" for Bluetooth devices.
            // MMDevice.ID is a GUID on modern Windows and does NOT contain "BTHENUM".
            var key = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 24);
            var value = device.Properties[key].Value?.ToString();
            return string.Equals(value, "BTHENUM", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts the Bluetooth MAC address from the MMDevice's BTHENUM instance path.
    /// The instance path (property {b3f8fa53-...}#2) embeds the MAC as 12 hex digits,
    /// e.g. <c>...&amp;D879B87FE6C9_C00000000</c>.
    /// </summary>
    /// <summary>
    /// Refresh the cache of paired Bluetooth device names and their MAC addresses.
    /// Called at startup and before device scans. The cache is used as a fallback when
    /// BTHENUM detection fails (e.g. Intel SST audio pipeline).
    /// </summary>
    internal static async Task RefreshPairedDeviceCacheAsync()
    {
        try
        {
            var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await global::Windows.Devices.Enumeration.DeviceInformation
                .FindAllAsync(selector);
            foreach (var di in devices)
            {
                try
                {
                    var btDev = await BluetoothDevice.FromIdAsync(di.Id);
                    if (btDev is null) continue;
                    var mac = FormatBluetoothAddress(btDev.BluetoothAddress);
                    s_pairedBtNameToMac[di.Name] = mac;
                }
                catch { /* skip individual device failures */ }
            }
        }
        catch { /* non-critical — BTHENUM path still works */ }
    }

    /// <summary>
    /// Try to find a paired BT device MAC by matching the MMDevice's FriendlyName.
    /// FriendlyName format: "Headphones (DeviceName)" or "Headset (DeviceName)".
    /// </summary>
    internal static string? TryGetMacFromPairedDeviceCache(string friendlyName)
    {
        var openParen = friendlyName.LastIndexOf('(');
        var closeParen = friendlyName.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen) return null;

        var deviceName = friendlyName.Substring(openParen + 1, closeParen - openParen - 1);
        return s_pairedBtNameToMac.TryGetValue(deviceName, out var mac) ? mac : null;
    }

    private static string FormatBluetoothAddress(ulong address)
    {
        var bytes = BitConverter.GetBytes(address);
        return $"{bytes[5]:X2}:{bytes[4]:X2}:{bytes[3]:X2}:{bytes[2]:X2}:{bytes[1]:X2}:{bytes[0]:X2}";
    }

    internal static string? ExtractMacFromDevice(MMDevice device)
    {
        try
        {
            var key = new PropertyKey(new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 2);
            var instancePath = device.Properties[key].Value?.ToString();
            if (instancePath is null) return null;

            var match = Regex.Match(instancePath, @"&([0-9A-Fa-f]{12})_C");
            if (!match.Success) return null;

            var hex = match.Groups[1].Value.ToUpperInvariant();
            return $"{hex[..2]}:{hex[2..4]}:{hex[4..6]}:{hex[6..8]}:{hex[8..10]}:{hex[10..12]}";
        }
        catch
        {
            return null;
        }
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
            // Refresh paired BT cache (new device may have just paired) then rescan.
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
            await RefreshPairedDeviceCacheAsync().ConfigureAwait(false);
            _owner.ScanAndRegister();
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
