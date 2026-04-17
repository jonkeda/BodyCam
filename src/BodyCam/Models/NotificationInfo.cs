namespace BodyCam.Models;

public record NotificationInfo
{
    public string App { get; init; } = "";
    public string? Title { get; init; }
    public string? Text { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
