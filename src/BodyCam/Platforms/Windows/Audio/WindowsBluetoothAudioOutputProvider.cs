using BodyCam.Services.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BodyCam.Platforms.Windows.Audio;

/// <summary>
/// Audio output to a specific Bluetooth audio device on Windows via WASAPI.
/// Mirrors <see cref="WindowsSpeakerProvider"/> but targets a specific BT MMDevice render endpoint.
/// </summary>
public class WindowsBluetoothAudioOutputProvider : IAudioOutputProvider, IDisposable
{
    private readonly MMDevice _mmDevice;
    private WasapiOut? _wasapiOut;
    private BufferedWaveProvider? _buffer;

    public string DisplayName { get; }
    public string ProviderId { get; }
    public bool IsAvailable => _mmDevice.State == DeviceState.Active;
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    public WindowsBluetoothAudioOutputProvider(MMDevice device)
    {
        _mmDevice = device;
        DisplayName = $"BT: {device.FriendlyName}";
        ProviderId = $"bt-out:{device.ID}";
    }

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        var waveFormat = new WaveFormat(sampleRate, 16, 1);
        _buffer = new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(30),
            DiscardOnBufferOverflow = false
        };

        _wasapiOut = new WasapiOut(_mmDevice, AudioClientShareMode.Shared, true, 200);
        _wasapiOut.PlaybackStopped += OnPlaybackStopped;
        _wasapiOut.Init(_buffer);
        _wasapiOut.Play();

        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsPlaying) return Task.CompletedTask;

        if (_wasapiOut is not null)
            _wasapiOut.PlaybackStopped -= OnPlaybackStopped;
        _wasapiOut?.Stop();
        _buffer?.ClearBuffer();
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public async Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (_buffer is null || !IsPlaying) return;

        var maxFill = _buffer.BufferLength - pcmData.Length;
        while (_buffer.BufferedBytes > maxFill)
        {
            await Task.Delay(20, ct);
            if (_buffer is null || !IsPlaying) return;
        }

        _buffer.AddSamples(pcmData, 0, pcmData.Length);
    }

    public void ClearBuffer() => _buffer?.ClearBuffer();

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            IsPlaying = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_wasapiOut is not null)
            _wasapiOut.PlaybackStopped -= OnPlaybackStopped;
        _wasapiOut?.Stop();
        _wasapiOut?.Dispose();
        _wasapiOut = null;
        _buffer = null;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
