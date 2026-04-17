using Android.Media;
using BodyCam.Services;

namespace BodyCam.Platforms.Android;

public class AndroidAudioInputService : IAudioInputService, IDisposable
{
    private readonly AppSettings _settings;
    private AudioRecord? _audioRecord;
    private CancellationTokenSource? _recordCts;
    private Task? _recordTask;

    public bool IsCapturing { get; private set; }
    public event EventHandler<byte[]>? AudioChunkAvailable;

    public AndroidAudioInputService(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return;

        // Request microphone permission
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

    public void Dispose()
    {
        _recordCts?.Cancel();
        _audioRecord?.Stop();
        _audioRecord?.Release();
        _audioRecord = null;
    }
}
