using Android.Content;
using Android.Media;
using BodyCam.Services.Audio;

namespace BodyCam.Platforms.Android;

/// <summary>
/// Android speaker provider using AudioTrack.
/// Uses VoiceCommunication mode and shares the audio session with PlatformMicProvider
/// so that Android's built-in AcousticEchoCanceler can correlate output with mic input.
/// Routes audio to the loudspeaker (not the earpiece).
/// </summary>
public sealed class PhoneSpeakerProvider : IAudioOutputProvider, IDisposable
{
    private readonly PlatformMicProvider _mic;
    private AudioTrack? _audioTrack;
    private AudioManager? _audioManager;

    public string DisplayName => "Phone Speaker";
    public string ProviderId => "phone-speaker";
    public bool IsAvailable => true;
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    public PhoneSpeakerProvider(PlatformMicProvider mic)
    {
        _mic = mic;
    }

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        // Route communication audio to the loudspeaker instead of the earpiece
        _audioManager = (AudioManager?)Platform.AppContext.GetSystemService(Context.AudioService);
        if (_audioManager is not null)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                // Android 12+: use setCommunicationDevice to select the built-in speaker
                var devices = _audioManager.GetDevices(GetDevicesTargets.Outputs);
                var speaker = devices?.FirstOrDefault(d => d.Type == AudioDeviceType.BuiltinSpeaker);
                if (speaker is not null)
                    _audioManager.SetCommunicationDevice(speaker);
            }
            else
            {
                _audioManager.SpeakerphoneOn = true;
            }
        }

        int bufferSize = AudioTrack.GetMinBufferSize(
            sampleRate,
            ChannelOut.Mono,
            Encoding.Pcm16bit);

        // Share the mic's audio session ID so AcousticEchoCanceler can correlate
        // speaker output with mic input. Fall back to a new session if mic isn't active.
        int sessionId = _mic.AudioSessionId > 0
            ? _mic.AudioSessionId
            : AudioManager.AudioSessionIdGenerate;

        _audioTrack = new AudioTrack(
            new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.VoiceCommunication)!
                .SetContentType(AudioContentType.Speech)!
                .Build()!,
            new AudioFormat.Builder()
                .SetSampleRate(sampleRate)!
                .SetChannelMask(ChannelOut.Mono)!
                .SetEncoding(Encoding.Pcm16bit)!
                .Build()!,
            bufferSize,
            AudioTrackMode.Stream,
            sessionId);

        _audioTrack.Play();
        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsPlaying) return Task.CompletedTask;
        _audioTrack?.Stop();
        IsPlaying = false;
        RestoreAudioRouting();
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
        RestoreAudioRouting();
    }

    private void RestoreAudioRouting()
    {
        if (_audioManager is null) return;

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            _audioManager.ClearCommunicationDevice();
        else
            _audioManager.SpeakerphoneOn = false;

        _audioManager = null;
    }
}
