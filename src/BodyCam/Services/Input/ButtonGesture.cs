namespace BodyCam.Services.Input;

public enum ButtonGesture
{
    SingleTap,
    DoubleTap,
    LongPress,
    LongPressRelease,
}

public sealed class ButtonGestureEvent
{
    public required string ProviderId { get; init; }
    public required ButtonGesture Gesture { get; init; }
    public string ButtonId { get; init; } = "primary";
    public required long TimestampMs { get; init; }
}

public sealed class ButtonActionEvent
{
    public required ButtonAction Action { get; init; }
    public required string SourceProviderId { get; init; }
    public required ButtonGesture SourceGesture { get; init; }
    public required long TimestampMs { get; init; }
}
