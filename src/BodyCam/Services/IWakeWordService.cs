namespace BodyCam.Services;

public interface IWakeWordService
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    bool IsListening { get; }
    event EventHandler<WakeWordDetectedEventArgs>? WakeWordDetected;
    void RegisterKeywords(IEnumerable<WakeWordEntry> entries);
}

public enum WakeWordAction
{
    StartSession,
    GoToSleep,
    InvokeTool
}

public class WakeWordDetectedEventArgs : EventArgs
{
    public required WakeWordAction Action { get; init; }
    public required string Keyword { get; init; }
    public string? ToolName { get; init; }
}

public record WakeWordEntry
{
    public required string KeywordPath { get; init; }
    public required string Label { get; init; }
    public required float Sensitivity { get; init; }
    public required WakeWordAction Action { get; init; }
    public string? ToolName { get; init; }
}
