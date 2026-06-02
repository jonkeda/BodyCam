using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Services.AiProviders;
using BodyCam.Services.Camera.Commands;

namespace BodyCam.RealTests.Fixtures;

/// <summary>
/// In-memory implementation of <see cref="ISettingsService"/> for real hardware tests.
/// Avoids MAUI Preferences dependency while allowing the full production object graph.
/// </summary>
public sealed class InMemorySettingsService : ISettingsService
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
    public string OutputMode { get; set; } = "Speak";

    // Provider
    public string ProviderId { get; set; } = AiProviderIds.OpenAi;
    public OpenAiProvider Provider
    {
        get => AiProviderIds.ToLegacyProvider(ProviderId);
        set => ProviderId = AiProviderIds.FromLegacyProvider(value);
    }
    public string? AzureEndpoint { get; set; }
    public string? AzureRealtimeDeploymentName { get; set; }
    public string? AzureChatDeploymentName { get; set; }
    public string? AzureVisionDeploymentName { get; set; }
    public string AzureApiVersion { get; set; } = "2025-04-01-preview";

    // Debug
    public bool DebugMode { get; set; }
    public bool ShowTokenCounts { get; set; }
    public bool ShowCostEstimate { get; set; }

    // System
    public string SystemInstructions { get; set; } = "You are a helpful AI assistant.";

    // Camera
    public string? ActiveCameraProvider { get; set; }
    public CameraCommandMode DefaultTouchCommandMode { get; set; } = CameraCommandMode.ManualAim;
    public LookDetailLevel DefaultLookDetailLevel { get; set; } = LookDetailLevel.Overview;
    public ReadDetailLevel DefaultReadDetailLevel { get; set; } = ReadDetailLevel.Full;
    public bool ConfirmExternalScanActions { get; set; } = true;

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
    public string? LastHeyCyanDeviceAddress { get; set; }
    public string? LastHeyCyanDeviceName { get; set; }
    public bool HeyCyanAutoReconnect { get; set; } = true;

    // A9 Camera
    public string? A9CameraIp { get; set; }
    public string? A9CameraUid { get; set; }
    public string? A9CameraUsername { get; set; }
    public string? A9CameraPassword { get; set; }

    // Vue990 Camera
    public string? Vue990CameraIp { get; set; }

    // USB Camera
    public string? UsbCameraDeviceMatch { get; set; }

    // Device Settings (JSON)
    public DeviceSettings DeviceSettings { get; set; } = new();

    // Setup
    public bool SetupCompleted { get; set; }
}
