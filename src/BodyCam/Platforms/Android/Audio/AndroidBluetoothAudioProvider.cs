using Android.Bluetooth;
using Android.Content;
using Android.Media;
using BodyCam.Services.Audio;

namespace BodyCam.Platforms.Android.Audio;

/// <summary>
/// Audio input from a Bluetooth HFP device on Android.
/// Routes audio through BT SCO and captures via AudioRecord.
/// </summary>
public sealed class AndroidBluetoothAudioProvider : IAudioInputProvider, IDisposable
{
    private readonly AppSettings _settings;
    private readonly Context _context;
    private readonly BluetoothDevice _btDevice;
    private AudioRecord? _audioRecord;
    private CancellationTokenSource? _recordCts;
    private Task? _recordTask;

    public string DisplayName { get; }
    public string ProviderId { get; }
    public bool IsAvailable { get; private set; } = true;
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public AndroidBluetoothAudioProvider(
        BluetoothDevice btDevice,
        AppSettings settings,
        Context context)
    {
        _btDevice = btDevice;
        _settings = settings;
        _context = context;
        DisplayName = $"BT: {btDevice.Name ?? "Unknown"}";
        ProviderId = $"bt:{btDevice.Address}";
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return;

        // Request permissions
        var micStatus = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (micStatus != PermissionStatus.Granted)
        {
            micStatus = await Permissions.RequestAsync<Permissions.Microphone>();
            if (micStatus != PermissionStatus.Granted)
                throw new PermissionException("Microphone permission denied.");
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var btStatus = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
            if (btStatus != PermissionStatus.Granted)
            {
                btStatus = await Permissions.RequestAsync<Permissions.Bluetooth>();
                if (btStatus != PermissionStatus.Granted)
                    throw new PermissionException("Bluetooth permission denied.");
            }
        }

        var audioManager = (AudioManager)_context.GetSystemService(Context.AudioService)!;

        // Route audio to BT SCO
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var devices = audioManager.GetDevices(AudioDeviceType.Input);
            var btInputDevice = devices?.FirstOrDefault(d =>
                d.Type == AudioDeviceType.BluetoothSco);

            if (btInputDevice is not null)
                audioManager.SetCommunicationDevice(btInputDevice);
            else
                throw new InvalidOperationException("BT audio device not found as input.");
        }
        else
        {
#pragma warning disable CA1422 // Validate platform compatibility
            audioManager.BluetoothScoOn = true;
            audioManager.StartBluetoothSco();
#pragma warning restore CA1422

            await WaitForScoConnectionAsync(ct);
        }

        // SCO audio is typically 16kHz (mSBC, HFP 1.6+)
        int scoSampleRate = 16000;
        int bufferSize = AudioRecord.GetMinBufferSize(
            scoSampleRate,
            ChannelIn.Mono,
            Encoding.Pcm16bit);

        _audioRecord = new AudioRecord(
            AudioSource.VoiceCommunication,
            scoSampleRate,
            ChannelIn.Mono,
            Encoding.Pcm16bit,
            bufferSize);

        // Enable noise suppression and echo cancellation if available
        if (NoiseSuppressor.IsAvailable)
            NoiseSuppressor.Create(_audioRecord.AudioSessionId)?.SetEnabled(true);

        if (AcousticEchoCanceler.IsAvailable)
            AcousticEchoCanceler.Create(_audioRecord.AudioSessionId)?.SetEnabled(true);

        _audioRecord.StartRecording();
        IsCapturing = true;

        _recordCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _recordTask = Task.Run(() => RecordLoopAsync(scoSampleRate, _recordCts.Token));
    }

    private async Task RecordLoopAsync(int sourceSampleRate, CancellationToken ct)
    {
        int chunkBytes = sourceSampleRate * 2 * _settings.ChunkDurationMs / 1000;
        var buffer = new byte[chunkBytes];

        while (!ct.IsCancellationRequested
            && _audioRecord?.RecordingState == RecordState.Recording)
        {
            int bytesRead = await _audioRecord.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                var chunk = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);

                // Resample from SCO rate to app rate
                if (sourceSampleRate != _settings.SampleRate)
                    chunk = AudioResampler.Resample(chunk, sourceSampleRate, _settings.SampleRate);

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

        ReleaseSco();
        return Task.CompletedTask;
    }

    private void ReleaseSco()
    {
        var audioManager = (AudioManager)_context.GetSystemService(Context.AudioService)!;

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            audioManager.ClearCommunicationDevice();
        }
        else
        {
#pragma warning disable CA1422
            audioManager.StopBluetoothSco();
            audioManager.BluetoothScoOn = false;
#pragma warning restore CA1422
        }
    }

    private async Task WaitForScoConnectionAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource();
        var receiver = new ScoStateReceiver(tcs);

        var filter = new IntentFilter(AudioManager.ActionScoAudioStateUpdated);
        _context.RegisterReceiver(receiver, filter);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            timeoutCts.Token.Register(() => tcs.TrySetCanceled());

            await tcs.Task;
        }
        finally
        {
            _context.UnregisterReceiver(receiver);
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _recordCts?.Cancel();
        _audioRecord?.Stop();
        _audioRecord?.Release();
        _audioRecord = null;
    }
}
