using BodyCam.Services;

namespace BodyCam.RealTests.Fixtures;

/// <summary>
/// Test implementation of <see cref="IAudioOutputService"/> that records all played chunks
/// instead of sending them to hardware.
/// </summary>
public sealed class AudioCaptureSink : IAudioOutputService
{
    private readonly List<byte[]> _chunks = [];
    private readonly object _lock = new();

    public bool IsPlaying { get; private set; }

    public IReadOnlyList<byte[]> Chunks
    {
        get { lock (_lock) return _chunks.ToList(); }
    }

    public long TotalBytes
    {
        get { lock (_lock) return _chunks.Sum(c => (long)c.Length); }
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        lock (_lock)
            _chunks.Add(pcmData);
        return Task.CompletedTask;
    }

    public void ClearBuffer()
    {
        lock (_lock)
            _chunks.Clear();
    }

    public void Clear()
    {
        lock (_lock)
            _chunks.Clear();
    }
}
