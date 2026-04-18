namespace BodyCam.Services.Audio;

/// <summary>
/// A source of PCM audio input (microphone, USB, Bluetooth, etc.).
/// Only one provider is active at a time, managed by AudioInputManager.
/// </summary>
public interface IAudioInputProvider : IAsyncDisposable
{
    /// <summary>Human-readable name for the audio source.</summary>
    string DisplayName { get; }

    /// <summary>Unique identifier for this provider type (e.g. "platform", "usb", "bluetooth").</summary>
    string ProviderId { get; }

    /// <summary>Whether the audio hardware is currently connected and ready.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether audio is currently being captured.</summary>
    bool IsCapturing { get; }

    /// <summary>Start capturing audio. Idempotent.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Stop capturing audio. Idempotent.</summary>
    Task StopAsync();

    /// <summary>Fires when a new PCM audio chunk is available.</summary>
    event EventHandler<byte[]>? AudioChunkAvailable;

    /// <summary>Raised when the audio source disconnects unexpectedly.</summary>
    event EventHandler? Disconnected;
}
