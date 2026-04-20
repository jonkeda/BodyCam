using BodyCam.Agents;

namespace BodyCam.Services.Vision;

/// <summary>
/// Pipeline stage that extracts visible text from a frame using a vision model.
/// Uses a text-only prompt to keep cost lower than full scene description.
/// Returns null when no meaningful text is detected.
/// </summary>
public class TextDetectionStage : IVisionPipelineStage
{
    private readonly VisionAgent _vision;

    public string Name => "Text Detection";
    public int Cost => 10;

    public TextDetectionStage(VisionAgent vision)
    {
        _vision = vision;
    }

    public async Task<VisionPipelineResult?> ProcessAsync(
        byte[] jpegFrame, string? query, CancellationToken ct)
    {
        var prompt = "Extract all visible text from this image. " +
                     "Return ONLY the text you can read, nothing else. " +
                     "If no text is visible, respond with exactly: NO_TEXT";

        var text = await _vision.DescribeFrameAsync(jpegFrame, prompt, ct);

        if (string.IsNullOrWhiteSpace(text)
            || text.Contains("NO_TEXT", StringComparison.OrdinalIgnoreCase)
            || text.Contains("no text", StringComparison.OrdinalIgnoreCase))
            return null;

        return new VisionPipelineResult("Text Detection", text, new()
        {
            ["found_type"] = "text",
            ["text"] = text,
        });
    }
}
