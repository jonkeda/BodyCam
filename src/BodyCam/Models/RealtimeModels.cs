namespace BodyCam.Models;

public class RealtimeSessionConfig
{
    public string Model { get; set; } = "gpt-realtime-1.5";
    public string Voice { get; set; } = "marin";
    public string TurnDetection { get; set; } = "semantic_vad";
    public string NoiseReduction { get; set; } = "near_field";
    public string Instructions { get; set; } = "You are a helpful assistant.";
    public int SampleRate { get; set; } = 24000;
}

public class RealtimeResponseInfo
{
    public required string ResponseId { get; set; }
    public string? ItemId { get; set; }
    public string? OutputTranscript { get; set; }
    public string? InputTranscript { get; set; }
}

public class AudioPlaybackTracker
{
    public string? CurrentItemId { get; set; }
    public int BytesPlayed { get; set; }
    public int SampleRate { get; set; } = 24000;
    public int BitsPerSample { get; set; } = 16;
    public int Channels { get; set; } = 1;

    public int PlayedMs => SampleRate == 0 ? 0
        : (int)(BytesPlayed * 1000L / (SampleRate * (BitsPerSample / 8) * Channels));

    public void Reset()
    {
        CurrentItemId = null;
        BytesPlayed = 0;
    }
}

/// <summary>
/// Information about a function call requested by the Realtime API.
/// </summary>
public record FunctionCallInfo(string CallId, string Name, string Arguments);
