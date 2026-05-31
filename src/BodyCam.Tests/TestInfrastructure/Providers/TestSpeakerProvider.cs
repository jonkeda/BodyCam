using BodyCam.Services.Audio;

namespace BodyCam.Tests.TestInfrastructure.Providers;

public class TestSpeakerProvider : IAudioOutputProvider
{
    private const int DefaultLatencyMs = 50;

    private readonly List<byte[]> _chunks = new();
    private readonly object _lock = new();
    private readonly AudioOutputCapabilities _outputCapabilities;

    public string DisplayName => "Test Speaker";
    public string ProviderId => "test-speaker";
    public bool IsAvailable => true;
    public bool IsPlaying { get; private set; }
    public int SampleRate { get; private set; }
    public int EstimatedOutputLatencyMs => _outputCapabilities.EstimatedOutputLatencyMs;
    public AudioOutputCapabilities OutputCapabilities => _outputCapabilities;

    public event EventHandler? Disconnected;
    public event EventHandler? OutputRouteChanged;

    public IReadOnlyList<byte[]> CapturedChunks { get { lock (_lock) return _chunks.ToList(); } }
    public int TotalBytesPlayed { get { lock (_lock) return _chunks.Sum(c => c.Length); } }
    public int ChunkCount { get { lock (_lock) return _chunks.Count; } }
    public bool WasAudioPlayed { get { lock (_lock) return _chunks.Count > 0; } }

    public TestSpeakerProvider(AudioOutputCapabilities? outputCapabilities = null)
    {
        _outputCapabilities = outputCapabilities ?? new AudioOutputCapabilities(
            EchoPathKind.DirectDeviceSpeaker,
            NeedsEchoCancellation: true,
            IsAcousticallyIsolated: false,
            SupportsRenderReference: true,
            EstimatedOutputLatencyMs: DefaultLatencyMs);
    }

    public byte[] GetCapturedBytes()
    {
        lock (_lock) return _chunks.SelectMany(c => c).ToArray();
    }

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        SampleRate = sampleRate;
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
        lock (_lock) _chunks.Add(pcmData.ToArray());
        return Task.CompletedTask;
    }

    public void ClearBuffer()
    {
        lock (_lock) _chunks.Clear();
    }

    public Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default)
    {
        ClearBuffer();
        return Task.CompletedTask;
    }

    public void SimulateDisconnect()
    {
        IsPlaying = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        lock (_lock) _chunks.Clear();
        IsPlaying = false;
        SampleRate = 0;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
