# M5 Phase 1 — Porcupine Engine Integration

Add the Porcupine wake word engine to BodyCam. Implement `PorcupineWakeWordService`,
generate `.ppn` keyword files, add AccessKey management. Windows-only first.

**Depends on:** M1 (audio pipeline — PCM capture), M5 infrastructure (all done).

---

## Current State

All abstractions exist and are tested:

| Component | Status |
|-----------|--------|
| `IWakeWordService` | ✅ `StartAsync`, `StopAsync`, `IsListening`, `WakeWordDetected`, `RegisterKeywords` |
| `WakeWordEntry` | ✅ `KeywordPath`, `Label`, `Sensitivity`, `Action`, `ToolName` |
| `WakeWordDetectedEventArgs` | ✅ `Action`, `Keyword`, `ToolName` |
| `NullWakeWordService` | ✅ No-op stub, currently registered in DI |
| `ToolDispatcher.BuildWakeWordEntries()` | ✅ Builds entries from tool `WakeWordBinding` declarations |
| `AgentOrchestrator.OnWakeWordDetected()` | ✅ 3-case handler (StartSession, GoToSleep, InvokeTool) |
| `IMicrophoneCoordinator` | ✅ Sequential handoff between wake word and Realtime API |

The only missing piece is a **real implementation** of `IWakeWordService`.

---

## Wave 1: Porcupine NuGet + AccessKey

### 1.1 — Add Porcupine Package

```xml
<!-- BodyCam.csproj -->
<PackageReference Include="Porcupine" Version="4.*" />
```

### 1.2 — AccessKey Storage

Add `PicovoiceAccessKey` to the existing `IApiKeyService` / settings infrastructure.
The AccessKey is NOT a network API key — it gates local SDK initialization only.

```csharp
// Settings/AppSettings.cs (extend existing)
public string PicovoiceAccessKey { get; set; } = string.Empty;
```

Settings page gets a new `SecureEntry` field for the key.

### 1.3 — Unit Test: AccessKey Validation

Verify that `PorcupineWakeWordService` throws a clear error when no AccessKey
is configured (don't let it crash deep in native code).

---

## Wave 2: Audio Frame Adapter

Porcupine requires a specific audio format that differs from BodyCam's pipeline:

| Parameter | BodyCam Pipeline | Porcupine Requirement |
|-----------|-----------------|----------------------|
| Sample rate | 24,000 Hz | 16,000 Hz |
| Bit depth | 16-bit | 16-bit |
| Channels | Mono | Mono |
| Frame size | 100ms chunks | `Porcupine.FrameLength` samples (512 @ 16kHz = 32ms) |

### 2.1 — PorcupineAudioAdapter

Resamples 24kHz → 16kHz and buffers into exact `FrameLength` frames:

```csharp
// Services/WakeWord/PorcupineAudioAdapter.cs
public class PorcupineAudioAdapter
{
    private readonly int _frameLength;
    private readonly short[] _frameBuffer;
    private int _bufferOffset;

    public PorcupineAudioAdapter(int frameLength)
    {
        _frameLength = frameLength;
        _frameBuffer = new short[frameLength];
    }

    /// <summary>
    /// Feed PCM16 24kHz audio. Returns complete 16kHz frames for Porcupine.
    /// </summary>
    public IEnumerable<short[]> Process(ReadOnlySpan<byte> pcm24kHz)
    {
        // 1. Resample 24kHz → 16kHz (simple 3:2 decimation)
        // 2. Buffer into FrameLength-sized chunks
        // 3. Yield complete frames
    }
}
```

### 2.2 — Unit Tests: Audio Adapter

- Correct resampling ratio (24kHz → 16kHz)
- Output frames are exactly `FrameLength` samples
- Partial frames buffered across calls
- Empty input returns no frames

---

## Wave 3: PorcupineWakeWordService

### 3.1 — Implementation

```csharp
// Services/WakeWord/PorcupineWakeWordService.cs
public class PorcupineWakeWordService : IWakeWordService, IDisposable
{
    private readonly IApiKeyService _apiKeys;
    private readonly IAudioInputService _audioInput;
    private Pv.Porcupine? _porcupine;
    private PorcupineAudioAdapter? _adapter;
    private List<WakeWordEntry> _entries = [];

    public bool IsListening { get; private set; }
    public event EventHandler<WakeWordDetectedEventArgs>? WakeWordDetected;

    public void RegisterKeywords(IEnumerable<WakeWordEntry> entries)
    {
        _entries = entries.ToList();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var accessKey = _apiKeys.GetPicovoiceAccessKey();
        if (string.IsNullOrWhiteSpace(accessKey))
            throw new InvalidOperationException("Picovoice AccessKey not configured.");

        var keywordPaths = _entries.Select(e => e.KeywordPath).ToList();
        var sensitivities = _entries.Select(e => e.Sensitivity).ToList();

        _porcupine = Pv.Porcupine.FromKeywordPaths(
            accessKey, keywordPaths, sensitivities: sensitivities);

        _adapter = new PorcupineAudioAdapter(_porcupine.FrameLength);

        // Subscribe to audio input (mic is managed by MicrophoneCoordinator)
        _audioInput.AudioChunkAvailable += OnAudioChunk;
        IsListening = true;
    }

    public Task StopAsync()
    {
        _audioInput.AudioChunkAvailable -= OnAudioChunk;
        _porcupine?.Dispose();
        _porcupine = null;
        IsListening = false;
        return Task.CompletedTask;
    }

    private void OnAudioChunk(object? sender, AudioChunkEventArgs e)
    {
        if (_porcupine is null || _adapter is null) return;

        foreach (var frame in _adapter.Process(e.Chunk))
        {
            int keywordIndex = _porcupine.Process(frame);
            if (keywordIndex >= 0 && keywordIndex < _entries.Count)
            {
                var entry = _entries[keywordIndex];
                WakeWordDetected?.Invoke(this, new WakeWordDetectedEventArgs
                {
                    Action = entry.Action,
                    Keyword = entry.Label,
                    ToolName = entry.ToolName,
                });
            }
        }
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
```

### 3.2 — Unit Tests

- `StartAsync` throws when AccessKey is empty
- `RegisterKeywords` stores entries
- `StopAsync` disposes Porcupine handle and unsubscribes from audio
- `IsListening` state transitions

---

## Wave 4: `.ppn` Keyword Files

### 4.1 — Generate Keywords

Log in to [console.picovoice.ai](https://console.picovoice.ai) and generate
`.ppn` files for each keyword:

| Keyword | File (Windows) | Action |
|---------|----------------|--------|
| "Hey BodyCam" | `hey-bodycam_en_windows.ppn` | StartSession |
| "Go to sleep" | `go-to-sleep_en_windows.ppn` | GoToSleep |
| "bodycam-look" | `bodycam-look_en_windows.ppn` | InvokeTool → `describe_scene` |
| "bodycam-read" | `bodycam-read_en_windows.ppn` | InvokeTool → `read_text` |
| "bodycam-find" | `bodycam-find_en_windows.ppn` | InvokeTool → `find_object` |
| "bodycam-remember" | `bodycam-remember_en_windows.ppn` | InvokeTool → `save_memory` |
| "bodycam-translate" | `bodycam-translate_en_windows.ppn` | InvokeTool → `set_translation_mode` |
| "bodycam-call" | `bodycam-call_en_windows.ppn` | InvokeTool → `make_phone_call` |
| "bodycam-navigate" | `bodycam-navigate_en_windows.ppn` | InvokeTool → `navigate_to` |

### 4.2 — Embed as Resources

```xml
<!-- BodyCam.csproj -->
<ItemGroup>
  <MauiAsset Include="Resources\WakeWords\*.ppn" />
</ItemGroup>
```

### 4.3 — Platform Keyword Path Helper

```csharp
// Services/WakeWord/KeywordPathResolver.cs
public static class KeywordPathResolver
{
    public static string Resolve(string baseName)
    {
        // Returns platform-specific path:
        // Windows: "hey-bodycam_en_windows.ppn"
        // Android: "hey-bodycam_en_android.ppn"
        // iOS:     "hey-bodycam_en_ios.ppn"
        var platform = DeviceInfo.Platform == DevicePlatform.Android ? "android"
                     : DeviceInfo.Platform == DevicePlatform.iOS ? "ios"
                     : "windows";
        return $"{baseName}_en_{platform}.ppn";
    }
}
```

---

## Wave 5: DI Registration + Build Verification

### 5.1 — Swap DI Registration

```csharp
// MauiProgram.cs
// Before:
builder.Services.AddSingleton<IWakeWordService, NullWakeWordService>();

// After:
builder.Services.AddSingleton<IWakeWordService, PorcupineWakeWordService>();
```

### 5.2 — Startup Wiring

In `MainViewModel` or app startup:

```csharp
var dispatcher = services.GetRequiredService<ToolDispatcher>();
var wakeWordService = services.GetRequiredService<IWakeWordService>();

// Build keyword list from registered tools
var entries = dispatcher.BuildWakeWordEntries();
wakeWordService.RegisterKeywords(entries);
```

### 5.3 — Build + Test

- `dotnet build` — verify Porcupine NuGet resolves and compiles
- `dotnet test` — all 229 existing tests still pass
- Manual test — speak "Hey BodyCam" and verify `WakeWordDetected` fires

---

## Exit Criteria

- [ ] Porcupine NuGet package added and compiles
- [ ] `PorcupineWakeWordService` implements `IWakeWordService`
- [ ] Audio adapter resamples 24kHz → 16kHz and buffers to `FrameLength`
- [ ] All 9 `.ppn` keyword files generated and embedded
- [ ] AccessKey stored in settings, error on missing key
- [ ] DI swapped from `NullWakeWordService` to `PorcupineWakeWordService`
- [ ] "Hey BodyCam" triggers `StartSession` on Windows
- [ ] All 7 tool wake words trigger `InvokeTool` with correct `ToolName`
- [ ] Existing tests unaffected
