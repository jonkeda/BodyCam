using System.Text.Json;
using BodyCam.Services;

namespace BodyCam.Tools;

public class ToolDispatcher
{
    private readonly Dictionary<string, ITool> _tools;

    public ToolDispatcher(IEnumerable<ITool> tools)
    {
        _tools = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools)
        {
            _tools[tool.Name] = tool;
        }
    }

    public IReadOnlyList<ToolDefinitionDto> GetToolDefinitions()
    {
        return _tools.Values
            .Where(t => t.IsEnabled)
            .Select(t => new ToolDefinitionDto
            {
                Type = "function",
                Name = t.Name,
                Description = t.Description,
                ParametersJson = t.ParameterSchema
            })
            .ToList();
    }

    public async Task<string> ExecuteAsync(
        string toolName, string? argumentsJson, ToolContext context, CancellationToken ct)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
        {
            return SerializeError($"Unknown function: {toolName}");
        }

        if (!tool.IsEnabled)
        {
            return SerializeError($"Tool '{toolName}' is currently disabled.");
        }

        // Parse raw JSON string at the boundary
        JsonElement? arguments = null;
        if (!string.IsNullOrWhiteSpace(argumentsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                arguments = doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                return SerializeError($"Invalid arguments for '{toolName}': {ex.Message}");
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            var result = await tool.ExecuteAsync(arguments, context, timeoutCts.Token);
            return result.Json;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return SerializeError($"Tool '{toolName}' timed out after 15 seconds.");
        }
    }

    private static string SerializeError(string message) =>
        JsonSerializer.Serialize(new ToolErrorResult { Error = message },
            ToolJsonContext.Default.ToolErrorResult);

    public IReadOnlyList<WakeWordEntry> BuildWakeWordEntries()
    {
        var entries = new List<WakeWordEntry>();

        // System wake words
        entries.Add(new WakeWordEntry
        {
            KeywordPath = "wakewords/hey-bodycam_en_windows.ppn",
            Label = "Hey BodyCam",
            Sensitivity = 0.5f,
            Action = WakeWordAction.StartSession
        });
        entries.Add(new WakeWordEntry
        {
            KeywordPath = "wakewords/go-to-sleep_en_windows.ppn",
            Label = "Go to sleep",
            Sensitivity = 0.5f,
            Action = WakeWordAction.GoToSleep
        });

        // Tool wake words
        foreach (var tool in _tools.Values)
        {
            if (tool.WakeWord is not null)
            {
                entries.Add(new WakeWordEntry
                {
                    KeywordPath = tool.WakeWord.KeywordPath,
                    Label = tool.Name,
                    Sensitivity = tool.WakeWord.Sensitivity,
                    Action = WakeWordAction.InvokeTool,
                    ToolName = tool.Name
                });
            }
        }

        return entries;
    }

    public IEnumerable<string> ToolNames => _tools.Keys;

    public ITool? GetTool(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;
}

public class ToolDefinitionDto
{
    public string Type { get; set; } = "function";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ParametersJson { get; set; } = "{}";
}

public class ToolErrorResult
{
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public string Error { get; set; } = "";
}

[System.Text.Json.Serialization.JsonSerializable(typeof(ToolErrorResult))]
internal partial class ToolJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
