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

    // Camera
    string? ActiveCameraProvider { get; set; }

    // Audio Input
    string? ActiveAudioInputProvider { get; set; }

    // Audio Output
    string? ActiveAudioOutputProvider { get; set; }

    // Wake Word
    string? PicovoiceAccessKey { get; set; }

    // Diagnostics & Telemetry
    bool SendDiagnosticData { get; set; }
    string? AzureMonitorConnectionString { get; set; }
    bool SendCrashReports { get; set; }
    string? SentryDsn { get; set; }
    bool SendUsageData { get; set; }

    // Setup
    bool SetupCompleted { get; set; }
}
