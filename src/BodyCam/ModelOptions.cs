namespace BodyCam;

public record ModelInfo(string Id, string Label);

public static class ModelOptions
{
    // --- Realtime (voice) ---
    public const string DefaultRealtime = "gpt-realtime-1.5";
    public static readonly ModelInfo[] RealtimeModels =
    [
        new("gpt-realtime-1.5", "Realtime 1.5 (Premium)"),
        new("gpt-realtime-mini", "Realtime Mini (Budget)"),
    ];

    // --- Chat (text reasoning) ---
    public const string DefaultChat = "gpt-5.4-mini";
    public static readonly ModelInfo[] ChatModels =
    [
        new("gpt-5.4", "GPT-5.4 (Flagship)"),
        new("gpt-5.4-mini", "GPT-5.4 Mini"),
        new("gpt-5.4-nano", "GPT-5.4 Nano (Cheapest)"),
    ];

    // --- Vision ---
    public const string DefaultVision = "gpt-5.4";
    public static readonly ModelInfo[] VisionModels =
    [
        new("gpt-5.4", "GPT-5.4 (Flagship)"),
        new("gpt-5.4-mini", "GPT-5.4 Mini"),
    ];

    // --- Transcription (inside Realtime session) ---
    public const string DefaultTranscription = "gpt-4o-mini-transcribe";
    public static readonly ModelInfo[] TranscriptionModels =
    [
        new("gpt-4o-mini-transcribe", "GPT-4o Mini Transcribe"),
        new("gpt-4o-transcribe", "GPT-4o Transcribe (Best)"),
    ];

    // --- Voice presets ---
    public const string DefaultVoice = "marin";
    public static readonly string[] Voices =
    [
        "alloy", "ash", "ballad", "coral", "echo",
        "fable", "marin", "sage", "shimmer", "verse",
    ];

    // --- Turn detection ---
    public const string DefaultTurnDetection = "semantic_vad";
    public static readonly string[] TurnDetectionModes =
    [
        "semantic_vad",
        "server_vad",
    ];

    // --- Noise reduction ---
    public const string DefaultNoiseReduction = "near_field";
    public static readonly string[] NoiseReductionModes =
    [
        "near_field",
        "far_field",
    ];

    public static string Label(string modelId)
    {
        var all = RealtimeModels
            .Concat(ChatModels)
            .Concat(VisionModels)
            .Concat(TranscriptionModels);

        return all.FirstOrDefault(m => m.Id == modelId)?.Label ?? modelId;
    }
}
