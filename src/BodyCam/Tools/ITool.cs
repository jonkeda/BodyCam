using System.Text.Json;

namespace BodyCam.Tools;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    string ParameterSchema { get; }
    bool IsEnabled { get; }
    WakeWordBinding? WakeWord => null;
    Task<ToolResult> ExecuteAsync(JsonElement? arguments, ToolContext context, CancellationToken ct);
}
