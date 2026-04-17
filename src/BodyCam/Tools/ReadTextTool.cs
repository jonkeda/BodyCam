using System.ComponentModel;
using BodyCam.Agents;

namespace BodyCam.Tools;

public class ReadTextArgs
{
    [Description("Optional focus area: sign, label, document, screen, etc.")]
    public string? Focus { get; set; }
}

public class ReadTextTool : ToolBase<ReadTextArgs>
{
    private readonly VisionAgent _vision;

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

    public ReadTextTool(VisionAgent vision)
    {
        _vision = vision;
    }

    protected override async Task<ToolResult> ExecuteAsync(
        ReadTextArgs args, ToolContext context, CancellationToken ct)
    {
        var frame = await context.CaptureFrame(ct);
        if (frame is null)
            return ToolResult.Fail("Camera not available.");

        var prompt = args.Focus is not null
            ? $"Read and extract all text from the {args.Focus}. Return the exact text you can read."
            : "Read and extract all visible text. Return the exact text you can read.";

        var text = await _vision.DescribeFrameAsync(frame, prompt);
        return ToolResult.Success(new { text });
    }
}
