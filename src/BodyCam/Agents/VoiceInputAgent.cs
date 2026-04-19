using BodyCam.Services;
using BodyCam.Services.Audio.WebRtcApm;

namespace BodyCam.Agents;

/// <summary>
/// Pipes mic audio to the Realtime API. That's it — transcription happens via events.
/// </summary>
public class VoiceInputAgent
{
    private readonly IAudioInputService _audioInput;
    private readonly AecProcessor? _aec;

    private Func<byte[], CancellationToken, Task>? _audioSink;
    private volatile bool _isConnected;

    public VoiceInputAgent(IAudioInputService audioInput, AecProcessor? aec = null)
    {
        _audioInput = audioInput;
        _aec = aec;
    }

    public void SetAudioSink(Func<byte[], CancellationToken, Task>? sink) => _audioSink = sink;

    public void SetConnected(bool connected) => _isConnected = connected;

    public async Task StartAsync(CancellationToken ct = default)
    {
        _audioInput.AudioChunkAvailable += OnAudioChunk;
        await _audioInput.StartAsync(ct);
    }

    public async Task StopAsync()
    {
        _audioInput.AudioChunkAvailable -= OnAudioChunk;
        await _audioInput.StopAsync();
    }

    private async void OnAudioChunk(object? sender, byte[] chunk)
    {
        try
        {
            if (_isConnected && _audioSink is not null)
            {
                byte[] processed = _aec is not null ? _aec.ProcessCapture(chunk) : chunk;
                await _audioSink(processed, CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // Swallow — don't crash the audio capture thread.
        }
    }
}
