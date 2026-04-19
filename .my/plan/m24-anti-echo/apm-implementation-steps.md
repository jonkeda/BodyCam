# M24 — WebRTC APM (Option B): Implementation Steps

Implements **Option B** from `overview.md` — direct native WebRTC APM via P/Invoke, without taking a dependency on the SoundFlow engine.

## Key discovery

The `SoundFlow.Extensions.WebRtc.Apm` NuGet package (v1.4.0, MIT license) already ships prebuilt native binaries for **all our target platforms**:

| RID | Native lib | Size |
|-----|-----------|------|
| `win-x64` | `webrtc-apm.dll` | 4.3 MB |
| `win-x86` | `webrtc-apm.dll` | — |
| `android-arm64` | `libwebrtc-apm.so` | 1.5 MB |
| `android-x64` | `libwebrtc-apm.so` | 1.6 MB |
| `ios-arm64` | `libwebrtc-apm.dylib` | 1.1 MB |
| `osx-arm64` | `libwebrtc-apm.dylib` | 1.1 MB |
| `linux-x64` | `libwebrtc-apm.so` | 1.6 MB |

The package also contains `AudioProcessingModule.cs` (34KB) — a complete P/Invoke wrapper with the C# API surface. We can extract just the native binaries and the P/Invoke wrapper without taking a dependency on SoundFlow core.

## Strategy

1. Extract the native libs + P/Invoke wrapper from SoundFlow.Extensions.WebRtc.Apm
2. Vendor them into our project (no runtime NuGet dependency on SoundFlow)
3. Build an `AecProcessor` service that wraps the P/Invoke calls
4. Insert it into the audio pipeline between mic capture and Realtime API send
5. Feed playback audio as the AEC reference signal

---

## Step 1 — Extract native binaries and P/Invoke wrapper

### 1a. Get the native binaries

Download the NuGet package and extract the `runtimes/` folder:

```powershell
# Download the NuGet package (it's a zip)
Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/SoundFlow.Extensions.WebRtc.Apm/1.4.0" -OutFile "soundflow-apm.zip"
Expand-Archive -Path "soundflow-apm.zip" -DestinationPath "soundflow-apm"

# Copy only the runtimes we need
$rids = @("win-x64", "android-arm64", "android-x64", "ios-arm64")
foreach ($rid in $rids) {
    $src = "soundflow-apm/runtimes/$rid/native/*"
    $dst = "src/BodyCam/runtimes/$rid/native/"
    New-Item -ItemType Directory -Force -Path $dst
    Copy-Item $src $dst
}
```

### 1b. Get the P/Invoke wrapper

Extract `AudioProcessingModule.cs` from the package's `lib/` folder. This file contains:
- `[DllImport]` declarations for all native functions
- Config structs for AEC, NS, AGC settings
- `Create()`, `Destroy()`, `ProcessCaptureStream()`, `ProcessRenderStream()`, `SetConfig()` methods

Copy it to: `src/BodyCam/Services/Audio/Native/AudioProcessingModule.cs`

Rename the namespace from `SoundFlow.Extensions.WebRtc.Apm` to `BodyCam.Services.Audio.Native`.

### 1c. Add native lib references to .csproj

```xml
<!-- WebRTC APM native libraries -->
<ItemGroup>
  <None Include="runtimes\win-x64\native\webrtc-apm.dll"
        Pack="false" CopyToOutputDirectory="PreserveNewest"
        Link="runtimes\win-x64\native\webrtc-apm.dll" />
  <!-- Android native libs go via AndroidNativeLibrary -->
</ItemGroup>

<!-- Android: native .so files -->
<ItemGroup Condition="$(TargetFramework.Contains('android'))">
  <AndroidNativeLibrary Include="runtimes\android-arm64\native\libwebrtc-apm.so"
                        Abi="arm64-v8a" />
  <AndroidNativeLibrary Include="runtimes\android-x64\native\libwebrtc-apm.so"
                        Abi="x86_64" />
</ItemGroup>
```

### Acceptance criteria

- [ ] Native libs are in `src/BodyCam/runtimes/` for each target platform
- [ ] `AudioProcessingModule.cs` compiles with namespace changed to `BodyCam.Services.Audio.Native`
- [ ] `DllImport` resolves at runtime on Windows (quick smoke test)
- [ ] No dependency on `SoundFlow` or `SoundFlow.Extensions.WebRtc.Apm` NuGet packages

---

## Step 2 — Create `AecProcessor` service

**File**: `src/BodyCam/Services/Audio/AecProcessor.cs`

Wraps `AudioProcessingModule` to provide a simple AEC/NS/AGC interface for our audio pipeline.

### Interface

```csharp
namespace BodyCam.Services.Audio;

/// <summary>
/// Processes microphone audio through WebRTC APM for echo cancellation,
/// noise suppression, and automatic gain control.
/// </summary>
public sealed class AecProcessor : IDisposable
{
    private readonly AppSettings _settings;
    private IntPtr _apm;  // native handle
    private bool _initialized;

    // WebRTC APM only supports 8000, 16000, 32000, 48000 Hz
    private const int ApmSampleRate = 48000;

    public AecProcessor(AppSettings settings) { _settings = settings; }

    /// <summary>Initialize the native APM with AEC + NS + AGC enabled.</summary>
    public void Initialize();

    /// <summary>
    /// Process a capture (microphone) chunk. Returns echo-cancelled audio.
    /// Input/output is PCM16 mono at AppSettings.SampleRate (24kHz).
    /// Internally resamples 24k→48k, processes, then 48k→24k.
    /// </summary>
    public byte[] ProcessCapture(byte[] micChunk);

    /// <summary>
    /// Feed a render (speaker playback) chunk as the AEC reference signal.
    /// Must be called for every chunk played through the speaker.
    /// Input is PCM16 mono at AppSettings.SampleRate (24kHz).
    /// </summary>
    public void FeedRenderReference(byte[] playbackChunk);

    public void Dispose();
}
```

### Key implementation details

1. **Resampling**: Use existing `AudioResampler.Resample()` to convert 24kHz↔48kHz.

2. **Frame size**: WebRTC APM processes in 10ms frames. At 48kHz mono PCM16:
   - 10ms = 480 samples = 960 bytes per frame
   - Our 50ms chunks = 5 APM frames
   - Process in a loop: split chunk into 10ms frames, process each

3. **Configuration**:
   ```csharp
   // Create with AEC enabled, mobile mode for Android
   var config = new ApmConfig
   {
       AecEnabled = true,
       AecMobileMode = OperatingSystem.IsAndroid(),
       AecLatencyMs = 40,
       NsEnabled = true,
       NsLevel = NoiseSuppressionLevel.High,
       Agc1Enabled = true,
       AgcMode = GainControlMode.AdaptiveDigital,
       HpfEnabled = true
   };
   ```

4. **Thread safety**: `ProcessCapture` and `FeedRenderReference` may be called from different threads. Use a lock around native calls (the native APM is not thread-safe).

5. **Latency budget**: Resampling adds ~2-4ms. APM processing adds ~2ms. Total overhead: ~4-6ms per chunk, well within our 50ms chunk interval.

### Acceptance criteria

- [ ] `AecProcessor` initializes without errors on Windows and Android
- [ ] `ProcessCapture` returns audio of the same length as input
- [ ] `FeedRenderReference` accepts playback chunks without errors
- [ ] Echo is audibly reduced in a loopback test

---

## Step 3 — Wire AEC into the capture path

Modify how mic audio flows from `PlatformMicProvider` → `VoiceInputAgent` → `RealtimeClient`.

### Current flow

```
PlatformMicProvider.AudioChunkAvailable
  → AudioInputManager.OnProviderChunk
    → AudioInputManager.AudioChunkAvailable
      → VoiceInputAgent.OnAudioChunk
        → RealtimeClient.SendAudioChunkAsync
```

### New flow

Insert `AecProcessor.ProcessCapture` between the input manager and the realtime client:

**Option A — In VoiceInputAgent** (simplest, minimal changes):

**File**: `src/BodyCam/Agents/VoiceInputAgent.cs`

```csharp
public class VoiceInputAgent
{
    private readonly IAudioInputService _audioInput;
    private readonly IRealtimeClient _realtime;
    private readonly AecProcessor? _aec;  // NEW

    public VoiceInputAgent(IAudioInputService audioInput, IRealtimeClient realtime, AecProcessor? aec = null)
    {
        _audioInput = audioInput;
        _realtime = realtime;
        _aec = aec;
    }

    private async void OnAudioChunk(object? sender, byte[] chunk)
    {
        try
        {
            if (_realtime.IsConnected)
            {
                var processed = _aec is not null ? _aec.ProcessCapture(chunk) : chunk;
                await _realtime.SendAudioChunkAsync(processed);
            }
        }
        catch (Exception) { }
    }
}
```

### Why VoiceInputAgent is the right place

- It's the single point where mic audio meets the API
- `PlatformMicProvider` stays unchanged (still raw mic, useful for non-AEC scenarios)
- `AudioInputManager` stays unchanged (provider management is orthogonal to AEC)
- AEC is opt-in via DI (null check on `_aec`)

### Acceptance criteria

- [ ] `VoiceInputAgent` passes audio through `AecProcessor` when available
- [ ] `VoiceInputAgent` still works without `AecProcessor` (null case)
- [ ] No change to `IAudioInputProvider` or `AudioInputManager`

---

## Step 4 — Wire AEC reference signal from playback path

The AEC needs to know what's being played through the speaker. Feed every playback chunk to `AecProcessor.FeedRenderReference`.

### Current flow

```
RealtimeClient.AudioDelta
  → VoiceOutputAgent.PlayAudioDeltaAsync
    → AudioOutputManager.PlayChunkAsync
      → IAudioOutputProvider.PlayChunkAsync  (speaker)
```

### Change — In VoiceOutputAgent

**File**: `src/BodyCam/Agents/VoiceOutputAgent.cs`

```csharp
public class VoiceOutputAgent
{
    private readonly IAudioOutputService _audioOutput;
    private readonly AecProcessor? _aec;  // NEW
    private readonly AudioPlaybackTracker _tracker = new();

    public VoiceOutputAgent(IAudioOutputService audioOutput, AecProcessor? aec = null)
    {
        _audioOutput = audioOutput;
        _aec = aec;
    }

    public async Task PlayAudioDeltaAsync(byte[] pcmData, CancellationToken ct = default)
    {
        // Feed to AEC BEFORE playing — reference must arrive before/with the capture
        _aec?.FeedRenderReference(pcmData);
        await _audioOutput.PlayChunkAsync(pcmData, ct);
        _tracker.BytesPlayed += pcmData.Length;
    }
}
```

### Timing considerations

The render reference should be fed **before or at the same time** as the audio reaches the speaker. Feeding it in `PlayAudioDeltaAsync` before calling `PlayChunkAsync` is correct — the native APM buffers the reference internally and correlates it with the next capture frame.

The `aecLatencyMs` parameter (set to 40ms in Step 2) tells the APM how much delay to expect between the render reference and when it actually comes out of the speaker. This is device-dependent; 40ms is a reasonable default.

### Acceptance criteria

- [ ] Every playback chunk is fed to `AecProcessor.FeedRenderReference`
- [ ] AEC reference and capture streams stay in sync
- [ ] No audible artifacts in playback

---

## Step 5 — Register `AecProcessor` in DI

**File**: `src/BodyCam/ServiceExtensions.cs`

### Change to `AddAudioServices()`

```csharp
public static IServiceCollection AddAudioServices(this IServiceCollection services)
{
    // AEC processor (WebRTC APM)
    services.AddSingleton<AecProcessor>();

    // ... existing provider registrations unchanged ...
}
```

### Change to agent registration (wherever VoiceInputAgent/VoiceOutputAgent are registered)

Ensure `AecProcessor` is injected:

```csharp
services.AddSingleton<VoiceInputAgent>();   // DI resolves AecProcessor automatically
services.AddSingleton<VoiceOutputAgent>();
```

### Initialize AecProcessor

In `MainPage.xaml.cs` `Loaded` handler (or in the orchestrator that creates the session), call:

```csharp
var aec = services.GetRequiredService<AecProcessor>();
aec.Initialize();
```

Or make `Initialize` lazy — called on first `ProcessCapture`.

### Acceptance criteria

- [ ] `AecProcessor` is singleton in DI
- [ ] `VoiceInputAgent` and `VoiceOutputAgent` receive `AecProcessor` via constructor injection
- [ ] App starts without errors

---

## Step 6 — Handle Android native library loading

Android `.so` files need special handling in MAUI.

### AndroidNativeLibrary items (in .csproj)

```xml
<ItemGroup Condition="$(TargetFramework.Contains('android'))">
  <AndroidNativeLibrary Include="runtimes\android-arm64\native\libwebrtc-apm.so"
                        Abi="arm64-v8a" />
  <AndroidNativeLibrary Include="runtimes\android-x64\native\libwebrtc-apm.so"
                        Abi="x86_64" />
</ItemGroup>
```

### DllImport name

The `[DllImport]` should use just `"webrtc-apm"` (no `lib` prefix, no `.so` suffix). The .NET runtime resolves this to `libwebrtc-apm.so` on Linux/Android and `webrtc-apm.dll` on Windows.

If the SoundFlow wrapper uses a different name, update it:
```csharp
[DllImport("webrtc-apm", CallingConvention = CallingConvention.Cdecl)]
```

### iOS native framework

For iOS, the native lib may need to be packaged as a framework or static lib. Check the SoundFlow `.targets` file for how they handle iOS bundling:
- May need `<NativeReference>` instead of `<NativeLibrary>`
- May need to set `Kind="Dynamic"` or `Kind="Static"`

### Acceptance criteria

- [ ] `libwebrtc-apm.so` is included in the Android APK
- [ ] `DllImport` resolves correctly on Android arm64
- [ ] `webrtc-apm.dll` loads on Windows x64

---

## Step 7 — Add AEC toggle to settings

### AppSettings change

**File**: `src/BodyCam/AppSettings.cs`

```csharp
public bool AecEnabled { get; set; } = true;
```

### AecProcessor conditional processing

```csharp
public byte[] ProcessCapture(byte[] micChunk)
{
    if (!_settings.AecEnabled || _apm == IntPtr.Zero)
        return micChunk;  // passthrough

    // ... AEC processing ...
}
```

### Settings UI (optional, after core works)

Add a toggle switch in the Settings page for "Echo Cancellation (WebRTC)".

### Acceptance criteria

- [ ] AEC can be disabled via settings without restart
- [ ] Disabled AEC = zero overhead (passthrough)

---

## Step 8 — Integration testing

### Test plan

1. **Windows first** (easier to debug):
   - Build: `dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None`
   - Start conversation → speak → verify echo is reduced
   - Compare with `AecEnabled = false`

2. **Android**:
   - Build: `dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android -t:Install`
   - Same test on device
   - Check logcat for native lib load errors

3. **Latency measurement**:
   - Add timing logs around `ProcessCapture`
   - Verify < 10ms per chunk (50ms chunk at 24kHz)

4. **Reference signal sync**:
   - If echo is not fully cancelled, adjust `aecLatencyMs` (try 20, 40, 60, 80, 100)
   - Log the actual speaker-to-mic delay

### Known edge cases

- **No playback**: If no AI response is playing, AEC should still pass through mic audio cleanly (no reference signal = no cancellation needed, but shouldn't distort)
- **Interruption**: When user interrupts AI, `ClearBuffer()` is called. AEC should handle sudden stop of render reference
- **Bluetooth**: Higher latency (~100-200ms). May need to increase `aecLatencyMs` when Bluetooth output is active

### Acceptance criteria

- [ ] Echo noticeably reduced on Windows
- [ ] Echo noticeably reduced on Android
- [ ] No audio distortion or artifacts
- [ ] Latency overhead < 10ms per chunk
- [ ] Clean passthrough when no playback is happening

---

## Step 9 — Unit tests

**File**: `src/BodyCam.Tests/Services/AecProcessorTests.cs`

```csharp
public class AecProcessorTests
{
    [Fact]
    public void ProcessCapture_SameLength()
    {
        var settings = new AppSettings { SampleRate = 24000, ChunkDurationMs = 50 };
        using var aec = new AecProcessor(settings);
        aec.Initialize();

        var input = new byte[2400]; // 50ms at 24kHz mono PCM16
        var output = aec.ProcessCapture(input);
        output.Length.Should().Be(input.Length);
    }

    [Fact]
    public void ProcessCapture_DisabledPassthrough()
    {
        var settings = new AppSettings { SampleRate = 24000, AecEnabled = false };
        using var aec = new AecProcessor(settings);

        var input = new byte[] { 1, 2, 3, 4 };
        var output = aec.ProcessCapture(input);
        output.Should().BeEquivalentTo(input);
    }

    [Fact]
    public void FeedRenderReference_DoesNotThrow()
    {
        var settings = new AppSettings { SampleRate = 24000 };
        using var aec = new AecProcessor(settings);
        aec.Initialize();

        var chunk = new byte[2400];
        var act = () => aec.FeedRenderReference(chunk);
        act.Should().NotThrow();
    }
}
```

Note: Full echo cancellation quality can only be tested with real audio (manual testing). Unit tests verify the pipeline doesn't crash and maintains correct data shapes.

### Acceptance criteria

- [ ] Tests pass on Windows
- [ ] Tests verify data integrity (length, passthrough)
- [ ] No native crashes in test runner

---

## File summary

| # | File | Action | Step |
|---|------|--------|------|
| 1 | `runtimes/win-x64/native/webrtc-apm.dll` | **Vendor** (extract from NuGet) | 1 |
| 2 | `runtimes/android-arm64/native/libwebrtc-apm.so` | **Vendor** | 1 |
| 3 | `runtimes/android-x64/native/libwebrtc-apm.so` | **Vendor** | 1 |
| 4 | `runtimes/ios-arm64/native/libwebrtc-apm.dylib` | **Vendor** | 1 |
| 5 | `Services/Audio/Native/AudioProcessingModule.cs` | **Vendor + edit namespace** | 1 |
| 6 | `BodyCam.csproj` | **Edit** — add native lib items | 1 |
| 7 | `Services/Audio/AecProcessor.cs` | **Create** | 2 |
| 8 | `Agents/VoiceInputAgent.cs` | **Edit** — add AecProcessor | 3 |
| 9 | `Agents/VoiceOutputAgent.cs` | **Edit** — feed render reference | 4 |
| 10 | `ServiceExtensions.cs` | **Edit** — register AecProcessor | 5 |
| 11 | `AppSettings.cs` | **Edit** — add AecEnabled | 7 |

All files under `src/BodyCam/`.

---

## Execution order

```
Step 1 (extract + vendor)
  │
  ▼
Step 2 (AecProcessor)  ──→  Step 9 (unit tests, parallel)
  │
  ▼
Step 3 (wire capture) + Step 4 (wire render) — parallel
  │
  ▼
Step 5 (DI registration)
  │
  ▼
Step 6 (Android native lib handling)
  │
  ▼
Step 7 (settings toggle)
  │
  ▼
Step 8 (integration testing — Windows first, then Android)
```

## Comparison with WebView approach

| Aspect | WebRTC APM (this) | WebView approach |
|--------|-------------------|------------------|
| AEC quality | Same engine (AEC3) | Same engine (via browser) |
| Latency | ~4-6ms (resample + native) | ~10-20ms (JS bridge) |
| Memory | ~5MB (native lib) | ~30-50MB (WebView process) |
| Complexity | Medium (P/Invoke, resampling) | High (JS, platform bridges) |
| Reference signal | Explicit `FeedRenderReference` | Automatic (browser manages it) |
| Platform work | Just vendor native libs | Platform-specific JS bridges per OS |
| Failure mode | Clean — passthrough on error | Complex — WebView lifecycle issues |
| Debugging | C# stack traces | Split C#/JS debugging |

**Recommendation**: Option B (this doc) is simpler, lower latency, and lower memory. The only advantage of the WebView approach is automatic reference signal management — but explicit feeding is straightforward and gives more control.
