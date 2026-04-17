using System.ComponentModel;
using BodyCam.Agents;

namespace BodyCam.Tools;

public class DescribeSceneArgs
{
    [Description("Optional specific question about the scene")]
    public string? Query { get; set; }
}

public class DescribeSceneTool : ToolBase<DescribeSceneArgs>
{
    private readonly VisionAgent _vision;

    public override string Name => "describe_scene";
    public override string Description =>
        "Capture and describe what the camera currently sees. " +
        "Use when the user asks what's in front of them, asks you to look at something, " +
        "or when you need visual context to answer a question.";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "wakewords/bodycam-look_en_windows.ppn",
        Mode = WakeWordMode.QuickAction,
        InitialPrompt = "Describe what you see in detail."
    };

    public DescribeSceneTool(VisionAgent vision)
    {
        _vision = vision;
    }

    protected override async Task<ToolResult> ExecuteAsync(
        DescribeSceneArgs args, ToolContext context, CancellationToken ct)
    {
        // Rate-limit: return cached description if within cooldown
        if (_vision.LastDescription is not null
            && DateTimeOffset.UtcNow - _vision.LastCaptureTime < TimeSpan.FromSeconds(5))
        {
            return ToolResult.Success(new { description = _vision.LastDescription });
        }

        var frame = await context.CaptureFrame(ct);

        if (frame is null)
        {
            var stale = _vision.LastDescription ?? "Camera not available or no frame captured.";
            return ToolResult.Success(new { description = stale });
        }

        var description = await _vision.DescribeFrameAsync(frame, args.Query);
        context.Session.LastVisionDescription = description;

        return ToolResult.Success(new { description });
    }
}
