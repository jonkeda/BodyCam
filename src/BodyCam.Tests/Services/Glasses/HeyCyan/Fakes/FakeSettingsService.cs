using BodyCam.Services;

namespace BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;

/// <summary>
/// Fake settings service for unit testing.
/// Stores settings in memory without persistence.
/// </summary>
public sealed class FakeSettingsService : ISettingsService
{
    // Models
    public string RealtimeModel { get; set; } = "gpt-realtime-1.5";
    public string ChatModel { get; set; } = "gpt-5.4-mini";
    public string VisionModel { get; set; } = "gpt-5.4";
    public string TranscriptionModel { get; set; } = "gpt-4o-mini-transcribe";

    // Voice
    public string Voice { get; set; } = "marin";
    public string TurnDetection { get; set; } = "semantic_vad";
    public string NoiseReduction { get; set; } = "near_field";

    // Provider
    public OpenAiProvider Provider { get; set; } = OpenAiProvider.OpenAi;
    public string? AzureEndpoint { get; set; }
    public string? AzureRealtimeDeploymentName { get; set; }
    public string? AzureChatDeploymentName { get; set; }
    public string? AzureVisionDeploymentName { get; set; }
    public string AzureApiVersion { get; set; } = "2024-05-01-preview";

    // Debug
    public bool DebugMode { get; set; }
    public bool ShowTokenCounts { get; set; }
    public bool ShowCostEstimate { get; set; }

    // System
    public string SystemInstructions { get; set; } = "You are a helpful AI assistant.";

    // Camera
    public string? ActiveCameraProvider { get; set; }

    // Audio Input
    public string? ActiveAudioInputProvider { get; set; }

    // Audio Output
    public string? ActiveAudioOutputProvider { get; set; }

    // Wake Word
    public string? PicovoiceAccessKey { get; set; }

    // Diagnostics & Telemetry
    public bool SendDiagnosticData { get; set; }
    public string? AzureMonitorConnectionString { get; set; }
    public bool SendCrashReports { get; set; }
    public string? SentryDsn { get; set; }
    public bool SendUsageData { get; set; }

    // HeyCyan Glasses
    public bool FeedVoiceNotesToDictation { get; set; }

    // Setup
    public bool SetupCompleted { get; set; }
}
