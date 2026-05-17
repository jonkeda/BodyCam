namespace BodyCam.Services.Audio;

/// <summary>
/// A destination for PCM audio output (speaker, Bluetooth, USB, etc.).
/// Only one provider is active at a time, managed by AudioOutputManager.
/// </summary>
public interface IAudioOutputProvider : IAsyncDisposable
{
    /// <summary>Human-readable name for the audio output.</summary>
    string DisplayName { get; }

    /// <summary>Unique identifier for this provider type (e.g. "windows-speaker", "phone-speaker").</summary>
    string ProviderId { get; }

    /// <summary>Whether the audio output hardware is currently connected and ready.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether audio is currently playing.</summary>
    bool IsPlaying { get; }

    /// <summary>
    /// Best-effort estimate, in ms, from PlayChunkAsync return to actual sound
    /// emission. Includes OS buffer + DAC + speaker enclosure delay.
    /// Default 40ms wired desktop; 80ms phone built-in; ~200ms BT.
    /// </summary>
    int EstimatedOutputLatencyMs { get; }

    /// <summary>Fired when the route changes (BT connect/disconnect, headphones, etc).</summary>
    event EventHandler? OutputRouteChanged;

    /// <summary>Start the audio output with the given sample rate. Idempotent.</summary>
    Task StartAsync(int sampleRate, CancellationToken ct = default);

    /// <summary>Stop the audio output. Idempotent.</summary>
    Task StopAsync();

    /// <summary>Play a PCM audio chunk.</summary>
    Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default);

    /// <summary>Clear any buffered audio (for interruption handling).</summary>
    void ClearBuffer();

    /// <summary>
    /// Fade out any buffered audio over the given duration, then clear the buffer.
    /// Default fadeMs ≈ 30 prevents audible clicks on barge-in. (Phase 5.4)
    /// </summary>
    Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default);

    /// <summary>Raised when the audio output disconnects unexpectedly.</summary>
    event EventHandler? Disconnected;
}
