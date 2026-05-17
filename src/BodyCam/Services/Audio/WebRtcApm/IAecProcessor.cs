namespace BodyCam.Services.Audio.WebRtcApm;

/// <summary>
/// Acoustic echo cancellation processor interface.
/// Implemented by WebRTC APM-based processor and platform-specific alternatives (Windows DMO).
/// </summary>
public interface IAecProcessor : IDisposable
{
    /// <summary>Whether AEC processing is enabled. Set to false to bypass (e.g., when headphones are connected).</summary>
    bool IsEnabled { get; set; }

    /// <summary>Initialize the AEC engine. Idempotent.</summary>
    void Initialize(bool mobileMode = false);

    /// <summary>Process a microphone capture chunk through AEC/NS/AGC.</summary>
    byte[] ProcessCapture(byte[] pcm16At48k);

    /// <summary>Feed speaker reference audio for echo cancellation.</summary>
    void FeedRenderReference(byte[] pcm16At48k);

    /// <summary>Update the speaker-to-mic delay estimate (ms).</summary>
    void UpdateStreamDelay(int totalDelayMs);

    /// <summary>Reset the render reference buffer (call after interrupting playback).</summary>
    void ResetRenderReference();
}
