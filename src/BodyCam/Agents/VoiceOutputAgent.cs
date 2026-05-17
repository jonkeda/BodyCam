using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Audio.WebRtcApm;

namespace BodyCam.Agents;

/// <summary>
/// Plays Realtime API audio and tracks playback for interruption handling.
/// </summary>
public class VoiceOutputAgent
{
    private readonly IAudioOutputService _audioOutput;
    private readonly IAecProcessor? _aec;
    private readonly AudioPlaybackTracker _tracker = new();
    private readonly PolyphaseFirResampler _resampler24to48 = new(24000, 48000);

    public AudioPlaybackTracker Tracker => _tracker;

    public VoiceOutputAgent(IAudioOutputService audioOutput, IAecProcessor? aec = null)
    {
        _audioOutput = audioOutput;
        _aec = aec;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _audioOutput.StartAsync(ct);
    }

    public async Task StopAsync()
    {
        _tracker.Reset();
        await _audioOutput.StopAsync();
    }

    public async Task PlayAudioDeltaAsync(byte[] pcm24k, CancellationToken ct = default)
    {
        // Resample 24k → 48k for speaker + AEC
        byte[] pcm48k = Resample24to48(pcm24k);
        
        _aec?.FeedRenderReference(pcm48k);
        await _audioOutput.PlayChunkAsync(pcm48k, ct);
        _tracker.BytesPlayed += pcm48k.Length; // Track at 48k rate
    }

    private byte[] Resample24to48(byte[] pcm16At24k)
    {
        int sampleCount24k = pcm16At24k.Length / 2;
        float[] floats24k = new float[sampleCount24k];
        for (int i = 0; i < sampleCount24k; i++)
        {
            short s = BitConverter.ToInt16(pcm16At24k, i * 2);
            floats24k[i] = s / 32768f;
        }

        int expectedSamples48k = sampleCount24k * 2;
        float[] floats48k = new float[expectedSamples48k + 32];
        int actualSamples48k = _resampler24to48.Resample(floats24k, floats48k);

        byte[] pcm16At48k = new byte[actualSamples48k * 2];
        for (int i = 0; i < actualSamples48k; i++)
        {
            int value = (int)(floats48k[i] * 32768f);
            short clamped = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
            BitConverter.TryWriteBytes(pcm16At48k.AsSpan(i * 2), clamped);
        }
        return pcm16At48k;
    }

    public async Task HandleInterruptionAsync(CancellationToken ct = default)
    {
        await _audioOutput.FadeOutAndClearAsync(fadeMs: 30, ct);
        _aec?.ResetRenderReference();
    }

    public void ResetTracker()
    {
        _tracker.Reset();
    }

    public void SetCurrentItem(string itemId)
    {
        _tracker.CurrentItemId = itemId;
    }
}
