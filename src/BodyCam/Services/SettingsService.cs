namespace BodyCam.Services;

public class SettingsService : ISettingsService
{
    // Models
    public string RealtimeModel
    {
        get => Preferences.Get(nameof(RealtimeModel), ModelOptions.DefaultRealtime);
        set => Preferences.Set(nameof(RealtimeModel), value);
    }

    public string ChatModel
    {
        get => Preferences.Get(nameof(ChatModel), ModelOptions.DefaultChat);
        set => Preferences.Set(nameof(ChatModel), value);
    }

    public string VisionModel
    {
        get => Preferences.Get(nameof(VisionModel), ModelOptions.DefaultVision);
        set => Preferences.Set(nameof(VisionModel), value);
    }

    public string TranscriptionModel
    {
        get => Preferences.Get(nameof(TranscriptionModel), ModelOptions.DefaultTranscription);
        set => Preferences.Set(nameof(TranscriptionModel), value);
    }

    // Voice
    public string Voice
    {
        get => Preferences.Get(nameof(Voice), ModelOptions.DefaultVoice);
        set => Preferences.Set(nameof(Voice), value);
    }

    public string TurnDetection
    {
        get => Preferences.Get(nameof(TurnDetection), ModelOptions.DefaultTurnDetection);
        set => Preferences.Set(nameof(TurnDetection), value);
    }

    public string NoiseReduction
    {
        get => Preferences.Get(nameof(NoiseReduction), ModelOptions.DefaultNoiseReduction);
        set => Preferences.Set(nameof(NoiseReduction), value);
    }

    // Provider
    public OpenAiProvider Provider
    {
        get => Enum.TryParse<OpenAiProvider>(Preferences.Get(nameof(Provider), nameof(OpenAiProvider.OpenAi)), true, out var p)
            ? p : OpenAiProvider.OpenAi;
        set => Preferences.Set(nameof(Provider), value.ToString());
    }

    public string? AzureEndpoint
    {
        get { var v = Preferences.Get(nameof(AzureEndpoint), string.Empty); return v.Length == 0 ? null : v; }
        set => Preferences.Set(nameof(AzureEndpoint), value ?? string.Empty);
    }

    public string? AzureRealtimeDeploymentName
    {
        get { var v = Preferences.Get(nameof(AzureRealtimeDeploymentName), string.Empty); return v.Length == 0 ? null : v; }
        set => Preferences.Set(nameof(AzureRealtimeDeploymentName), value ?? string.Empty);
    }

    public string? AzureChatDeploymentName
    {
        get { var v = Preferences.Get(nameof(AzureChatDeploymentName), string.Empty); return v.Length == 0 ? null : v; }
        set => Preferences.Set(nameof(AzureChatDeploymentName), value ?? string.Empty);
    }

    public string? AzureVisionDeploymentName
    {
        get { var v = Preferences.Get(nameof(AzureVisionDeploymentName), string.Empty); return v.Length == 0 ? null : v; }
        set => Preferences.Set(nameof(AzureVisionDeploymentName), value ?? string.Empty);
    }

    public string AzureApiVersion
    {
        get => Preferences.Get(nameof(AzureApiVersion), "2025-04-01-preview");
        set => Preferences.Set(nameof(AzureApiVersion), value);
    }

    // Debug
    public bool DebugMode
    {
        get => Preferences.Get(nameof(DebugMode), false);
        set => Preferences.Set(nameof(DebugMode), value);
    }

    public bool ShowTokenCounts
    {
        get => Preferences.Get(nameof(ShowTokenCounts), false);
        set => Preferences.Set(nameof(ShowTokenCounts), value);
    }

    public bool ShowCostEstimate
    {
        get => Preferences.Get(nameof(ShowCostEstimate), false);
        set => Preferences.Set(nameof(ShowCostEstimate), value);
    }

    // System
    public string SystemInstructions
    {
        get => Preferences.Get(nameof(SystemInstructions), "You are a helpful assistant.");
        set => Preferences.Set(nameof(SystemInstructions), value);
    }
}
