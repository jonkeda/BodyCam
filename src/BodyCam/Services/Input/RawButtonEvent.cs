namespace BodyCam.Services.Input;

public enum RawButtonEventType
{
    ButtonDown,
    ButtonUp,
    Click,
}

public sealed class RawButtonEvent
{
    public required string ProviderId { get; init; }
    public required RawButtonEventType EventType { get; init; }
    public required long TimestampMs { get; init; }
    public string ButtonId { get; init; } = "primary";
}
