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

    /// <summary>Start the audio output with the given sample rate. Idempotent.</summary>
    Task StartAsync(int sampleRate, CancellationToken ct = default);

    /// <summary>Stop the audio output. Idempotent.</summary>
    Task StopAsync();

    /// <summary>Play a PCM audio chunk.</summary>
    Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default);

    /// <summary>Clear any buffered audio (for interruption handling).</summary>
    void ClearBuffer();

    /// <summary>Raised when the audio output disconnects unexpectedly.</summary>
    event EventHandler? Disconnected;
}
