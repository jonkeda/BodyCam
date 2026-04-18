namespace BodyCam.Models;

public record SessionConfig
{
    public required string RealtimeModel { get; init; }
    public required string ChatModel { get; init; }
    public required string VisionModel { get; init; }
    public required string TranscriptionModel { get; init; }
    public required string Voice { get; init; }
    public required string TurnDetection { get; init; }
    public required string NoiseReduction { get; init; }
    public required string SystemInstructions { get; init; }
}
