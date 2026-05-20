namespace BodyCam.Services.Input;

/// <summary>
/// A source of physical button input (keyboard, BLE remote, glasses, etc.).
/// Multiple providers can be active simultaneously.
/// </summary>
public interface IButtonInputProvider : IDisposable
{
    string DisplayName { get; }
    string ProviderId { get; }
    bool IsAvailable { get; }
    bool IsActive { get; }

    /// <summary>
    /// The buttons this device has, and which gestures each supports.
    /// Used by the settings UI to dynamically render mapping rows.
    /// </summary>
    IReadOnlyList<ButtonDescriptor> Buttons => [];

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();

    event EventHandler<RawButtonEvent>? RawButtonEvent;

    /// <summary>
    /// For providers with firmware-level gesture recognition (e.g. BTHome remotes).
    /// ButtonInputManager routes these directly to ActionMap, bypassing GestureRecognizer.
    /// </summary>
    event EventHandler<ButtonGestureEvent>? PreRecognizedGesture;

    event EventHandler? Disconnected;
}
