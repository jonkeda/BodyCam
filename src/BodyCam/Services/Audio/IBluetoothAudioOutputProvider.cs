namespace BodyCam.Services.Audio;

/// <summary>
/// Bluetooth-specific extension of IAudioOutputProvider with MAC-aware selection.
/// Implemented by platform-specific Bluetooth audio output providers (Android, Windows).
/// </summary>
public interface IBluetoothAudioOutputProvider : IAudioOutputProvider
{
    /// <summary>
    /// Returns true if a connected BT render endpoint with the specified MAC address exists.
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
