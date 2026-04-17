using BodyCam.Services;

namespace BodyCam.Agents;

/// <summary>
/// Pipes mic audio to the Realtime API. That's it — transcription happens via events.
/// </summary>
public class VoiceInputAgent
{
    private readonly IAudioInputService _audioInput;
    private readonly IRealtimeClient _realtime;

    public VoiceInputAgent(IAudioInputService audioInput, IRealtimeClient realtime)
    {
        _audioInput = audioInput;
        _realtime = realtime;
    }

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
            if (_realtime.IsConnected)
                await _realtime.SendAudioChunkAsync(chunk);
        }
        catch (Exception)
        {
            // Swallow — don't crash the audio capture thread.
            // Errors surface via IRealtimeClient.ErrorOccurred.
        }
    }
}
