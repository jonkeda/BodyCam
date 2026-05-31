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
    private readonly string _deviceId;
    private WasapiOut? _wasapiOut;
    private BufferedWaveProvider? _buffer;
    private MMDevice? _activeDevice;

    public string DisplayName { get; }
    public string ProviderId { get; }
    public bool IsAvailable
    {
        get
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDevice(_deviceId);
                return device.State == DeviceState.Active;
            }
            catch { return false; }
        }
    }
    public bool IsPlaying { get; private set; }
    public int EstimatedOutputLatencyMs => 200; // Typical BT latency
    public AudioOutputCapabilities OutputCapabilities =>
        AudioCapabilityHeuristics.BluetoothOutput(DisplayName, EstimatedOutputLatencyMs);

    public event EventHandler? Disconnected;
    public event EventHandler? OutputRouteChanged;

    public WindowsBluetoothAudioOutputProvider(MMDevice device, string mac)
    {
        _deviceId = device.ID;
        DisplayName = $"BT: {device.FriendlyName}";
        ProviderId = $"bt:{mac}";
    }

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        // Re-acquire a fresh COM proxy — the original MMDevice from scan time may be stale.
        // See RCA 001.
        var enumerator = new MMDeviceEnumerator();
        MMDevice device;
        try
        {
            device = enumerator.GetDevice(_deviceId);
        }
        catch (Exception ex)
        {
            enumerator.Dispose();
            throw new InvalidOperationException(
                $"BT render endpoint '{DisplayName}' is no longer available.", ex);
        }

        if (device.State != DeviceState.Active)
        {
            var state = device.State;
            enumerator.Dispose();
            throw new InvalidOperationException(
                $"BT render endpoint '{DisplayName}' is not active (state: {state}).");
        }

        _activeDevice = device;

        var waveFormat = new WaveFormat(sampleRate, 16, 1);
        _buffer = new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(30),
            DiscardOnBufferOverflow = false
        };

        try
        {
            _wasapiOut = new WasapiOut(device, AudioClientShareMode.Shared, true, 200);
        }
        catch (InvalidCastException ex)
        {
            _activeDevice = null;
            _buffer = null;
            enumerator.Dispose();
            throw new InvalidOperationException(
                $"BT render endpoint '{DisplayName}' COM proxy is invalid — device may have disconnected.", ex);
        }

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

    public Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default)
    {
        ClearBuffer();
        return Task.CompletedTask;
    }

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
        _activeDevice = null;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
