# M1 Implementation — Step 8: Android Audio (Input + Output)

**Depends on:** Step 3+4 (interfaces finalized, Windows impl as reference)
**Produces:** `AndroidAudioInputService`, `AndroidAudioOutputService`

---

## Why This Step?
The ROADMAP lists Android as a target platform. This step provides the platform-specific audio services for Android, mirroring what NAudio does on Windows.

---

## Tasks

### 8.1 — Implement `AndroidAudioInputService`

**File:** `src/BodyCam/Platforms/Android/AndroidAudioInputService.cs`

```csharp
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

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return Task.CompletedTask;

        int bufferSize = AudioRecord.GetMinBufferSize(
            _settings.SampleRate,
            ChannelIn.Mono,
            Encoding.Pcm16bit);

        // Use VoiceCommunication for automatic echo cancellation
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

        return Task.CompletedTask;
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
```

### 8.2 — Implement `AndroidAudioOutputService`

**File:** `src/BodyCam/Platforms/Android/AndroidAudioOutputService.cs`

```csharp
using Android.Media;
using BodyCam.Services;

namespace BodyCam.Platforms.Android;

public class AndroidAudioOutputService : IAudioOutputService, IDisposable
{
    private readonly AppSettings _settings;
    private AudioTrack? _audioTrack;

    public bool IsPlaying { get; private set; }

    public AndroidAudioOutputService(AppSettings settings)
    {
        _settings = settings;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        int bufferSize = AudioTrack.GetMinBufferSize(
            _settings.SampleRate,
            ChannelOut.Mono,
            Encoding.Pcm16bit);

        _audioTrack = new AudioTrack(
            new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Speech)!
                .Build()!,
            new AudioFormat.Builder()
                .SetSampleRate(_settings.SampleRate)!
                .SetChannelMask(ChannelOut.Mono)!
                .SetEncoding(Encoding.Pcm16bit)!
                .Build()!,
            bufferSize,
            AudioTrackMode.Stream,
            AudioManager.AudioSessionIdGenerate);

        _audioTrack.Play();
        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsPlaying) return Task.CompletedTask;
        _audioTrack?.Stop();
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (_audioTrack is null || !IsPlaying) return Task.CompletedTask;
        _audioTrack.Write(pcmData, 0, pcmData.Length);
        return Task.CompletedTask;
    }

    public void ClearBuffer()
    {
        _audioTrack?.Flush();
    }

    public void Dispose()
    {
        _audioTrack?.Stop();
        _audioTrack?.Release();
        _audioTrack = null;
    }
}
```

### 8.3 — Add Android RECORD_AUDIO permission

**File:** `src/BodyCam/Platforms/Android/AndroidManifest.xml`

```xml
<uses-permission android:name="android.permission.RECORD_AUDIO" />
```

### 8.4 — Add runtime permission request

Before starting audio capture on Android, request permission:

```csharp
var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
if (status != PermissionStatus.Granted)
{
    status = await Permissions.RequestAsync<Permissions.Microphone>();
    if (status != PermissionStatus.Granted)
        throw new PermissionException("Microphone permission denied.");
}
```

This can live in `AndroidAudioInputService.StartAsync` or be handled by the orchestrator before starting.

### 8.5 — Update DI registration

**File:** `src/BodyCam/MauiProgram.cs`

```csharp
#if WINDOWS
builder.Services.AddSingleton<IAudioInputService, BodyCam.Platforms.Windows.WindowsAudioInputService>();
builder.Services.AddSingleton<IAudioOutputService, BodyCam.Platforms.Windows.WindowsAudioOutputService>();
#elif ANDROID
builder.Services.AddSingleton<IAudioInputService, BodyCam.Platforms.Android.AndroidAudioInputService>();
builder.Services.AddSingleton<IAudioOutputService, BodyCam.Platforms.Android.AndroidAudioOutputService>();
#else
builder.Services.AddSingleton<IAudioInputService, AudioInputService>();
builder.Services.AddSingleton<IAudioOutputService, AudioOutputService>();
#endif
```

---

## Verification

- [ ] Build succeeds for `net10.0-android`
- [ ] Build still succeeds for `net10.0-windows10.0.19041.0`
- [ ] Manual test on Android device/emulator: mic capture works
- [ ] Manual test on Android: playback works
- [ ] `VoiceCommunication` audio source activates AEC
- [ ] Permission request shown and handled correctly
- [ ] All unit tests still pass (they don't reference platform code)

---

## Files Changed

| File | Action |
|------|--------|
| `Platforms/Android/AndroidAudioInputService.cs` | NEW |
| `Platforms/Android/AndroidAudioOutputService.cs` | NEW |
| `Platforms/Android/AndroidManifest.xml` | MODIFY — add RECORD_AUDIO permission |
| `MauiProgram.cs` | MODIFY — add Android DI registrations |
