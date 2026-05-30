using System.Text.Json;
using BodyCam.Models;

namespace BodyCam.Services;

public class SettingsService : ISettingsService
{
    // Serialize writes to preferences.dat — MAUI's UnpackagedPreferencesImplementation
    // opens the file with FileShare.None, so concurrent Set() calls throw IOException.
    private static readonly object _prefsLock = new();

    // Models
    public string RealtimeModel
    {
        get => Preferences.Get(nameof(RealtimeModel), ModelOptions.DefaultRealtime);
        set { lock (_prefsLock) Preferences.Set(nameof(RealtimeModel), value); }
    }

    public string ChatModel
    {
        get => Preferences.Get(nameof(ChatModel), ModelOptions.DefaultChat);
        set { lock (_prefsLock) Preferences.Set(nameof(ChatModel), value); }
    }

    public string VisionModel
    {
        get => Preferences.Get(nameof(VisionModel), ModelOptions.DefaultVision);
        set { lock (_prefsLock) Preferences.Set(nameof(VisionModel), value); }
    }

    public string TranscriptionModel
    {
        get => Preferences.Get(nameof(TranscriptionModel), ModelOptions.DefaultTranscription);
        set { lock (_prefsLock) Preferences.Set(nameof(TranscriptionModel), value); }
    }

    // Voice
    public string Voice
    {
        get => Preferences.Get(nameof(Voice), ModelOptions.DefaultVoice);
        set { lock (_prefsLock) Preferences.Set(nameof(Voice), value); }
    }

    public string TurnDetection
    {
        get => Preferences.Get(nameof(TurnDetection), ModelOptions.DefaultTurnDetection);
        set { lock (_prefsLock) Preferences.Set(nameof(TurnDetection), value); }
    }

    public string NoiseReduction
    {
        get => Preferences.Get(nameof(NoiseReduction), ModelOptions.DefaultNoiseReduction);
        set { lock (_prefsLock) Preferences.Set(nameof(NoiseReduction), value); }
    }

    // Provider
    public OpenAiProvider Provider
    {
        get => Enum.TryParse<OpenAiProvider>(Preferences.Get(nameof(Provider), nameof(OpenAiProvider.OpenAi)), true, out var p)
            ? p : OpenAiProvider.OpenAi;
        set { lock (_prefsLock) Preferences.Set(nameof(Provider), value.ToString()); }
    }

    public string? AzureEndpoint
    {
        get { var v = Preferences.Get(nameof(AzureEndpoint), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(AzureEndpoint), value ?? string.Empty); }
    }

    public string? AzureRealtimeDeploymentName
    {
        get { var v = Preferences.Get(nameof(AzureRealtimeDeploymentName), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(AzureRealtimeDeploymentName), value ?? string.Empty); }
    }

    public string? AzureChatDeploymentName
    {
        get { var v = Preferences.Get(nameof(AzureChatDeploymentName), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(AzureChatDeploymentName), value ?? string.Empty); }
    }

    public string? AzureVisionDeploymentName
    {
        get { var v = Preferences.Get(nameof(AzureVisionDeploymentName), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(AzureVisionDeploymentName), value ?? string.Empty); }
    }

    public string AzureApiVersion
    {
        get => Preferences.Get(nameof(AzureApiVersion), "2025-04-01-preview");
        set { lock (_prefsLock) Preferences.Set(nameof(AzureApiVersion), value); }
    }

    // Debug
    public bool DebugMode
    {
        get => Preferences.Get(nameof(DebugMode), false);
        set { lock (_prefsLock) Preferences.Set(nameof(DebugMode), value); }
    }

    public bool ShowTokenCounts
    {
        get => Preferences.Get(nameof(ShowTokenCounts), false);
        set { lock (_prefsLock) Preferences.Set(nameof(ShowTokenCounts), value); }
    }

    public bool ShowCostEstimate
    {
        get => Preferences.Get(nameof(ShowCostEstimate), false);
        set { lock (_prefsLock) Preferences.Set(nameof(ShowCostEstimate), value); }
    }

    // System
    public string SystemInstructions
    {
        get => Preferences.Get(nameof(SystemInstructions), "You are a helpful assistant.");
        set { lock (_prefsLock) Preferences.Set(nameof(SystemInstructions), value); }
    }

    // Camera
    public string? ActiveCameraProvider
    {
        get { var v = Preferences.Get(nameof(ActiveCameraProvider), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(ActiveCameraProvider), value ?? string.Empty); }
    }

    // Audio Input
    public string? ActiveAudioInputProvider
    {
        get { var v = Preferences.Get(nameof(ActiveAudioInputProvider), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(ActiveAudioInputProvider), value ?? string.Empty); }
    }

    // Audio Output
    public string? ActiveAudioOutputProvider
    {
        get { var v = Preferences.Get(nameof(ActiveAudioOutputProvider), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(ActiveAudioOutputProvider), value ?? string.Empty); }
    }

    // Wake Word
    public string? PicovoiceAccessKey
    {
        get { var v = Preferences.Get(nameof(PicovoiceAccessKey), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(PicovoiceAccessKey), value ?? string.Empty); }
    }

    // Diagnostics & Telemetry
    public bool SendDiagnosticData
    {
        get => Preferences.Get(nameof(SendDiagnosticData), false);
        set { lock (_prefsLock) Preferences.Set(nameof(SendDiagnosticData), value); }
    }

    public string? AzureMonitorConnectionString
    {
        get { var v = Preferences.Get(nameof(AzureMonitorConnectionString), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(AzureMonitorConnectionString), value ?? string.Empty); }
    }

    public bool SendCrashReports
    {
        get => Preferences.Get(nameof(SendCrashReports), false);
        set { lock (_prefsLock) Preferences.Set(nameof(SendCrashReports), value); }
    }

    public string? SentryDsn
    {
        get { var v = Preferences.Get(nameof(SentryDsn), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(SentryDsn), value ?? string.Empty); }
    }

    public bool SendUsageData
    {
        get => Preferences.Get(nameof(SendUsageData), false);
        set { lock (_prefsLock) Preferences.Set(nameof(SendUsageData), value); }
    }

    // HeyCyan Glasses
    public bool FeedVoiceNotesToDictation
    {
        get => Preferences.Get(nameof(FeedVoiceNotesToDictation), false);
        set { lock (_prefsLock) Preferences.Set(nameof(FeedVoiceNotesToDictation), value); }
    }

    public string? LastHeyCyanDeviceAddress
    {
        get { var v = Preferences.Get(nameof(LastHeyCyanDeviceAddress), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(LastHeyCyanDeviceAddress), value ?? string.Empty); }
    }

    public string? LastHeyCyanDeviceName
    {
        get { var v = Preferences.Get(nameof(LastHeyCyanDeviceName), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(LastHeyCyanDeviceName), value ?? string.Empty); }
    }

    public bool HeyCyanAutoReconnect
    {
        get => Preferences.Get(nameof(HeyCyanAutoReconnect), true);
        set { lock (_prefsLock) Preferences.Set(nameof(HeyCyanAutoReconnect), value); }
    }

    // A9 Camera
    public string? A9CameraIp
    {
        get { var v = Preferences.Get(nameof(A9CameraIp), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(A9CameraIp), value ?? string.Empty); }
    }

    public string? A9CameraUid
    {
        get { var v = Preferences.Get(nameof(A9CameraUid), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(A9CameraUid), value ?? string.Empty); }
    }

    public string? A9CameraUsername
    {
        get { var v = Preferences.Get(nameof(A9CameraUsername), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(A9CameraUsername), value ?? string.Empty); }
    }

    public string? A9CameraPassword
    {
        get { var v = Preferences.Get(nameof(A9CameraPassword), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(A9CameraPassword), value ?? string.Empty); }
    }

    // Vue990 Camera
    public string? Vue990CameraIp
    {
        get { var v = Preferences.Get(nameof(Vue990CameraIp), string.Empty); return v.Length == 0 ? null : v; }
        set { lock (_prefsLock) Preferences.Set(nameof(Vue990CameraIp), value ?? string.Empty); }
    }

    // Device Settings (JSON)
    public DeviceSettings DeviceSettings
    {
        get
        {
            var json = Preferences.Get(nameof(DeviceSettings), "{}");
            return JsonSerializer.Deserialize<DeviceSettings>(json) ?? new();
        }
        set
        {
            lock (_prefsLock)
                Preferences.Set(nameof(DeviceSettings), JsonSerializer.Serialize(value));
        }
    }

    /// <summary>
    /// Migrate legacy flat device keys into the new DeviceSettings JSON object.
    /// Call once on startup — idempotent (skips if DeviceSettings already has a non-default profile).
    /// </summary>
    public void MigrateDeviceSettings()
    {
        // Skip if already migrated (profile was explicitly set to something)
        var existing = Preferences.Get(nameof(DeviceSettings), "");
        if (!string.IsNullOrEmpty(existing) && existing != "{}")
            return;

        var ds = new DeviceSettings();

        // Migrate active providers
        var camera = ActiveCameraProvider;
        var mic = ActiveAudioInputProvider;
        var speaker = ActiveAudioOutputProvider;

        ds.Active.CameraProviderId = camera;
        ds.Active.AudioInputProviderId = mic;
        ds.Active.AudioOutputProviderId = speaker;

        // If the user had custom selections, set profile to custom
        if (camera is not null || mic is not null || speaker is not null)
        {
            ds.ActiveProfileId = "custom";
            ds.Custom.CameraProviderId = camera;
            ds.Custom.AudioInputProviderId = mic;
            ds.Custom.AudioOutputProviderId = speaker;
        }

        // Migrate HeyCyan known device
        var hcAddr = LastHeyCyanDeviceAddress;
        var hcName = LastHeyCyanDeviceName;
        if (!string.IsNullOrEmpty(hcAddr))
        {
            ds.KnownDevices.Add(new KnownDevice
            {
                DeviceId = hcAddr,
                DisplayName = hcName ?? "HeyCyan Glasses",
                DeviceType = "heycyan-glasses",
                AutoReconnect = HeyCyanAutoReconnect,
            });
        }

        DeviceSettings = ds;
    }

    public bool SetupCompleted
    {
        get => Preferences.Get(nameof(SetupCompleted), false);
        set { lock (_prefsLock) Preferences.Set(nameof(SetupCompleted), value); }
    }
}
