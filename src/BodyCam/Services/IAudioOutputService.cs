namespace BodyCam.Services;

/// <summary>
/// Plays streaming PCM audio frames through the device speaker.
/// </summary>
public interface IAudioOutputService
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default);
    void ClearBuffer();
    bool IsPlaying { get; }
}
