namespace BodyCam.Services.Input;

/// <summary>
/// Describes a single physical button on a device and which gestures it supports.
/// Used by the settings UI to dynamically render mapping rows.
/// </summary>
public sealed record ButtonDescriptor(
    string ButtonId,
    string DisplayName,
    IReadOnlyList<ButtonGesture> SupportedGestures);
