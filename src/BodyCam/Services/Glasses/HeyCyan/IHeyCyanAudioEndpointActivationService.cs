namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Guides platform-specific activation of HeyCyan audio endpoints around the
/// glasses BLE session.
/// </summary>
public interface IHeyCyanAudioEndpointActivationService
{
    bool IsSupported { get; }

    /// <summary>
    /// True when the platform should activate Classic Bluetooth audio before
    /// the app opens and holds the BLE/GATT connection.
    /// </summary>
    bool RequiresActivationBeforeBleConnect { get; }

    HeyCyanAudioEndpointSnapshot? Current { get; }

    event EventHandler<HeyCyanAudioEndpointSnapshot>? Updated;

    Task<HeyCyanAudioEndpointSnapshot> RefreshAsync(CancellationToken ct);

    Task<HeyCyanAudioEndpointSnapshot> BeginActivationAsync(
        HeyCyanDeviceInfo? selectedDevice,
        CancellationToken ct);

    Task OpenBluetoothSettingsAsync(CancellationToken ct);
}

public sealed record HeyCyanAudioEndpointSnapshot(
    string? MacAddress,
    string Summary,
    HeyCyanEndpointStatus CaptureStatus,
    HeyCyanEndpointStatus RenderStatus,
    IReadOnlyList<HeyCyanWindowsEndpointInfo> CaptureEndpoints,
    IReadOnlyList<HeyCyanWindowsEndpointInfo> RenderEndpoints,
    IReadOnlyList<HeyCyanWindowsProfileInfo> ProfileNodes,
    bool RequiresUserAction)
{
    public bool IsReady =>
        CaptureStatus == HeyCyanEndpointStatus.Active
        && RenderStatus == HeyCyanEndpointStatus.Active;

    public bool HasAnyActiveEndpoint =>
        CaptureStatus == HeyCyanEndpointStatus.Active
        || RenderStatus == HeyCyanEndpointStatus.Active;
}

public enum HeyCyanEndpointStatus
{
    Unknown,
    Missing,
    NotPresent,
    Unplugged,
    Disabled,
    Active
}

public sealed record HeyCyanWindowsEndpointInfo(
    string FriendlyName,
    string DeviceId,
    string State,
    string? ProviderId,
    string? MatchedMac);

public sealed record HeyCyanWindowsProfileInfo(
    string Name,
    string Status,
    string PnpClass,
    string DeviceId);
