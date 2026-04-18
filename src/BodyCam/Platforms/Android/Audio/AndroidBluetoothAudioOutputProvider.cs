using Android.Content;
using Android.Media;
using BodyCam.Services.Audio;

namespace BodyCam.Platforms.Android.Audio;

/// <summary>
/// Audio output to a specific Bluetooth audio device on Android.
/// Routes audio via <see cref="AudioTrack.SetPreferredDevice"/> to the specific BT device.
/// Monitors for device disconnection via <see cref="AudioDeviceCallback"/>.
/// </summary>
public class AndroidBluetoothAudioOutputProvider : IAudioOutputProvider, IDisposable
{
    private readonly AudioDeviceInfo _device;
    private readonly Context _context;
    private AudioTrack? _audioTrack;
    private AudioManager? _audioManager;
    private OutputDeviceCallback? _deviceCallback;

    public string DisplayName { get; }
    public string ProviderId { get; }
    public bool IsAvailable => true;
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    public AndroidBluetoothAudioOutputProvider(AudioDeviceInfo device, Context context)
    {
        _device = device;
        _context = context;
        DisplayName = $"BT: {device.ProductName}";
        ProviderId = $"bt-out:{device.Id}";
    }

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        _audioManager = (AudioManager?)_context.GetSystemService(Context.AudioService);

        int bufferSize = AudioTrack.GetMinBufferSize(
            sampleRate,
            ChannelOut.Mono,
            Encoding.Pcm16bit);

        // Larger buffer for BT to absorb jitter — at least 200ms
        bufferSize = Math.Max(bufferSize, sampleRate * 2 / 5);

        _audioTrack = new AudioTrack(
            new AudioAttributes.Builder()!
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Speech)!
                .Build()!,
            new AudioFormat.Builder()!
                .SetSampleRate(sampleRate)!
                .SetChannelMask(ChannelOut.Mono)!
                .SetEncoding(Encoding.Pcm16bit)!
                .Build()!,
            bufferSize,
            AudioTrackMode.Stream,
            AudioManager.AudioSessionIdGenerate);

        _audioTrack.SetPreferredDevice(_device);
        _audioTrack.Play();

        RegisterDeviceCallback();

        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsPlaying) return Task.CompletedTask;

        UnregisterDeviceCallback();
        _audioTrack?.Stop();
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (_audioTrack is null || !IsPlaying) return Task.CompletedTask;
        _audioTrack.Write(pcmData, 0, pcmData.Length);
        return Task.CompletedTask;
    }

    public void ClearBuffer() => _audioTrack?.Flush();

    private void RegisterDeviceCallback()
    {
        if (_audioManager is null) return;
        _deviceCallback = new OutputDeviceCallback(this);
        _audioManager.RegisterAudioDeviceCallback(_deviceCallback, null);
    }

    private void UnregisterDeviceCallback()
    {
        if (_audioManager is null || _deviceCallback is null) return;
        _audioManager.UnregisterAudioDeviceCallback(_deviceCallback);
        _deviceCallback = null;
    }

    internal void OnDeviceRemoved(AudioDeviceInfo removedDevice)
    {
        if (removedDevice.Id == _device.Id)
        {
            IsPlaying = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        UnregisterDeviceCallback();
        _audioTrack?.Stop();
        _audioTrack?.Release();
        _audioTrack = null;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private class OutputDeviceCallback : AudioDeviceCallback
    {
        private readonly AndroidBluetoothAudioOutputProvider _provider;

        public OutputDeviceCallback(AndroidBluetoothAudioOutputProvider provider)
            => _provider = provider;

        public override void OnAudioDevicesRemoved(AudioDeviceInfo[]? removedDevices)
        {
            if (removedDevices is null) return;
            foreach (var device in removedDevices)
            {
                if (device.Type is AudioDeviceType.BluetoothA2dp
                                or AudioDeviceType.BluetoothSco)
                {
                    _provider.OnDeviceRemoved(device);
                }
            }
        }
    }
}
