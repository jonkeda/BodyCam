namespace BodyCam.Services.Vision;

/// <summary>
/// Result from a vision pipeline stage.
/// </summary>
public record VisionPipelineResult(
    string StageName,
    string Summary,
    Dictionary<string, object> Details);
