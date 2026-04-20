using System.ComponentModel;
using BodyCam.Services.Vision;

namespace BodyCam.Tools;

public class LookArgs
{
    [Description("Optional specific question about what you see")]
    public string? Query { get; set; }
}

public class LookTool : ToolBase<LookArgs>
{
    private readonly VisionPipeline _pipeline;

    public override string Name => "look";
    public override string Description =>
        "Look at what the camera sees. Automatically scans for QR codes, " +
        "reads text, and describes the scene — returning the first useful result.";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "wakewords/bodycam-look_en_windows.ppn",
        Mode = WakeWordMode.QuickAction,
        InitialPrompt = "Look at what's in front of me."
    };

    public LookTool(VisionPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    protected override async Task<ToolResult> ExecuteAsync(
        LookArgs args, ToolContext context, CancellationToken ct)
    {
        var frame = await context.CaptureFrame(ct);
        if (frame is null)
            return ToolResult.Fail("Camera not available.");

        var result = await _pipeline.ExecuteAsync(frame, args.Query, ct);
        return ToolResult.Success(result);
    }
}
