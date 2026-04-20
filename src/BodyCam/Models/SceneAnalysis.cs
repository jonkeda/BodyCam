namespace BodyCam.Models;

/// <summary>
/// Structured scene analysis returned by the enhanced describe_scene tool.
/// </summary>
public record SceneAnalysis
{
    public required string Description { get; init; }
    public string? ExtractedText { get; init; }
    public IReadOnlyList<DetectedCode>? DetectedCodes { get; init; }
}

public record DetectedCode(
    string Format,
    string? Location);
