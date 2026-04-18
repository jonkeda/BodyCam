namespace BodyCam.Services.Audio;

/// <summary>
/// Lightweight metadata for a discovered Bluetooth audio device.
/// </summary>
public record BluetoothDeviceInfo
{
    public required string DeviceId { get; init; }
    public required string Name { get; init; }
    public required string ProviderId { get; init; }
    public bool IsConnected { get; init; }
}
