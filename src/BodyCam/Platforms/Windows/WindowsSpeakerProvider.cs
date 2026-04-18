using BodyCam.Services.Audio;
using NAudio.Wave;

namespace BodyCam.Platforms.Windows;

/// <summary>
/// Windows speaker provider using NAudio WaveOutEvent.
/// </summary>
public sealed class WindowsSpeakerProvider : IAudioOutputProvider, IDisposable
{
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _buffer;

    public string DisplayName => "System Speaker";
    public string ProviderId => "windows-speaker";
    public bool IsAvailable => true;
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        var waveFormat = new WaveFormat(sampleRate, 16, 1);
        _buffer = new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(30),
            DiscardOnBufferOverflow = false
        };

        _waveOut = new WaveOutEvent
        {
            DesiredLatency = 200
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
    }

    public void ClearBuffer()
    {
        _buffer?.ClearBuffer();
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
