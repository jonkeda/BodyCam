using System.ComponentModel;
using BodyCam.Services;

namespace BodyCam.Tools;

public class SaveMemoryArgs
{
    [Description("The information to remember")]
    public string Content { get; set; } = "";

    [Description("Category: general, location, person, item, instruction")]
    public string? Category { get; set; }
}

public class SaveMemoryTool : ToolBase<SaveMemoryArgs>
{
    private readonly MemoryStore _store;

    public override string Name => "save_memory";
    public override string Description =>
        "Save information to persistent memory for later recall. " +
        "Use when the user says 'remember this', wants to save a note, " +
        "or asks you to remember something for later.";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "wakewords/bodycam-remember_en_windows.ppn",
        Mode = WakeWordMode.FullSession,
        InitialPrompt = "What would you like me to remember?"
    };

    public SaveMemoryTool(MemoryStore store)
    {
        _store = store;
    }

    protected override async Task<ToolResult> ExecuteAsync(
        SaveMemoryArgs args, ToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Content))
            return ToolResult.Fail("Content is required to save a memory.");

        var entry = new MemoryEntry
        {
            Content = args.Content,
            Category = args.Category
        };

        await _store.SaveAsync(entry);
        context.Log($"Memory saved: {args.Content}");

        return ToolResult.Success(new
        {
            saved = true,
            id = entry.Id,
            content = args.Content,
            category = args.Category ?? "general"
        });
    }
}
