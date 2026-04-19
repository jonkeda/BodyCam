using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Services.Audio.WebRtcApm;

namespace BodyCam.Agents;

/// <summary>
/// Plays Realtime API audio and tracks playback for interruption handling.
/// </summary>
public class VoiceOutputAgent
{
    private readonly IAudioOutputService _audioOutput;
    private readonly AecProcessor? _aec;
    private readonly AudioPlaybackTracker _tracker = new();

    public AudioPlaybackTracker Tracker => _tracker;

    public VoiceOutputAgent(IAudioOutputService audioOutput, AecProcessor? aec = null)
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

    public async Task PlayAudioDeltaAsync(byte[] pcmData, CancellationToken ct = default)
    {
        _aec?.FeedRenderReference(pcmData);
        await _audioOutput.PlayChunkAsync(pcmData, ct);
        _tracker.BytesPlayed += pcmData.Length;
    }

    public void HandleInterruption()
    {
        _audioOutput.ClearBuffer();
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
