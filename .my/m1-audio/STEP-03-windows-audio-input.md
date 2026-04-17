# M1 Implementation — Step 3: Windows Audio Input (NAudio Mic Capture)

**Depends on:** Step 2 (IRealtimeClient interface exists, stubs compile)
**Produces:** Working `WindowsAudioInputService` using NAudio `WaveInEvent`

---

## Why This Step?
We need real mic audio to feed the Realtime API. This is the first platform-specific implementation. Windows is the primary dev platform, so it goes first.

---

## Tasks

### 3.1 — Add NAudio NuGet package

**File:** `src/BodyCam/BodyCam.csproj`

Add conditional package reference (Windows only):

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows'">
    <PackageReference Include="NAudio" Version="2.3.0" />
</ItemGroup>
```

### 3.2 — Implement `WindowsAudioInputService`

**File:** `src/BodyCam/Platforms/Windows/WindowsAudioInputService.cs`

```csharp
using BodyCam.Services;
using NAudio.Wave;

namespace BodyCam.Platforms.Windows;

public class WindowsAudioInputService : IAudioInputService, IDisposable
{
    private readonly AppSettings _settings;
    private WaveInEvent? _waveIn;

    public bool IsCapturing { get; private set; }
    public event EventHandler<byte[]>? AudioChunkAvailable;

    public WindowsAudioInputService(AppSettings settings)
    {
        _settings = settings;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return Task.CompletedTask;

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(_settings.SampleRate, 16, 1),
            BufferMilliseconds = _settings.ChunkDurationMs
        };

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;

        _waveIn.StartRecording();
        IsCapturing = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsCapturing) return Task.CompletedTask;

        _waveIn?.StopRecording();
        IsCapturing = false;
        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        // CRITICAL: always copy BytesRecorded, NOT Buffer.Length
        var chunk = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
        AudioChunkAvailable?.Invoke(this, chunk);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        IsCapturing = false;
    }

    public void Dispose()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;
    }
}
```

### 3.3 — Update DI registration (platform-conditional)

**File:** `src/BodyCam/MauiProgram.cs`

Replace the generic `AudioInputService` registration with platform-specific:

```csharp
#if WINDOWS
builder.Services.AddSingleton<IAudioInputService, BodyCam.Platforms.Windows.WindowsAudioInputService>();
#else
builder.Services.AddSingleton<IAudioInputService, AudioInputService>(); // stub for non-Windows
#endif
```

### 3.4 — Keep stub `AudioInputService` as fallback

The existing `AudioInputService.cs` stub remains for non-Windows platforms until Android impl in Step 8.

### 3.5 — Unit tests

Can't truly unit-test NAudio mic capture (hardware dependency), but we can test:
- `WindowsAudioInputService` constructor doesn't throw
- `StartAsync` / `StopAsync` state transitions
- `AudioChunkAvailable` event subscription works

Use a test double or just verify the public contract. Consider integration-level test that records 1 second of actual audio (manual/CI skip).

---

## Verification

- [ ] Build succeeds on Windows (net10.0-windows10.0.19041.0)
- [ ] Build succeeds on Android (net10.0-android) — NAudio excluded, stub used
- [ ] Manual test: Start capture → speak → verify `AudioChunkAvailable` fires with non-empty chunks
- [ ] `IsCapturing` correctly reflects state
- [ ] `StopAsync` → `Dispose` releases WaveIn without errors
- [ ] All existing tests still pass

---

## Files Changed

| File | Action |
|------|--------|
| `BodyCam.csproj` | MODIFY — add NAudio conditional PackageReference |
| `Platforms/Windows/WindowsAudioInputService.cs` | NEW |
| `MauiProgram.cs` | MODIFY — platform-conditional DI |
| `Services/AudioInputService.cs` | KEEP (stub fallback) |
