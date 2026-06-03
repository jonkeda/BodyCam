using BodyCam.Models;
using BodyCam.Services.Camera.Commands;

namespace BodyCam.Services.Settings;

public interface IAppPreferencesStore
{
    string OutputMode { get; set; }
    string SystemInstructions { get; set; }
    bool DebugMode { get; set; }
    bool SetupCompleted { get; set; }
}

public interface IAiProviderSettingsStore
{
    string ProviderId { get; set; }
    string RealtimeModel { get; set; }
    string ChatModel { get; set; }
    string VisionModel { get; set; }
    string TranscriptionModel { get; set; }
    string Voice { get; set; }
    string TurnDetection { get; set; }
    string NoiseReduction { get; set; }
    string? AzureEndpoint { get; set; }
    string? AzureRealtimeDeploymentName { get; set; }
    string? AzureChatDeploymentName { get; set; }
    string? AzureVisionDeploymentName { get; set; }
    string AzureApiVersion { get; set; }
}

public interface IDeviceSettingsStore
{
    DeviceSettings DeviceSettings { get; set; }
    string? ActiveCameraProvider { get; set; }
    CameraCommandMode DefaultTouchCommandMode { get; set; }
    LookDetailLevel DefaultLookDetailLevel { get; set; }
    ReadDetailLevel DefaultReadDetailLevel { get; set; }
    bool ConfirmExternalScanActions { get; set; }
    string? ActiveAudioInputProvider { get; set; }
    string? ActiveAudioOutputProvider { get; set; }
    bool FeedVoiceNotesToDictation { get; set; }
    string? LastHeyCyanDeviceAddress { get; set; }
    string? LastHeyCyanDeviceName { get; set; }
    bool HeyCyanAutoReconnect { get; set; }
    string? A9CameraIp { get; set; }
    string? A9CameraUid { get; set; }
    string? A9CameraUsername { get; set; }
    string? A9CameraPassword { get; set; }
    string? Vue990CameraIp { get; set; }
    string? UsbCameraDeviceMatch { get; set; }
}

public interface IDiagnosticsSettingsStore
{
    bool ShowTokenCounts { get; set; }
    bool ShowCostEstimate { get; set; }
    bool SendDiagnosticData { get; set; }
    string? AzureMonitorConnectionString { get; set; }
    bool SendCrashReports { get; set; }
    string? SentryDsn { get; set; }
    bool SendUsageData { get; set; }
}

public sealed class SettingsStoreFacade :
    IAppPreferencesStore,
    IAiProviderSettingsStore,
    IDeviceSettingsStore,
    IDiagnosticsSettingsStore
{
    private readonly ISettingsService _settings;

    public SettingsStoreFacade(ISettingsService settings)
    {
        _settings = settings;
    }

    public string OutputMode
    {
        get => _settings.OutputMode;
        set => _settings.OutputMode = value;
    }

    public string SystemInstructions
    {
        get => _settings.SystemInstructions;
        set => _settings.SystemInstructions = value;
    }

    public bool DebugMode
    {
        get => _settings.DebugMode;
        set => _settings.DebugMode = value;
    }

    public bool SetupCompleted
    {
        get => _settings.SetupCompleted;
        set => _settings.SetupCompleted = value;
    }

    public string ProviderId
    {
        get => _settings.ProviderId;
        set => _settings.ProviderId = value;
    }

    public string RealtimeModel
    {
        get => _settings.RealtimeModel;
        set => _settings.RealtimeModel = value;
    }

    public string ChatModel
    {
        get => _settings.ChatModel;
        set => _settings.ChatModel = value;
    }

    public string VisionModel
    {
        get => _settings.VisionModel;
        set => _settings.VisionModel = value;
    }

    public string TranscriptionModel
    {
        get => _settings.TranscriptionModel;
        set => _settings.TranscriptionModel = value;
    }

    public string Voice
    {
        get => _settings.Voice;
        set => _settings.Voice = value;
    }

    public string TurnDetection
    {
        get => _settings.TurnDetection;
        set => _settings.TurnDetection = value;
    }

    public string NoiseReduction
    {
        get => _settings.NoiseReduction;
        set => _settings.NoiseReduction = value;
    }

    public string? AzureEndpoint
    {
        get => _settings.AzureEndpoint;
        set => _settings.AzureEndpoint = value;
    }

    public string? AzureRealtimeDeploymentName
    {
        get => _settings.AzureRealtimeDeploymentName;
        set => _settings.AzureRealtimeDeploymentName = value;
    }

    public string? AzureChatDeploymentName
    {
        get => _settings.AzureChatDeploymentName;
        set => _settings.AzureChatDeploymentName = value;
    }

    public string? AzureVisionDeploymentName
    {
        get => _settings.AzureVisionDeploymentName;
        set => _settings.AzureVisionDeploymentName = value;
    }

    public string AzureApiVersion
    {
        get => _settings.AzureApiVersion;
        set => _settings.AzureApiVersion = value;
    }

    public DeviceSettings DeviceSettings
    {
        get => _settings.DeviceSettings;
        set => _settings.DeviceSettings = value;
    }

    public string? ActiveCameraProvider
    {
        get => _settings.ActiveCameraProvider;
        set => _settings.ActiveCameraProvider = value;
    }

    public CameraCommandMode DefaultTouchCommandMode
    {
        get => _settings.DefaultTouchCommandMode;
        set => _settings.DefaultTouchCommandMode = value;
    }

    public LookDetailLevel DefaultLookDetailLevel
    {
        get => _settings.DefaultLookDetailLevel;
        set => _settings.DefaultLookDetailLevel = value;
    }

    public ReadDetailLevel DefaultReadDetailLevel
    {
        get => _settings.DefaultReadDetailLevel;
        set => _settings.DefaultReadDetailLevel = value;
    }

    public bool ConfirmExternalScanActions
    {
        get => _settings.ConfirmExternalScanActions;
        set => _settings.ConfirmExternalScanActions = value;
    }

    public string? ActiveAudioInputProvider
    {
        get => _settings.ActiveAudioInputProvider;
        set => _settings.ActiveAudioInputProvider = value;
    }

    public string? ActiveAudioOutputProvider
    {
        get => _settings.ActiveAudioOutputProvider;
        set => _settings.ActiveAudioOutputProvider = value;
    }

    public bool FeedVoiceNotesToDictation
    {
        get => _settings.FeedVoiceNotesToDictation;
        set => _settings.FeedVoiceNotesToDictation = value;
    }

    public string? LastHeyCyanDeviceAddress
    {
        get => _settings.LastHeyCyanDeviceAddress;
        set => _settings.LastHeyCyanDeviceAddress = value;
    }

    public string? LastHeyCyanDeviceName
    {
        get => _settings.LastHeyCyanDeviceName;
        set => _settings.LastHeyCyanDeviceName = value;
    }

    public bool HeyCyanAutoReconnect
    {
        get => _settings.HeyCyanAutoReconnect;
        set => _settings.HeyCyanAutoReconnect = value;
    }

    public string? A9CameraIp
    {
        get => _settings.A9CameraIp;
        set => _settings.A9CameraIp = value;
    }

    public string? A9CameraUid
    {
        get => _settings.A9CameraUid;
        set => _settings.A9CameraUid = value;
    }

    public string? A9CameraUsername
    {
        get => _settings.A9CameraUsername;
        set => _settings.A9CameraUsername = value;
    }

    public string? A9CameraPassword
    {
        get => _settings.A9CameraPassword;
        set => _settings.A9CameraPassword = value;
    }

    public string? Vue990CameraIp
    {
        get => _settings.Vue990CameraIp;
        set => _settings.Vue990CameraIp = value;
    }

    public string? UsbCameraDeviceMatch
    {
        get => _settings.UsbCameraDeviceMatch;
        set => _settings.UsbCameraDeviceMatch = value;
    }

    public bool ShowTokenCounts
    {
        get => _settings.ShowTokenCounts;
        set => _settings.ShowTokenCounts = value;
    }

    public bool ShowCostEstimate
    {
        get => _settings.ShowCostEstimate;
        set => _settings.ShowCostEstimate = value;
    }

    public bool SendDiagnosticData
    {
        get => _settings.SendDiagnosticData;
        set => _settings.SendDiagnosticData = value;
    }

    public string? AzureMonitorConnectionString
    {
        get => _settings.AzureMonitorConnectionString;
        set => _settings.AzureMonitorConnectionString = value;
    }

    public bool SendCrashReports
    {
        get => _settings.SendCrashReports;
        set => _settings.SendCrashReports = value;
    }

    public string? SentryDsn
    {
        get => _settings.SentryDsn;
        set => _settings.SentryDsn = value;
    }

    public bool SendUsageData
    {
        get => _settings.SendUsageData;
        set => _settings.SendUsageData = value;
    }
}
