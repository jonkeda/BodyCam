namespace BodyCam.Services.Audio;

/// <summary>
/// Bluetooth-specific extension of IAudioInputProvider with MAC-aware selection.
/// Implemented by AndroidBluetoothAudioProvider (and potentially WindowsBluetoothAudioProvider).
/// </summary>
public interface IBluetoothAudioInputProvider : IAudioInputProvider
{
    /// <summary>
    /// Returns true if a connected BT capture endpoint with the specified MAC address exists.
    /// MAC comparison is case-insensitive and tolerates colon vs. dash separators.
    /// (AA:BB:CC:DD:EE:FF ≡ aa-bb-cc-dd-ee-ff).
    /// </summary>
    bool HasEndpointWithMac(string? mac);

    /// <summary>
    /// Locks subsequent StartAsync calls to the endpoint with the specified MAC address.
    /// Must be called before StartAsync.
    /// </summary>
    Task SelectEndpointByMacAsync(string mac, CancellationToken ct);
}
