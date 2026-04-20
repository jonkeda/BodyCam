namespace BodyCam.Services.Vision;

/// <summary>
/// Executes vision pipeline stages in ascending cost order.
/// The first stage that returns a non-null result wins.
/// </summary>
public class VisionPipeline
{
    private readonly IReadOnlyList<IVisionPipelineStage> _stages;

    public VisionPipeline(IEnumerable<IVisionPipelineStage> stages)
    {
        _stages = stages.OrderBy(s => s.Cost).ToList();
    }

    /// <summary>Ordered stages (for testing/logging).</summary>
    public IReadOnlyList<IVisionPipelineStage> Stages => _stages;

    public async Task<VisionPipelineResult> ExecuteAsync(
        byte[] jpegFrame, string? query, CancellationToken ct)
    {
        foreach (var stage in _stages)
        {
            var result = await stage.ProcessAsync(jpegFrame, query, ct);
            if (result is not null)
                return result;
        }

        return new VisionPipelineResult("fallback", "Unable to analyze the image.", new());
    }
}
