namespace BodyCam.Services.Vision;

/// <summary>
/// A single stage in the cascading vision pipeline.
/// Stages are executed in ascending <see cref="Cost"/> order; the first non-null result wins.
/// </summary>
public interface IVisionPipelineStage
{
    /// <summary>Display name for logging/debug.</summary>
    string Name { get; }

    /// <summary>
    /// Approximate cost tier for ordering. Lower runs first.
    /// 0 = free/local, 10 = lightweight API, 100 = full LLM vision.
    /// </summary>
    int Cost { get; }

    /// <summary>
    /// Attempt to extract information from the frame.
    /// Returns null if this stage found nothing relevant.
    /// </summary>
    Task<VisionPipelineResult?> ProcessAsync(
        byte[] jpegFrame, string? query, CancellationToken ct);
}
