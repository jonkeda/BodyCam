namespace BodyCam;

public enum OpenAiProvider { OpenAi, Azure }

public class AppSettings
{
    // Provider
    public OpenAiProvider Provider { get; set; } = OpenAiProvider.OpenAi;

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
        - When the user asks to look at something, see something, or asks "what's that?"
          or "scan that", use the look tool. It automatically checks for QR codes, reads
          text, and describes the scene — returning the first useful result.
        - When the user asks to describe or analyze the overall scene ("describe the scene",
          "what's going on here?"), use describe_scene for a comprehensive structured analysis.
        - Use scan_qr_code only when the user explicitly asks to scan a barcode.
        - Use read_text only when the user explicitly asks to read specific text.
        - When asked about a previous scan, use the recall_last_scan tool
        """;

    // Azure OpenAI
    public string? AzureEndpoint { get; set; }
    public string? AzureRealtimeDeploymentName { get; set; }
    public string? AzureChatDeploymentName { get; set; }
    public string? AzureVisionDeploymentName { get; set; }
    public string AzureApiVersion { get; set; } = "2025-04-01-preview";

    // Audio
    public int SampleRate { get; set; } = 24000;
    public int ChunkDurationMs { get; set; } = 50;
    public bool AecEnabled { get; set; } = true;

    // Microphone coordination
    public int MicReleaseDelayMs { get; set; } = 50;

    private string AzureBase => AzureEndpoint?.TrimEnd('/') ?? string.Empty;

    public Uri GetRealtimeUri() => Provider switch
    {
        OpenAiProvider.Azure =>
            new Uri($"{AzureBase.Replace("https://", "wss://")}/openai/realtime"
                  + $"?api-version={AzureApiVersion}&deployment={AzureRealtimeDeploymentName}"),
        _ =>
            new Uri($"{RealtimeApiEndpoint}?model={RealtimeModel}")
    };

    public Uri GetChatUri() => Provider switch
    {
        OpenAiProvider.Azure =>
            new Uri($"{AzureBase}/openai/deployments/{AzureChatDeploymentName}"
                  + $"/chat/completions?api-version={AzureApiVersion}"),
        _ =>
            new Uri(ChatApiEndpoint)
    };

    public Uri GetVisionUri() => Provider switch
    {
        OpenAiProvider.Azure =>
            new Uri($"{AzureBase}/openai/deployments/{AzureVisionDeploymentName}"
                  + $"/chat/completions?api-version={AzureApiVersion}"),
        _ =>
            new Uri(ChatApiEndpoint)
    };

}
