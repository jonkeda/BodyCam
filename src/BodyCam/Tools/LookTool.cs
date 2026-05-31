using System.ComponentModel;
using BodyCam.Services.Camera.Commands;

namespace BodyCam.Tools;

public class LookArgs
{
    [Description("Optional detail level: Summary, Overview, Detailed, or Full")]
    public LookDetailLevel? DetailLevel { get; set; }

    [Description("Optional thing to pay particular attention to")]
    public string? Focus { get; set; }

    [Description("Optional specific question about what you see")]
    public string? Query { get; set; }
}

public class LookTool : ToolBase<LookArgs>
{
    private readonly ICameraCommandService _commands;

    public override string Name => "look";
    public override string Description =>
        "Immediately capture one camera frame and describe what is visible. " +
        "Use for scene, object, hazard, or orientation questions.";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "wakewords/bodycam-look_en_windows.ppn",
        Mode = WakeWordMode.QuickAction,
        InitialPrompt = "Look at what's in front of me."
    };

    public LookTool(ICameraCommandService commands)
    {
        _commands = commands;
    }

    protected override async Task<ToolResult> ExecuteAsync(
        LookArgs args, ToolContext context, CancellationToken ct)
    {
        var options = new LookCommandOptions(args.DetailLevel, args.Focus, args.Query);
        var request = new CameraCommandRequest(
            "look",
            Mode: null,
            Origin: context.CommandOrigin ?? CommandTriggerOrigin.LlmToolCall,
            Options: options,
            Query: args.Query);

        var result = await _commands.ExecuteAsync(request, ct);
        return result.Success
            ? ToolResult.Success(result.Data ?? new { description = result.TranscriptText })
            : ToolResult.Fail(result.Error ?? result.TranscriptText);
    }
}
