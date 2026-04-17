namespace BodyCam.Tools;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    string ParameterSchema { get; }
    bool IsEnabled { get; }
    WakeWordBinding? WakeWord => null;
    Task<ToolResult> ExecuteAsync(string? argumentsJson, ToolContext context, CancellationToken ct);
}
