using System.Text.Json.Serialization;

namespace BodyCam.Models;

/// <summary>
/// Persisted as JSON in a single Preferences key. Replaces all flat device keys.
/// </summary>
public sealed class DeviceSettings
{
    /// <summary>Active source profile ID ("phone", "heycyan-glasses", "custom", etc.).</summary>
    [JsonPropertyName("activeProfileId")]
    public string ActiveProfileId { get; set; } = "phone";

    /// <summary>Per-slot provider IDs when profile = "custom".</summary>
    [JsonPropertyName("custom")]
    public CustomSelection Custom { get; set; } = new();

    /// <summary>Currently active provider IDs (runtime state, set by profile or custom).</summary>
    [JsonPropertyName("active")]
    public ActiveProviders Active { get; set; } = new();

    /// <summary>Known devices that should auto-reconnect.</summary>
    [JsonPropertyName("knownDevices")]
    public List<KnownDevice> KnownDevices { get; set; } = [];

    /// <summary>Per-profile overrides (e.g. which specific BT device for "bluetooth" profile).</summary>
    [JsonPropertyName("profileSettings")]
    public Dictionary<string, ProfileOverrides> ProfileSettings { get; set; } = new();
}

public sealed class CustomSelection
{
    [JsonPropertyName("cameraProviderId")]
    public string? CameraProviderId { get; set; }

    [JsonPropertyName("audioInputProviderId")]
    public string? AudioInputProviderId { get; set; }

    [JsonPropertyName("audioOutputProviderId")]
    public string? AudioOutputProviderId { get; set; }
}

public sealed class ActiveProviders
{
    [JsonPropertyName("cameraProviderId")]
    public string? CameraProviderId { get; set; }

    [JsonPropertyName("audioInputProviderId")]
    public string? AudioInputProviderId { get; set; }

    [JsonPropertyName("audioOutputProviderId")]
    public string? AudioOutputProviderId { get; set; }
}

/// <summary>
/// A device the user has connected before. Supports multiple glasses, headsets, etc.
/// </summary>
public sealed class KnownDevice
{
    /// <summary>Stable identifier (BLE MAC, BT address, USB VID:PID, etc.).</summary>
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = "";

    /// <summary>User-visible name ("HeyCyan Glasses", "AirPods Pro", etc.).</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    /// <summary>Device type for icon/grouping.</summary>
    [JsonPropertyName("deviceType")]
    public string DeviceType { get; set; } = "";

    /// <summary>Auto-reconnect on app start.</summary>
    [JsonPropertyName("autoReconnect")]
    public bool AutoReconnect { get; set; } = true;

    /// <summary>Last successful connection timestamp (for sort order / LRU).</summary>
    [JsonPropertyName("lastConnected")]
    public DateTime? LastConnected { get; set; }

    /// <summary>Device-specific settings (e.g. button mappings for glasses).</summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, string> Properties { get; set; } = new();
}

public sealed class ProfileOverrides
{
    /// <summary>Preferred device ID within this profile (e.g. which BT device for "bluetooth" profile).</summary>
    [JsonPropertyName("preferredDeviceId")]
    public string? PreferredDeviceId { get; set; }
}
