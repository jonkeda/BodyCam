# M1 Implementation — Step 4: Windows Audio Output (NAudio Speaker Playback)

**Depends on:** Step 3 (NAudio package added)
**Produces:** Working `WindowsAudioOutputService` using NAudio `WaveOutEvent` + `BufferedWaveProvider`

---

## Why This Step?
Once we have audio coming back from OpenAI (Step 5), we need to play it. Building this now means we can test playback independently with synthetic audio before wiring to the Realtime API.

---

## Tasks

### 4.1 — Implement `WindowsAudioOutputService`

**File:** `src/BodyCam/Platforms/Windows/WindowsAudioOutputService.cs`

```csharp
using BodyCam.Services;
using NAudio.Wave;

namespace BodyCam.Platforms.Windows;

public class WindowsAudioOutputService : IAudioOutputService, IDisposable
{
    private readonly AppSettings _settings;
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _buffer;

    public bool IsPlaying { get; private set; }

    public WindowsAudioOutputService(AppSettings settings)
    {
        _settings = settings;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        var waveFormat = new WaveFormat(_settings.SampleRate, 16, 1);
        _buffer = new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(5),
            DiscardOnBufferOverflow = true
        };

        _waveOut = new WaveOutEvent
        {
            DesiredLatency = 200
        };
        _waveOut.Init(_buffer);
        _waveOut.Play();

        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsPlaying) return Task.CompletedTask;

        _waveOut?.Stop();
        _buffer?.ClearBuffer();
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (_buffer is null || !IsPlaying) return Task.CompletedTask;
        _buffer.AddSamples(pcmData, 0, pcmData.Length);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        _buffer = null;
    }
}
```

### 4.2 — Update `IAudioOutputService` — add `ClearBuffer` method

The existing interface needs a method to clear the playback buffer for interruption handling:

**File:** `src/BodyCam/Services/IAudioOutputService.cs`

```csharp
namespace BodyCam.Services;

public interface IAudioOutputService
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default);
    void ClearBuffer();
    bool IsPlaying { get; }
}
```

Update `AudioOutputService` stub and `WindowsAudioOutputService` to implement `ClearBuffer`.

### 4.3 — Update DI registration

**File:** `src/BodyCam/MauiProgram.cs`

```csharp
#if WINDOWS
builder.Services.AddSingleton<IAudioOutputService, BodyCam.Platforms.Windows.WindowsAudioOutputService>();
#else
builder.Services.AddSingleton<IAudioOutputService, AudioOutputService>(); // stub
#endif
```

### 4.4 — Manual playback test

Create a simple integration test or manual test that:
1. Generates a 440Hz sine wave as PCM 24kHz 16-bit mono (1 second)
2. Feeds it through `WindowsAudioOutputService`
3. Verify: you hear a tone through speakers

Sine wave generator (test helper):
```csharp
static byte[] GenerateSineWave(int sampleRate, int durationMs, int frequency = 440)
{
    int sampleCount = sampleRate * durationMs / 1000;
    var buffer = new byte[sampleCount * 2]; // 16-bit = 2 bytes per sample
    for (int i = 0; i < sampleCount; i++)
    {
        short sample = (short)(short.MaxValue * 0.5 * Math.Sin(2 * Math.PI * frequency * i / sampleRate));
        buffer[i * 2] = (byte)(sample & 0xFF);
        buffer[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
    }
    return buffer;
}
```

---

## Verification

- [ ] Build succeeds on Windows and Android
- [ ] Manual test: hear 440Hz tone through speakers
- [ ] `ClearBuffer` stops audio immediately
- [ ] `StopAsync` → `Dispose` clean shutdown
- [ ] `DiscardOnBufferOverflow` prevents memory growth
- [ ] All existing tests pass

---

## Files Changed

| File | Action |
|------|--------|
| `Platforms/Windows/WindowsAudioOutputService.cs` | NEW |
| `Services/IAudioOutputService.cs` | MODIFY — add `ClearBuffer()` |
| `Services/AudioOutputService.cs` | MODIFY — add `ClearBuffer()` stub |
| `MauiProgram.cs` | MODIFY — platform-conditional DI |
