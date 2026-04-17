using BodyCam.Services;
using NAudio.Wave;

namespace BodyCam.Platforms.Windows;

public class WindowsAudioOutputService : IAudioOutputService, IDisposable
{
    private readonly AppSettings _settings;
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _buffer;

    public bool IsPlaying { get; private set; }

    public WindowsAudioOutputService(AppSettings settings)
    {
        _settings = settings;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        var waveFormat = new WaveFormat(_settings.SampleRate, 16, 1);
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
        // This prevents overflow exceptions and keeps audio continuous.
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

    public void Dispose()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        _buffer = null;
    }
}
