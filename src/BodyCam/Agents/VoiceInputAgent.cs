using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Audio.WebRtcApm;
using Microsoft.Extensions.Logging;

namespace BodyCam.Agents;

/// <summary>
/// Pipes mic audio to the Realtime API. That's it — transcription happens via events.
/// AEC processing now happens in AudioInputManager's consumer thread.
/// </summary>
public class VoiceInputAgent
{
    private readonly IAudioInputService _audioInput;
    private readonly ILogger<VoiceInputAgent> _logger;
    private readonly AudioPlaybackTracker? _playbackTracker;
    private readonly AppSettings? _settings;
    private readonly PolyphaseFirResampler _resampler48to24 = new(48000, 24000);

    private Func<byte[], CancellationToken, Task>? _audioSink;
    private volatile bool _isConnected;
    private int _chunksSent;

    public VoiceInputAgent(
        IAudioInputService audioInput,
        ILogger<VoiceInputAgent> logger,
        AudioPlaybackTracker? playbackTracker = null,
        AppSettings? settings = null)
    {
        _audioInput = audioInput;
        _logger = logger;
        _playbackTracker = playbackTracker;
        _settings = settings;
    }

    public void SetAudioSink(Func<byte[], CancellationToken, Task>? sink) => _audioSink = sink;

    public void SetConnected(bool connected)
    {
        _isConnected = connected;
        _chunksSent = 0;
        _logger.LogInformation("VoiceInputAgent connected={Connected}", connected);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _audioInput.AudioChunkAvailable += OnAudioChunk;
        await _audioInput.StartAsync(ct);
        _logger.LogInformation("VoiceInputAgent started");
    }

    public async Task StopAsync()
    {
        _audioInput.AudioChunkAvailable -= OnAudioChunk;
        await _audioInput.StopAsync();
        _logger.LogInformation("VoiceInputAgent stopped, chunks sent: {ChunksSent}", _chunksSent);
    }

    private async void OnAudioChunk(object? sender, byte[] chunk)
    {
        try
        {
            if (_isConnected && _audioSink is not null)
            {
                // Phase 5.3: Optional mic ducking during playback
                if (_settings?.PauseMicWhilePlaying == true && _playbackTracker?.PlayedMs > 0)
                    return; // Mic is gated while AI is speaking

                // Chunk is at 48 kHz from AudioInputManager (post-AEC)
                // Resample 48k → 24k for API
                byte[] processed24k = Resample48to24(chunk);
                
                await _audioSink(processed24k, CancellationToken.None);
                _chunksSent++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audio chunk send failed");
        }
    }

    private byte[] Resample48to24(byte[] pcm16At48k)
    {
        int sampleCount48k = pcm16At48k.Length / 2;
        float[] floats48k = new float[sampleCount48k];
        for (int i = 0; i < sampleCount48k; i++)
        {
            short s = BitConverter.ToInt16(pcm16At48k, i * 2);
            floats48k[i] = s / 32768f;
        }

        int expectedSamples24k = (sampleCount48k + 1) / 2;
        float[] floats24k = new float[expectedSamples24k + 16];
        int actualSamples24k = _resampler48to24.Resample(floats48k, floats24k);

        byte[] pcm16At24k = new byte[actualSamples24k * 2];
        for (int i = 0; i < actualSamples24k; i++)
        {
            int value = (int)(floats24k[i] * 32768f);
            short clamped = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
            BitConverter.TryWriteBytes(pcm16At24k.AsSpan(i * 2), clamped);
        }
        return pcm16At24k;
    }
}
