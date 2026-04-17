namespace BodyCam.Services;

/// <summary>
/// Stub audio output — no-op. Replace with platform implementation.
/// </summary>
public class AudioOutputService : IAudioOutputService
{
    public bool IsPlaying { get; private set; }

    public Task StartAsync(CancellationToken ct = default)
    {
        IsPlaying = true;
        // TODO: Implement platform speaker playback (M1)
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        // TODO: Play PCM data through speaker
        return Task.CompletedTask;
    }

    public void ClearBuffer() { }
}
