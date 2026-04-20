using Android.Media;
using Android.Media.Audiofx;
using BodyCam.Services;
using BodyCam.Services.Audio;

namespace BodyCam.Platforms.Android;

/// <summary>
/// Android microphone provider using AudioRecord.
/// </summary>
public sealed class PlatformMicProvider : IAudioInputProvider, IDisposable
{
    private readonly AppSettings _settings;
    private AudioRecord? _audioRecord;
    private AcousticEchoCanceler? _aec;
    private NoiseSuppressor? _ns;
    private CancellationTokenSource? _recordCts;
    private Task? _recordTask;

    public string DisplayName => "Phone Microphone";
    public string ProviderId => "platform";
    public bool IsAvailable => true;
    public bool IsCapturing { get; private set; }

    /// <summary>Audio session ID for the active AudioRecord. Used by PhoneSpeakerProvider to share the session.</summary>
    public int AudioSessionId => _audioRecord?.AudioSessionId ?? 0;

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public PlatformMicProvider(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return;

        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.Microphone>();
            if (status != PermissionStatus.Granted)
                throw new PermissionException("Microphone permission denied.");
        }

        int bufferSize = AudioRecord.GetMinBufferSize(
            _settings.SampleRate,
            ChannelIn.Mono,
            Encoding.Pcm16bit);

        _audioRecord = new AudioRecord(
            AudioSource.VoiceCommunication,
            _settings.SampleRate,
            ChannelIn.Mono,
            Encoding.Pcm16bit,
            bufferSize);

        // Enable hardware echo cancellation and noise suppression.
        // Must keep references alive for the duration of the recording session.
        if (AcousticEchoCanceler.IsAvailable)
        {
            _aec = AcousticEchoCanceler.Create(_audioRecord.AudioSessionId);
            _aec?.SetEnabled(true);
        }

        if (NoiseSuppressor.IsAvailable)
        {
            _ns = NoiseSuppressor.Create(_audioRecord.AudioSessionId);
            _ns?.SetEnabled(true);
        }

        _audioRecord.StartRecording();
        IsCapturing = true;

        _recordCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _recordTask = Task.Run(() => RecordLoopAsync(_recordCts.Token));
    }

    private async Task RecordLoopAsync(CancellationToken ct)
    {
        int chunkBytes = _settings.SampleRate * 2 * _settings.ChunkDurationMs / 1000;
        var buffer = new byte[chunkBytes];

        while (!ct.IsCancellationRequested && _audioRecord?.RecordingState == RecordState.Recording)
        {
            int bytesRead = await _audioRecord.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                var chunk = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);
                AudioChunkAvailable?.Invoke(this, chunk);
            }
        }
    }

    public Task StopAsync()
    {
        if (!IsCapturing) return Task.CompletedTask;
        _recordCts?.Cancel();
        _audioRecord?.Stop();
        IsCapturing = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _recordCts?.Cancel();
        _aec?.Release();
        _aec = null;
        _ns?.Release();
        _ns = null;
        _audioRecord?.Stop();
        _audioRecord?.Release();
        _audioRecord = null;
    }
}
