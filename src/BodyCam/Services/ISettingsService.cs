namespace BodyCam.Services;

public interface ISettingsService
{
    // Models
    string RealtimeModel { get; set; }
    string ChatModel { get; set; }
    string VisionModel { get; set; }
    string TranscriptionModel { get; set; }

    // Voice
    string Voice { get; set; }
    string TurnDetection { get; set; }
    string NoiseReduction { get; set; }

    // Provider
    OpenAiProvider Provider { get; set; }
    string? AzureEndpoint { get; set; }
    string? AzureRealtimeDeploymentName { get; set; }
    string? AzureChatDeploymentName { get; set; }
    string? AzureVisionDeploymentName { get; set; }
    string AzureApiVersion { get; set; }

    // Debug
    bool DebugMode { get; set; }
    bool ShowTokenCounts { get; set; }
    bool ShowCostEstimate { get; set; }

    // System
    string SystemInstructions { get; set; }
}
