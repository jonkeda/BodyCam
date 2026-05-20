namespace BodyCam.Services;

/// <summary>Reason for an automatic profile switch.</summary>
public enum ProfileSwitchReason
{
    /// <summary>User explicitly selected a profile.</summary>
    UserSelected,

    /// <summary>A higher-priority device connected, auto-upgrading.</summary>
    DeviceConnected,

    /// <summary>The active device disconnected, falling back.</summary>
    DeviceDisconnected,

    /// <summary>Saved profile unavailable at app startup.</summary>
    StartupFallback,
}

/// <summary>
/// Carries context about an automatic profile switch for UI notifications.
/// </summary>
public sealed class ProfileSwitchNotification
{
    public required string OldProfileName { get; init; }
    public required string NewProfileName { get; init; }
    public required ProfileSwitchReason Reason { get; init; }

    /// <summary>Human-readable message for toast display.</summary>
    public string Message => Reason switch
    {
        ProfileSwitchReason.DeviceConnected =>
            $"Switched to {NewProfileName} (device connected)",
        ProfileSwitchReason.DeviceDisconnected =>
            $"Switched to {NewProfileName} ({OldProfileName} disconnected)",
        ProfileSwitchReason.StartupFallback =>
            $"Using {NewProfileName} ({OldProfileName} unavailable)",
        _ => $"Switched to {NewProfileName}",
    };
}
