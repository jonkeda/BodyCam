using System.ComponentModel;
using System.Text.Json;
using BodyCam.Agents;
using BodyCam.Models;

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
        "Capture and produce a comprehensive structured analysis of the current scene. " +
        "Returns a description, any visible text, and locations of QR codes or barcodes. " +
        "Use when the user asks to describe or analyze the overall scene.";

    public DescribeSceneTool(VisionAgent vision)
    {
        _vision = vision;
    }

    private static readonly string StructuredPrompt = """
        Analyze this image and respond in JSON (no markdown fences):
        {
          "description": "A concise scene description in 1-3 sentences.",
          "text": "Any readable text in the image, or null if none.",
          "codes": [{"format": "QR_CODE", "location": "bottom-left"}]
        }
        Set "codes" to null if no QR codes or barcodes are visible.
        Set "text" to null if no readable text is visible.
        """;

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

        var prompt = args.Query is not null
            ? $"{StructuredPrompt}\n\nAdditional focus: {args.Query}"
            : StructuredPrompt;

        var raw = await _vision.DescribeFrameAsync(frame, prompt, ct);
        context.Session.LastVisionDescription = raw;

        // Try to parse structured response; fall back to plain description
        var analysis = TryParseAnalysis(raw);
        return ToolResult.Success(analysis);
    }

    private static SceneAnalysis TryParseAnalysis(string raw)
    {
        try
        {
            // Strip markdown code fences if present
            var json = raw.Trim();
            if (json.StartsWith("```"))
            {
                var firstNewline = json.IndexOf('\n');
                var lastFence = json.LastIndexOf("```");
                if (firstNewline > 0 && lastFence > firstNewline)
                    json = json[(firstNewline + 1)..lastFence].Trim();
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var description = root.TryGetProperty("description", out var descProp)
                ? descProp.GetString() ?? raw
                : raw;

            var text = root.TryGetProperty("text", out var textProp) && textProp.ValueKind != JsonValueKind.Null
                ? textProp.GetString()
                : null;

            List<DetectedCode>? codes = null;
            if (root.TryGetProperty("codes", out var codesProp) && codesProp.ValueKind == JsonValueKind.Array)
            {
                codes = [];
                foreach (var item in codesProp.EnumerateArray())
                {
                    var format = item.TryGetProperty("format", out var fmtProp)
                        ? fmtProp.GetString() ?? "Unknown"
                        : "Unknown";
                    var location = item.TryGetProperty("location", out var locProp)
                        ? locProp.GetString()
                        : null;
                    codes.Add(new DetectedCode(format, location));
                }
                if (codes.Count == 0) codes = null;
            }

            return new SceneAnalysis
            {
                Description = description,
                ExtractedText = text,
                DetectedCodes = codes,
            };
        }
        catch
        {
            return new SceneAnalysis { Description = raw };
        }
    }
}
