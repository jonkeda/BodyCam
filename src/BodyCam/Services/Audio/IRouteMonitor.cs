namespace BodyCam.Services.Audio;

/// <summary>
/// Monitors audio routing changes (headphones, Bluetooth, speaker) to inform AEC bypass decisions.
/// </summary>
public interface IRouteMonitor : IAsyncDisposable
{
    /// <summary>True if wired or wireless headphones are connected.</summary>
    bool IsHeadphonesConnected { get; }

    /// <summary>True if Bluetooth audio is currently active.</summary>
    bool IsBluetoothAudioConnected { get; }

    /// <summary>Fired when the active audio route changes.</summary>
    event EventHandler? RouteChanged;
}
