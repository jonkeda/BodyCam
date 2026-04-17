using System.Runtime.CompilerServices;

namespace BodyCam.Services;

/// <summary>
/// Stub audio input — produces silence. Replace with platform implementation.
/// </summary>
public class AudioInputService : IAudioInputService
{
    public bool IsCapturing { get; private set; }
    public event EventHandler<byte[]>? AudioChunkAvailable;

    public Task StartAsync(CancellationToken ct = default)
    {
        IsCapturing = true;
        // TODO: Implement platform mic capture (M1)
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsCapturing = false;
        return Task.CompletedTask;
    }

    protected void OnAudioChunkAvailable(byte[] chunk)
    {
        AudioChunkAvailable?.Invoke(this, chunk);
    }
}
