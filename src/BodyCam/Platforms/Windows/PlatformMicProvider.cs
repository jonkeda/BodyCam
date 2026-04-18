using BodyCam.Services;
using BodyCam.Services.Audio;
using NAudio.Wave;

namespace BodyCam.Platforms.Windows;

/// <summary>
/// Windows microphone provider using NAudio WaveInEvent.
/// </summary>
public sealed class PlatformMicProvider : IAudioInputProvider, IDisposable
{
    private readonly AppSettings _settings;
    private WaveInEvent? _waveIn;

    public string DisplayName => "System Microphone";
    public string ProviderId => "platform";
    public bool IsAvailable => true;
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public PlatformMicProvider(AppSettings settings)
    {
        _settings = settings;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return Task.CompletedTask;

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(_settings.SampleRate, 16, 1),
            BufferMilliseconds = _settings.ChunkDurationMs
        };

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;
        _waveIn.StartRecording();
        IsCapturing = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsCapturing) return Task.CompletedTask;

        _waveIn?.StopRecording();
        IsCapturing = false;
        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        var chunk = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
        AudioChunkAvailable?.Invoke(this, chunk);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        IsCapturing = false;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;
    }
}
