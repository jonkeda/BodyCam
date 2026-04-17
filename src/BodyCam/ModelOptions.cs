namespace BodyCam;

public static class ModelOptions
{
    // --- Realtime (voice) ---
    public const string DefaultRealtime = "gpt-realtime-1.5";
    public static readonly string[] RealtimeModels =
    [
        "gpt-realtime-1.5",
        "gpt-realtime-mini",
    ];

    // --- Chat (text reasoning) ---
    public const string DefaultChat = "gpt-5.4-mini";
    public static readonly string[] ChatModels =
    [
        "gpt-5.4",
        "gpt-5.4-mini",
        "gpt-5.4-nano",
    ];

    // --- Vision ---
    public const string DefaultVision = "gpt-5.4";
    public static readonly string[] VisionModels =
    [
        "gpt-5.4",
        "gpt-5.4-mini",
    ];

    // --- Transcription (inside Realtime session) ---
    public const string DefaultTranscription = "gpt-4o-mini-transcribe";
    public static readonly string[] TranscriptionModels =
    [
        "gpt-4o-mini-transcribe",
        "gpt-4o-transcribe",
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

    public static string Label(string modelId) => modelId switch
    {
        "gpt-realtime-1.5"       => "Realtime 1.5 (Premium)",
        "gpt-realtime-mini"      => "Realtime Mini (Budget)",
        "gpt-5.4"                => "GPT-5.4 (Flagship)",
        "gpt-5.4-mini"           => "GPT-5.4 Mini",
        "gpt-5.4-nano"           => "GPT-5.4 Nano (Cheapest)",
        "gpt-4o-mini-transcribe" => "GPT-4o Mini Transcribe",
        "gpt-4o-transcribe"      => "GPT-4o Transcribe (Best)",
        _ => modelId,
    };
}
