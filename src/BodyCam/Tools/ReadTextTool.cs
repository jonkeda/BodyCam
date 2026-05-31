using System.ComponentModel;
using BodyCam.Services.Camera.Commands;

namespace BodyCam.Tools;

public class ReadTextArgs
{
    [Description("Optional detail level: Summary, Overview, or Full")]
    public ReadDetailLevel? DetailLevel { get; set; }

    [Description("Optional focus area: sign, label, document, screen, etc.")]
    public string? Focus { get; set; }
}

public class ReadTextTool : ToolBase<ReadTextArgs>
{
    private readonly ICameraCommandService _commands;

    public override string Name => "read_text";
    public override string Description =>
        "Read and extract text visible in the camera view. " +
        "Use when the user asks to read a sign, label, document, menu, or any text.";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "wakewords/bodycam-read_en_windows.ppn",
        Mode = WakeWordMode.QuickAction,
        InitialPrompt = "Read any text you can see."
    };

    public ReadTextTool(ICameraCommandService commands)
    {
        _commands = commands;
    }

    protected override async Task<ToolResult> ExecuteAsync(
        ReadTextArgs args, ToolContext context, CancellationToken ct)
    {
        var options = new ReadCommandOptions(args.DetailLevel, args.Focus);
        var request = new CameraCommandRequest(
            "read",
            Mode: null,
            Origin: context.CommandOrigin ?? CommandTriggerOrigin.LlmToolCall,
            Options: options,
            Query: args.Focus);

        var result = await _commands.ExecuteAsync(request, ct);
        return result.Success
            ? ToolResult.Success(result.Data ?? new { text = result.TranscriptText })
            : ToolResult.Fail(result.Error ?? result.TranscriptText);
    }
}
