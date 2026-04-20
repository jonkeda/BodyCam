using BodyCam.Agents;

namespace BodyCam.Services.Vision;

/// <summary>
/// Pipeline stage that produces a full scene description using the vision LLM.
/// This is the most expensive stage and always returns a result (never null).
/// </summary>
public class SceneDescriptionStage : IVisionPipelineStage
{
    private readonly VisionAgent _vision;

    public string Name => "Scene Description";
    public int Cost => 100;

    public SceneDescriptionStage(VisionAgent vision)
    {
        _vision = vision;
    }

    public async Task<VisionPipelineResult?> ProcessAsync(
        byte[] jpegFrame, string? query, CancellationToken ct)
    {
        var description = await _vision.DescribeFrameAsync(jpegFrame, query, ct);

        return new VisionPipelineResult("Scene Description", description, new()
        {
            ["found_type"] = "scene",
            ["description"] = description,
        });
    }
}
