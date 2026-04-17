using System.ComponentModel;
using BodyCam.Agents;

namespace BodyCam.Tools;

public class StartSceneWatchArgs
{
    [Description("Condition to watch for (e.g., 'when the light turns green', 'when someone arrives')")]
    public string Condition { get; set; } = "";

    [Description("How often to check, in seconds (default: 10)")]
    public int? IntervalSeconds { get; set; }
}

public class StartSceneWatchTool : ToolBase<StartSceneWatchArgs>
{
    private readonly VisionAgent _vision;

    public override string Name => "start_scene_watch";
    public override string Description =>
        "Start watching the camera for a specific condition. " +
        "Checks periodically until the condition is met. " +
        "Use when the user asks to watch for or alert when something happens.";

    public StartSceneWatchTool(VisionAgent vision)
    {
        _vision = vision;
    }

    protected override async Task<ToolResult> ExecuteAsync(
        StartSceneWatchArgs args, ToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Condition))
            return ToolResult.Fail("Condition is required.");

        var interval = args.IntervalSeconds ?? 10;

        // Start background monitoring — fire and forget with the cancellation token
        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(interval), ct);

                    var frame = await context.CaptureFrame(ct);
                    if (frame is null) continue;

                    var prompt = $"Check if this condition is met: '{args.Condition}'. " +
                                 "If the condition IS met, respond starting with CONDITION_MET. " +
                                 "If NOT met, respond starting with NOT_MET.";
                    var result = await _vision.DescribeFrameAsync(frame, prompt);

                    if (result.StartsWith("CONDITION_MET", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Log($"Scene watch triggered: {args.Condition}");
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation
            }
        }, ct);

        context.Log($"Scene watch started: {args.Condition} (every {interval}s)");

        return ToolResult.Success(new
        {
            condition = args.Condition,
            intervalSeconds = interval,
            status = "Watching"
        });
    }
}
