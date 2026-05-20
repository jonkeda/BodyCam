namespace BodyCam.Models;

/// <summary>
/// Unified model for a single device card in the Connected Devices panel.
/// Built from glasses state, BT provider state, etc.
/// </summary>
public sealed class ConnectedDeviceInfo
{
    /// <summary>Stable device identifier (MAC address, device path, etc.).</summary>
    public required string DeviceId { get; init; }

    /// <summary>User-visible device name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Device type for icon/grouping ("heycyan-glasses", "bluetooth-headset", etc.).</summary>
    public required string DeviceType { get; init; }

    /// <summary>Battery percentage (0–100), or null if unavailable.</summary>
    public int? BatteryPct { get; init; }

    /// <summary>Whether the device is charging.</summary>
    public bool IsCharging { get; init; }

    /// <summary>Device-specific status line (firmware, MAC, etc.).</summary>
    public string? StatusLine { get; init; }

    /// <summary>Whether disconnecting this device is supported.</summary>
    public bool CanDisconnect { get; init; }
}
