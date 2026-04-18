using BodyCam.Services.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BodyCam.Platforms.Windows.Audio;

/// <summary>
/// Audio input from a Bluetooth HFP device on Windows.
/// Uses WasapiCapture on the BT audio endpoint and resamples to the app sample rate.
/// </summary>
public sealed class WindowsBluetoothAudioProvider : IAudioInputProvider, IDisposable
{
    private readonly MMDevice _device;
    private readonly AppSettings _settings;
    private WasapiCapture? _capture;

    public string DisplayName { get; }
    public string ProviderId { get; }
    public bool IsAvailable => _device.State == DeviceState.Active;
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public WindowsBluetoothAudioProvider(MMDevice device, AppSettings settings)
    {
        _device = device;
        _settings = settings;
        DisplayName = $"BT: {device.FriendlyName}";
        ProviderId = $"bt:{device.ID}";
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return Task.CompletedTask;

        _capture = new WasapiCapture(_device)
        {
            WaveFormat = _device.AudioClient.MixFormat
        };

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
        IsCapturing = true;

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsCapturing) return Task.CompletedTask;

        _capture?.StopRecording();
        IsCapturing = false;
        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        var chunk = ConvertToTargetFormat(e.Buffer, e.BytesRecorded);
        if (chunk.Length > 0)
            AudioChunkAvailable?.Invoke(this, chunk);
    }

    private byte[] ConvertToTargetFormat(byte[] buffer, int bytesRecorded)
    {
        var sourceFormat = _capture!.WaveFormat;
        var data = new byte[bytesRecorded];
        Buffer.BlockCopy(buffer, 0, data, 0, bytesRecorded);

        // Convert to mono if stereo
        if (sourceFormat.Channels > 1)
            data = DownmixToMono(data, sourceFormat.BitsPerSample, sourceFormat.Channels);

        // Convert to 16-bit if needed
        if (sourceFormat.BitsPerSample == 32 && sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            data = Float32ToPcm16(data);
        else if (sourceFormat.BitsPerSample != 16)
            return []; // Unsupported format

        // Resample to app sample rate
        int effectiveSourceRate = sourceFormat.SampleRate;
        if (effectiveSourceRate != _settings.SampleRate)
            data = AudioResampler.Resample(data, effectiveSourceRate, _settings.SampleRate);

        return data;
    }

    private static byte[] DownmixToMono(byte[] data, int bitsPerSample, int channels)
    {
        int bytesPerSample = bitsPerSample / 8;
        int frameSize = bytesPerSample * channels;
        int frameCount = data.Length / frameSize;
        var mono = new byte[frameCount * bytesPerSample];

        for (int i = 0; i < frameCount; i++)
        {
            // Take the first channel only (sufficient for voice)
            Buffer.BlockCopy(data, i * frameSize, mono, i * bytesPerSample, bytesPerSample);
        }

        return mono;
    }

    private static byte[] Float32ToPcm16(byte[] float32Data)
    {
        int sampleCount = float32Data.Length / 4;
        var pcm16 = new byte[sampleCount * 2];

        for (int i = 0; i < sampleCount; i++)
        {
            float sample = BitConverter.ToSingle(float32Data, i * 4);
            sample = Math.Clamp(sample, -1f, 1f);
            short pcmSample = (short)(sample * short.MaxValue);
            BitConverter.TryWriteBytes(pcm16.AsSpan(i * 2), pcmSample);
        }

        return pcm16;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        IsCapturing = false;
        if (e.Exception is not null)
            Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
    }
}
