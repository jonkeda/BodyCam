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
    private readonly object _lock = new();
    private AudioTrack? _audioTrack;
    private AudioManager? _audioManager;
    
    // Phase 5.4: Track recent output for fade-out
    private readonly Queue<byte> _recentSamples = new();
    private const int MaxRecentSamplesMs = 50;
    private int _sampleRate;

    public string DisplayName => "Phone Speaker";
    public string ProviderId => "phone-speaker";
    public bool IsAvailable => true;
    public bool IsPlaying { get; private set; }
    public int EstimatedOutputLatencyMs
    {
        get
        {
            if (_audioTrack is null) return 80;
            int bufferMs = _audioTrack.BufferSizeInFrames * 1000 / _audioTrack.SampleRate;
            return bufferMs + 25; // Buffer + typical speaker/DAC delay
        }
    }

    public event EventHandler? Disconnected;
    public event EventHandler? OutputRouteChanged;

    public PhoneSpeakerProvider(PlatformMicProvider mic)
    {
        _mic = mic;
    }

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        _sampleRate = sampleRate;

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

        // Phase 5.4: Track recent samples for fade-out
        lock (_lock)
        {
            foreach (byte b in pcmData)
                _recentSamples.Enqueue(b);

            int maxRecentBytes = _sampleRate * 2 * MaxRecentSamplesMs / 1000;
            while (_recentSamples.Count > maxRecentBytes)
                _recentSamples.Dequeue();
        }

        return Task.CompletedTask;
    }

    public void ClearBuffer()
    {
        _audioTrack?.Flush();
    }

    /// <summary>
    /// Phase 5.4: Fade out the last chunk to prevent audible click on interruption.
    /// </summary>
    public async Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default)
    {
        if (_audioTrack is null || !IsPlaying)
        {
            ClearBuffer();
            return;
        }

        byte[] fadeChunk;
        lock (_lock)
        {
            if (_recentSamples.Count == 0)
            {
                ClearBuffer();
                return;
            }

            // Take up to fadeMs worth of recent samples
            int fadeSamples = Math.Min(_sampleRate * fadeMs / 1000, _recentSamples.Count / 2);
            int fadeBytes = fadeSamples * 2;
            fadeChunk = _recentSamples.TakeLast(fadeBytes).ToArray();
        }

        // Apply linear fade-out
        for (int i = 0; i < fadeChunk.Length / 2; i++)
        {
            short sample = BitConverter.ToInt16(fadeChunk, i * 2);
            float gain = 1.0f - ((float)i / (fadeChunk.Length / 2));
            short faded = (short)(sample * gain);
            BitConverter.TryWriteBytes(fadeChunk.AsSpan(i * 2), faded);
        }

        // Write the fade chunk and wait
        _audioTrack.Write(fadeChunk, 0, fadeChunk.Length);
        await Task.Delay(fadeMs, ct);

        // Now clear
        ClearBuffer();
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
