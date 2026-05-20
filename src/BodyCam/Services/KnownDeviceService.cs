namespace BodyCam.Services;

using BodyCam.Models;

/// <summary>
/// Manages the list of known devices (glasses, BT headsets, etc.) for persistence and auto-reconnect.
/// Wraps <see cref="DeviceSettings.KnownDevices"/> with add/remove/update operations.
/// </summary>
public sealed class KnownDeviceService
{
    private readonly ISettingsService _settings;

    public KnownDeviceService(ISettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>All known devices, most recently connected first.</summary>
    public IReadOnlyList<KnownDevice> Devices =>
        _settings.DeviceSettings.KnownDevices
            .OrderByDescending(d => d.LastConnected)
            .ToList();

    /// <summary>
    /// Add or update a known device. If a device with the same ID exists, updates it;
    /// otherwise adds it. Persists immediately.
    /// </summary>
    public void AddOrUpdate(string deviceId, string displayName, string deviceType,
        Dictionary<string, string>? properties = null)
    {
        var ds = _settings.DeviceSettings;
        var existing = ds.KnownDevices.FirstOrDefault(d =>
            string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.DisplayName = displayName;
            existing.DeviceType = deviceType;
            existing.LastConnected = DateTime.UtcNow;
            if (properties is not null)
            {
                foreach (var kvp in properties)
                    existing.Properties[kvp.Key] = kvp.Value;
            }
        }
        else
        {
            ds.KnownDevices.Add(new KnownDevice
            {
                DeviceId = deviceId,
                DisplayName = displayName,
                DeviceType = deviceType,
                AutoReconnect = true,
                LastConnected = DateTime.UtcNow,
                Properties = properties ?? new(),
            });
        }

        _settings.DeviceSettings = ds;
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Remove a known device by ID. Persists immediately.</summary>
    public bool Remove(string deviceId)
    {
        var ds = _settings.DeviceSettings;
        var removed = ds.KnownDevices.RemoveAll(d =>
            string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
        {
            _settings.DeviceSettings = ds;
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed > 0;
    }

    /// <summary>Get a known device by ID, or null if not found.</summary>
    public KnownDevice? Get(string deviceId) =>
        _settings.DeviceSettings.KnownDevices.FirstOrDefault(d =>
            string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));

    /// <summary>All devices marked for auto-reconnect.</summary>
    public IReadOnlyList<KnownDevice> AutoReconnectDevices =>
        _settings.DeviceSettings.KnownDevices
            .Where(d => d.AutoReconnect)
            .OrderByDescending(d => d.LastConnected)
            .ToList();

    /// <summary>Toggle auto-reconnect for a device. Persists immediately.</summary>
    public void SetAutoReconnect(string deviceId, bool autoReconnect)
    {
        var ds = _settings.DeviceSettings;
        var device = ds.KnownDevices.FirstOrDefault(d =>
            string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));

        if (device is not null && device.AutoReconnect != autoReconnect)
        {
            device.AutoReconnect = autoReconnect;
            _settings.DeviceSettings = ds;
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Fires when the known devices list changes.</summary>
    public event EventHandler? DevicesChanged;
}
