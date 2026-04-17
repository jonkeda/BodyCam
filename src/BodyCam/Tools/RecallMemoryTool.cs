using System.ComponentModel;
using BodyCam.Services;

namespace BodyCam.Tools;

public class RecallMemoryArgs
{
    [Description("Search query to find relevant memories")]
    public string Query { get; set; } = "";
}

public class RecallMemoryTool : ToolBase<RecallMemoryArgs>
{
    private readonly MemoryStore _store;

    public override string Name => "recall_memory";
    public override string Description =>
        "Search and recall previously saved memories. " +
        "Use when the user asks 'what did I save', 'do you remember', or references something saved earlier.";

    public RecallMemoryTool(MemoryStore store)
    {
        _store = store;
    }

    protected override async Task<ToolResult> ExecuteAsync(
        RecallMemoryArgs args, ToolContext context, CancellationToken ct)
    {
        var results = await _store.SearchAsync(args.Query);

        if (results.Count == 0)
            return ToolResult.Success(new { found = false, message = "No matching memories found." });

        var memories = results.Select(r => new
        {
            content = r.Content,
            category = r.Category,
            timestamp = r.Timestamp.ToString("g")
        }).ToArray();

        return ToolResult.Success(new { found = true, count = results.Count, memories });
    }
}
