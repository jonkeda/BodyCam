namespace BodyCam.Services.AiProviders;

public static class GrokModelOptions
{
    public const string DefaultChat = "grok-4.3";
    public const string DefaultVision = "grok-4.3";
    public const string DefaultRealtime = "grok-voice-latest";
    public const string DefaultTranscription = "xai-stt";
    public const string DefaultTextToSpeech = "xai-tts";
    public const string DefaultImageGeneration = "grok-imagine-image-quality";

    public static readonly ModelInfo[] ChatModels =
    [
        new("grok-4.3", "Grok 4.3"),
        new("grok-4", "Grok 4"),
        new("grok-code-fast-1", "Grok Code Fast"),
    ];

    public static readonly ModelInfo[] VisionModels =
    [
        new("grok-4.3", "Grok 4.3 Vision"),
        new("grok-4", "Grok 4 Vision"),
    ];

    public static readonly ModelInfo[] RealtimeModels =
    [
        new("grok-voice-latest", "Grok Voice Latest"),
        new("grok-voice-think-fast-1.0", "Grok Voice Think Fast"),
        new("grok-voice-fast-1.0", "Grok Voice Fast"),
    ];

    public static readonly ModelInfo[] TranscriptionModels =
    [
        new("xai-stt", "xAI Speech to Text"),
    ];

    public static readonly ModelInfo[] TextToSpeechModels =
    [
        new("xai-tts", "xAI Text to Speech"),
    ];

    public static readonly ModelInfo[] ImageGenerationModels =
    [
        new("grok-imagine-image-quality", "Grok Imagine Quality"),
        new("grok-imagine-image", "Grok Imagine"),
    ];

    public static bool IsKnownChatModel(string? modelId) => Contains(ChatModels, modelId);
    public static bool IsKnownVisionModel(string? modelId) => Contains(VisionModels, modelId);
    public static bool IsKnownRealtimeModel(string? modelId) => Contains(RealtimeModels, modelId);
    public static bool IsKnownImageGenerationModel(string? modelId) => Contains(ImageGenerationModels, modelId);

    public static string NormalizeChatModel(string? modelId) =>
        IsKnownChatModel(modelId) ? modelId! : DefaultChat;

    public static string NormalizeVisionModel(string? modelId) =>
        IsKnownVisionModel(modelId) ? modelId! : DefaultVision;

    public static string NormalizeRealtimeModel(string? modelId) =>
        IsKnownRealtimeModel(modelId) ? modelId! : DefaultRealtime;

    public static string NormalizeImageGenerationModel(string? modelId) =>
        IsKnownImageGenerationModel(modelId) ? modelId! : DefaultImageGeneration;

    private static bool Contains(IEnumerable<ModelInfo> models, string? modelId) =>
        !string.IsNullOrWhiteSpace(modelId)
        && models.Any(model => string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
}
