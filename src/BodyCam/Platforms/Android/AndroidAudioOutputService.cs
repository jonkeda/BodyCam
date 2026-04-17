using Android.Media;
using BodyCam.Services;

namespace BodyCam.Platforms.Android;

public class AndroidAudioOutputService : IAudioOutputService, IDisposable
{
    private readonly AppSettings _settings;
    private AudioTrack? _audioTrack;

    public bool IsPlaying { get; private set; }

    public AndroidAudioOutputService(AppSettings settings)
    {
        _settings = settings;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        int bufferSize = AudioTrack.GetMinBufferSize(
            _settings.SampleRate,
            ChannelOut.Mono,
            Encoding.Pcm16bit);

        _audioTrack = new AudioTrack(
            new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Speech)!
                .Build()!,
            new AudioFormat.Builder()
                .SetSampleRate(_settings.SampleRate)!
                .SetChannelMask(ChannelOut.Mono)!
                .SetEncoding(Encoding.Pcm16bit)!
                .Build()!,
            bufferSize,
            AudioTrackMode.Stream,
            AudioManager.AudioSessionIdGenerate);

        _audioTrack.Play();
        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsPlaying) return Task.CompletedTask;
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

    public void ClearBuffer()
    {
        _audioTrack?.Flush();
    }

    public void Dispose()
    {
        _audioTrack?.Stop();
        _audioTrack?.Release();
        _audioTrack = null;
    }
}
