using BodyCam.Services.AiProviders;

namespace BodyCam;

public enum OpenAiProvider { OpenAi, Azure }

public class AppSettings
{
    // Provider
    public string ProviderId { get; set; } = AiProviderIds.OpenAi;

    [Obsolete("Use ProviderId instead.")]
    public OpenAiProvider Provider
    {
        get => AiProviderIds.ToLegacyProvider(ProviderId);
        set => ProviderId = AiProviderIds.FromLegacyProvider(value);
    }

    // Models
    public string RealtimeModel { get; set; } = "gpt-realtime-1.5";
    public string ChatModel { get; set; } = "gpt-5.4-mini";
    public string VisionModel { get; set; } = "gpt-5.4";
    public string TranscriptionModel { get; set; } = "gpt-4o-mini-transcribe";

    // Realtime API — Direct OpenAI
    public string RealtimeApiEndpoint { get; set; } = "wss://api.openai.com/v1/realtime";
    public string ChatApiEndpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string TranscriptionApiEndpoint { get; set; } = "https://api.openai.com/v1/audio/transcriptions";
    public string Voice { get; set; } = "marin";
    public string TurnDetection { get; set; } = "semantic_vad";
    public string NoiseReduction { get; set; } = "near_field";
    public string SystemInstructions { get; set; } = """
        You are BodyCam, an AI assistant integrated into smart glasses.
        You can see what the user sees (when vision is active) and hear what they say.

        Guidelines:
        - Be concise — the user hears your response through small speakers
        - Prefer short, direct answers (1-3 sentences)
        - If vision context is available, reference what you see
        - You can be asked to remember things for later
        - Be conversational and natural
        - When the user asks to look at something, see something, or asks "what's that?",
          use the look tool. It captures immediately and describes the visible scene,
          object, hazard, or target.
        - When the user asks to describe or analyze the overall scene ("describe the scene",
          "what's going on here?"), use describe_scene for a comprehensive structured analysis.
        - Use scan_qr_code only when the user explicitly asks to scan a QR code or barcode.
        - Use read_text only when the user explicitly asks to read specific text.
        - When the user scans a product barcode or asks about a product, use
          the lookup_barcode tool to find product information.
        - For food products, mention the name, brand, Nutri-Score, calories,
          and any allergens.
        - For other products, mention the name, brand, and price range if available.
        - When asked about a previous scan, use the recall_last_scan tool
        """;

    // Azure OpenAI
    public string? AzureEndpoint { get; set; }
    public string? AzureRealtimeDeploymentName { get; set; }
    public string? AzureChatDeploymentName { get; set; }
    public string? AzureVisionDeploymentName { get; set; }
    public string AzureApiVersion { get; set; } = "2025-04-01-preview";

    // Audio
    public int InternalSampleRate { get; set; } = 48000; // Internal pipeline rate (native mic/speaker)
    public int ApiSampleRate { get; set; } = 24000;      // OpenAI Realtime API rate
    public int SampleRate => InternalSampleRate;          // Back-compat shim
    public int ChunkDurationMs { get; set; } = 50;
    public bool AecEnabled { get; set; } = true;
    public bool EnableJitterBuffer { get; set; } = true;
    public bool IosUsePlatformAecOnly { get; set; } = true; // Use iOS VoiceProcessingIO instead of WebRTC APM
    public bool WindowsUseVoiceCaptureDmo { get; set; } = false; // Opt-in fallback to Windows DMO AEC
    public float RealtimeServerVadThreshold { get; set; } = 0.75f;
    public int RealtimeServerVadPrefixPaddingMs { get; set; } = 250;
    public int RealtimeServerVadSilenceDurationMs { get; set; } = 650;

    // AGC tuning (Phase 5.1)
    public int AgcTargetLevelDbfs { get; set; } = -9;   // Target level in dB below full scale (-9 prevents clipping)
    public int AgcCompressionGainDb { get; set; } = 6;  // Compression gain in dB (6 reduces pumping artifacts)

    // Noise suppression (Phase 5.2)
    public int NoiseSuppressionLevel { get; set; } = 1; // 0=Off, 1=Moderate, 2=High, 3=VeryHigh (1 avoids musical noise)

    // Legacy setting kept for settings compatibility; mic input remains live for barge-in.
    public bool PauseMicWhilePlaying { get; set; } = false;

    // Observability (Phase 6)
    public bool DisableAec { get; set; } = false; // When true, bypass AEC entirely (for A/B testing)
    public bool DebugMode { get; set; } = false;  // When true, show debug overlay and enable WAV capture

    // Microphone coordination
    public int MicReleaseDelayMs { get; set; } = 50;

    public Uri GetRealtimeUri() =>
        AiProviderRegistry.Default.GetRequiredAdapter(ProviderId).GetRealtimeUri(this);

    public Uri GetChatUri() =>
        AiProviderRegistry.Default.GetRequiredAdapter(ProviderId).GetChatUri(this);

    public Uri GetVisionUri() =>
        AiProviderRegistry.Default.GetRequiredAdapter(ProviderId).GetVisionUri(this);

}
