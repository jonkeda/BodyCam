using System.ComponentModel;
using BodyCam.Agents;
using SkiaSharp;

namespace BodyCam.Tools;

public class FindObjectArgs
{
    [Description("The object to find (e.g., 'my keys', 'the red mug')")]
    public string Target { get; set; } = "";
}

public partial class FindObjectTool : ToolBase<FindObjectArgs>
{
    private readonly VisionAgent _vision;

    public override string Name => "find_object";
    public override string Description =>
        "Search for a specific object in the camera view. " +
        "Continuously scans until found or timeout. " +
        "Use when the user asks to find, locate, or look for something.";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "wakewords/bodycam-find_en_windows.ppn",
        Mode = WakeWordMode.FullSession,
        InitialPrompt = "What would you like me to find?"
    };

    public FindObjectTool(VisionAgent vision)
    {
        _vision = vision;
    }

    internal int TimeoutSeconds { get; set; } = 30;
    internal int ScanIntervalSeconds { get; set; } = 3;

    protected override async Task<ToolResult> ExecuteAsync(
        FindObjectArgs args, ToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Target))
            return ToolResult.Fail("Target object is required.");

        var deadline = DateTimeOffset.UtcNow.AddSeconds(TimeoutSeconds);

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var frame = await context.CaptureFrame(ct);
            if (frame is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(ScanIntervalSeconds), ct);
                continue;
            }

            var prompt = $"Is '{args.Target}' visible in this image? " +
                         "If yes, respond starting with FOUND and describe where it is. " +
                         "If no, respond starting with NOT_FOUND.";
            var result = await _vision.DescribeFrameAsync(frame, prompt);

            if (result.StartsWith("FOUND", StringComparison.OrdinalIgnoreCase))
            {
                context.Log($"Found: {args.Target}");
                return ToolResult.Success(new
                {
                    found = true,
                    description = result,
                    target = args.Target
                });
            }

            await Task.Delay(TimeSpan.FromSeconds(ScanIntervalSeconds), ct);
        }

        return ToolResult.Success(new
        {
            found = false,
            description = $"Could not find '{args.Target}' within {TimeoutSeconds}s.",
            target = args.Target
        });
    }

    public static byte[] AnnotateFrame(byte[] jpeg, string label)
    {
        using var bitmap = SKBitmap.Decode(jpeg);
        if (bitmap is null) return jpeg;

        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height));
        var canvas = surface.Canvas;
        canvas.DrawBitmap(bitmap, 0, 0);

        // Draw label at top
        using var font = new SKFont { Size = 48 };
        using var paint = new SKPaint
        {
            Color = SKColors.Red,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawText(label, 20, 60, SKTextAlign.Left, font, paint);

        // Draw border highlight
        using var borderPaint = new SKPaint
        {
            Color = SKColors.Red,
            StrokeWidth = 4,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawRect(10, 10, bitmap.Width - 20, bitmap.Height - 20, borderPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }
}
