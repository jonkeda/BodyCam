namespace BodyCam.Tools;

public record WakeWordBinding
{
    public required string KeywordPath { get; init; }
    public float Sensitivity { get; init; } = 0.5f;
    public WakeWordMode Mode { get; init; } = WakeWordMode.QuickAction;
    public string? InitialPrompt { get; init; }
}

public enum WakeWordMode
{
    QuickAction,
    FullSession
}
