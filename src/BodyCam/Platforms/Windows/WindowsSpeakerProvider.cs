using BodyCam.Services.Audio;
using NAudio.Wave;

namespace BodyCam.Platforms.Windows;

/// <summary>
/// Windows speaker provider using NAudio WaveOutEvent.
/// </summary>
public sealed class WindowsSpeakerProvider : IAudioOutputProvider, IDisposable
{
    private const int DesiredLatencyMs = 200;

    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _buffer;
    private readonly object _lock = new();
    
    // Phase 5.4: Track recent output for fade-out (ring buffer of last 50ms)
    private readonly Queue<byte> _recentSamples = new();
    private const int MaxRecentSamplesMs = 50;
    private int _sampleRate;

    public string DisplayName => "System Speaker";
    public string ProviderId => "windows-speaker";
    public bool IsAvailable => true;
    public bool IsPlaying { get; private set; }
    public int EstimatedOutputLatencyMs => _waveOut?.DesiredLatency ?? DesiredLatencyMs;
    public AudioOutputCapabilities OutputCapabilities => new(
        EchoPathKind.DirectDeviceSpeaker,
        NeedsEchoCancellation: true,
        IsAcousticallyIsolated: false,
        SupportsRenderReference: true,
        EstimatedOutputLatencyMs);

    public event EventHandler? Disconnected;
    public event EventHandler? OutputRouteChanged;

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        _sampleRate = sampleRate;
        var waveFormat = new WaveFormat(sampleRate, 16, 1);
        _buffer = new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(30),
            DiscardOnBufferOverflow = false
        };

        _waveOut = new WaveOutEvent
        {
            DesiredLatency = DesiredLatencyMs
        };
        _waveOut.Init(_buffer);
        _waveOut.Play();
        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsPlaying) return Task.CompletedTask;

        _waveOut?.Stop();
        _buffer?.ClearBuffer();
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public async Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (_buffer is null || !IsPlaying) return;

        // Back-pressure: wait for the buffer to drain if it's nearly full.
        var maxFill = _buffer.BufferLength - pcmData.Length;
        while (_buffer.BufferedBytes > maxFill)
        {
            await Task.Delay(20, ct);
            if (_buffer is null || !IsPlaying) return;
        }

        _buffer.AddSamples(pcmData, 0, pcmData.Length);

        // Phase 5.4: Track recent samples for fade-out (keep last 50ms)
        lock (_lock)
        {
            foreach (byte b in pcmData)
                _recentSamples.Enqueue(b);

            int maxRecentBytes = _sampleRate * 2 * MaxRecentSamplesMs / 1000;
            while (_recentSamples.Count > maxRecentBytes)
                _recentSamples.Dequeue();
        }
    }

    public void ClearBuffer()
    {
        _buffer?.ClearBuffer();
    }

    /// <summary>
    /// Phase 5.4: Fade out the last chunk to prevent audible click on interruption.
    /// </summary>
    public async Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default)
    {
        if (_buffer is null || !IsPlaying)
        {
            ClearBuffer();
            return;
        }

        byte[] fadeChunk;
        lock (_lock)
        {
            if (_recentSamples.Count == 0)
            {
                ClearBuffer();
                return;
            }

            // Take up to fadeMs worth of recent samples
            int fadeSamples = Math.Min(_sampleRate * fadeMs / 1000, _recentSamples.Count / 2);
            int fadeBytes = fadeSamples * 2;
            fadeChunk = _recentSamples.TakeLast(fadeBytes).ToArray();
        }

        // Apply linear fade-out
        for (int i = 0; i < fadeChunk.Length / 2; i++)
        {
            short sample = BitConverter.ToInt16(fadeChunk, i * 2);
            float gain = 1.0f - ((float)i / (fadeChunk.Length / 2));
            short faded = (short)(sample * gain);
            BitConverter.TryWriteBytes(fadeChunk.AsSpan(i * 2), faded);
        }

        // Play the fade chunk and wait
        _buffer.AddSamples(fadeChunk, 0, fadeChunk.Length);
        await Task.Delay(fadeMs, ct);

        // Now clear
        ClearBuffer();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        _buffer = null;
    }
}
