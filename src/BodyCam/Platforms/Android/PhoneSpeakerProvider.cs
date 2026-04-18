using Android.Media;
using BodyCam.Services.Audio;

namespace BodyCam.Platforms.Android;

/// <summary>
/// Android speaker provider using AudioTrack.
/// </summary>
public sealed class PhoneSpeakerProvider : IAudioOutputProvider, IDisposable
{
    private AudioTrack? _audioTrack;

    public string DisplayName => "Phone Speaker";
    public string ProviderId => "phone-speaker";
    public bool IsAvailable => true;
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        int bufferSize = AudioTrack.GetMinBufferSize(
            sampleRate,
            ChannelOut.Mono,
            Encoding.Pcm16bit);

        _audioTrack = new AudioTrack(
            new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Speech)!
                .Build()!,
            new AudioFormat.Builder()
                .SetSampleRate(sampleRate)!
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

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _audioTrack?.Stop();
        _audioTrack?.Release();
        _audioTrack = null;
    }
}
