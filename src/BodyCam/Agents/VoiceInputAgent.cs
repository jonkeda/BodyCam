using BodyCam.Services;
using BodyCam.Services.Audio.WebRtcApm;
using Microsoft.Extensions.Logging;

namespace BodyCam.Agents;

/// <summary>
/// Pipes mic audio to the Realtime API. That's it — transcription happens via events.
/// </summary>
public class VoiceInputAgent
{
    private readonly IAudioInputService _audioInput;
    private readonly AecProcessor? _aec;
    private readonly ILogger<VoiceInputAgent> _logger;

    private Func<byte[], CancellationToken, Task>? _audioSink;
    private volatile bool _isConnected;
    private int _chunksSent;

    public VoiceInputAgent(IAudioInputService audioInput, ILogger<VoiceInputAgent> logger, AecProcessor? aec = null)
    {
        _audioInput = audioInput;
        _logger = logger;
        _aec = aec;
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
                byte[] processed = _aec is not null ? _aec.ProcessCapture(chunk) : chunk;
                await _audioSink(processed, CancellationToken.None);
                _chunksSent++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audio chunk send failed");
        }
    }
}
