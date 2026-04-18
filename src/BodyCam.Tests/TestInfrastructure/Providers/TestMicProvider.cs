using BodyCam.Services.Audio;

namespace BodyCam.Tests.TestInfrastructure.Providers;

public class TestMicProvider : IAudioInputProvider
{
    private readonly byte[] _pcmData;
    private readonly int _chunkSize;
    private readonly int _chunkIntervalMs;
    private CancellationTokenSource? _cts;

    public string DisplayName => "Test Microphone";
    public string ProviderId => "test-mic";
    public bool IsAvailable => true;
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public int ChunksEmitted { get; private set; }
    public bool FinishedPlaying { get; private set; }

    public TestMicProvider(byte[] pcmData, int chunkSize = 3200, int chunkIntervalMs = 100)
    {
        _pcmData = pcmData;
        _chunkSize = chunkSize;
        _chunkIntervalMs = chunkIntervalMs;
    }

    public TestMicProvider(string pcmFilePath, int chunkSize = 3200, int chunkIntervalMs = 100)
    {
        _pcmData = File.ReadAllBytes(pcmFilePath);
        _chunkSize = chunkSize;
        _chunkIntervalMs = chunkIntervalMs;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsCapturing = true;
        ChunksEmitted = 0;
        FinishedPlaying = false;
        _ = EmitChunksAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        IsCapturing = false;
        return Task.CompletedTask;
    }

    public void SimulateDisconnect()
    {
        IsCapturing = false;
        _cts?.Cancel();
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        ChunksEmitted = 0;
        FinishedPlaying = false;
        IsCapturing = false;
    }

    private async Task EmitChunksAsync(CancellationToken ct)
    {
        var offset = 0;
        while (!ct.IsCancellationRequested && offset < _pcmData.Length)
        {
            var remaining = _pcmData.Length - offset;
            var size = Math.Min(_chunkSize, remaining);
            var chunk = new byte[size];
            Array.Copy(_pcmData, offset, chunk, 0, size);
            AudioChunkAvailable?.Invoke(this, chunk);
            ChunksEmitted++;
            offset += size;
            try { await Task.Delay(_chunkIntervalMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        FinishedPlaying = true;
        IsCapturing = false;
    }

    public ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        return ValueTask.CompletedTask;
    }
}
