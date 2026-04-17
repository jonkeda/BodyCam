using BodyCam.Services;
using NAudio.Wave;

namespace BodyCam.Platforms.Windows;

public class WindowsAudioInputService : IAudioInputService, IDisposable
{
    private readonly AppSettings _settings;
    private WaveInEvent? _waveIn;

    public bool IsCapturing { get; private set; }
    public event EventHandler<byte[]>? AudioChunkAvailable;

    public WindowsAudioInputService(AppSettings settings)
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

    public void Dispose()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;
    }
}
