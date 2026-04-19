using BodyCam.Services;

namespace BodyCam.RealTests.Fixtures;

/// <summary>
/// Test implementation of <see cref="IAudioInputService"/> that allows programmatic injection
/// of audio chunks. Call <see cref="SendChunk"/> to fire <see cref="AudioChunkAvailable"/>.
/// </summary>
public sealed class PcmAudioSource : IAudioInputService
{
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;

    public Task StartAsync(CancellationToken ct = default)
    {
        IsCapturing = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsCapturing = false;
        return Task.CompletedTask;
    }

    /// <summary>Emit a single audio chunk, triggering <see cref="AudioChunkAvailable"/>.</summary>
    public void SendChunk(byte[] pcmData)
    {
        AudioChunkAvailable?.Invoke(this, pcmData);
    }

    /// <summary>
    /// Chops <paramref name="fullPcm"/> into chunks of <paramref name="chunkSize"/> bytes
    /// and emits each at <paramref name="intervalMs"/> intervals (fire-and-forget).
    /// </summary>
    public void SendSpeech(byte[] fullPcm, int chunkSize = 3200, int intervalMs = 100)
    {
        _ = Task.Run(async () =>
        {
            for (int offset = 0; offset < fullPcm.Length; offset += chunkSize)
            {
                int len = Math.Min(chunkSize, fullPcm.Length - offset);
                var chunk = new byte[len];
                Buffer.BlockCopy(fullPcm, offset, chunk, 0, len);
                SendChunk(chunk);
                if (offset + chunkSize < fullPcm.Length)
                    await Task.Delay(intervalMs);
            }
        });
    }
}
