namespace BodyCam.Services;

/// <summary>
/// Captures PCM audio chunks from the platform microphone.
/// </summary>
public interface IAudioInputService
{
    /// <summary>Starts capturing audio from the microphone.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Stops audio capture.</summary>
    Task StopAsync();

    /// <summary>Whether audio is currently being captured.</summary>
    bool IsCapturing { get; }

    /// <summary>Fires when a new PCM audio chunk is available.</summary>
    event EventHandler<byte[]>? AudioChunkAvailable;
}
