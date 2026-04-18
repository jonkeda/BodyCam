# M13 Phase 1 — Audio Output Abstraction & Platform Providers

**Status:** NOT STARTED  
**Prerequisite:** M11 Phase 1 (Camera Abstraction) — completed, established the provider pattern  
**Goal:** Extract the tightly-coupled platform audio output implementations into an
`IAudioOutputProvider` → `AudioOutputManager` → `IAudioOutputService` pipeline.
`AudioOutputManager` implements `IAudioOutputService` for backward compatibility —
VoiceOutputAgent works without code changes.

---

## Current State (What Exists)

| Component | Location | Problem |
|-----------|----------|---------|
| `IAudioOutputService` | `Services/IAudioOutputService.cs` | No device selection, no availability, no disconnect detection |
| `WindowsAudioOutputService` | `Platforms/Windows/WindowsAudioOutputService.cs` | NAudio WaveOutEvent, hardcoded to default speaker |
| `AndroidAudioOutputService` | `Platforms/Android/AndroidAudioOutputService.cs` | AudioTrack, hardcoded to default output |
| `AudioOutputService` (stub) | `Services/AudioOutputService.cs` | Silent no-op for unsupported platforms |
| `VoiceOutputAgent` | `Agents/VoiceOutputAgent.cs` | Consumes `IAudioOutputService` — calls `PlayChunkAsync`, `ClearBuffer`, `StartAsync`/`StopAsync` |
| DI registration | `MauiProgram.cs` | `#if WINDOWS` / `#elif ANDROID` / `#else` → `IAudioOutputService` |

**Key constraint:** `VoiceOutputAgent` must work without code changes. `AudioOutputManager`
implements `IAudioOutputService` to achieve this.

**Audio format:** 16-bit signed PCM, mono, sample rate from `AppSettings` (default 24kHz).

**Key difference from M12 (input):** Output providers receive `sampleRate` as a parameter
to `StartAsync` rather than reading `AppSettings` directly. This makes providers testable
and allows the manager to control the rate when switching providers.

---

## Deliverables

### New Files

| File | Purpose |
|------|---------|
| `Services/Audio/IAudioOutputProvider.cs` | Interface — all audio output destinations implement this |
| `Services/Audio/AudioOutputManager.cs` | Manages active provider, implements `IAudioOutputService` |
| `Platforms/Windows/WindowsSpeakerProvider.cs` | Wraps NAudio WaveOutEvent (from WindowsAudioOutputService) |
| `Platforms/Android/PhoneSpeakerProvider.cs` | Wraps AudioTrack (from AndroidAudioOutputService) |

### Modified Files

| File | Change |
|------|--------|
| `MauiProgram.cs` | Replace `IAudioOutputService` registrations with providers + `AudioOutputManager` |
| `Services/ISettingsService.cs` | Add `ActiveAudioOutputProvider` property |
| `Services/SettingsService.cs` | Add `ActiveAudioOutputProvider` implementation |
| `ViewModels/SettingsViewModel.cs` | Add audio output picker properties |
| `SettingsPage.xaml` | Add audio output device picker UI |

### Unchanged Files (backward compatible)

| File | Why Unchanged |
|------|---------------|
| `Agents/VoiceOutputAgent.cs` | Consumes `IAudioOutputService` — `AudioOutputManager` implements it |
| `Orchestration/AgentOrchestrator.cs` | Uses `VoiceOutputAgent` which uses `IAudioOutputService` |
| `Services/MicrophoneCoordinator.cs` | Audio output unrelated to mic coordination |

---

## Implementation Waves

### Wave 1: Interface + Providers (no integration yet)

Create new files without modifying any existing code. Compile to verify.

**1.1 — Create `IAudioOutputProvider` interface**

```csharp
// Services/Audio/IAudioOutputProvider.cs
namespace BodyCam.Services.Audio;

public interface IAudioOutputProvider : IAsyncDisposable
{
    string DisplayName { get; }
    string ProviderId { get; }
    bool IsAvailable { get; }
    bool IsPlaying { get; }

    Task StartAsync(int sampleRate, CancellationToken ct = default);
    Task StopAsync();
    Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default);
    void ClearBuffer();

    event EventHandler? Disconnected;
}
```

Differences from `IAudioInputProvider`:
- `StartAsync` takes `sampleRate` parameter (input reads from `AppSettings`)
- `PlayChunkAsync` instead of `AudioChunkAvailable` event (push vs pull)
- `ClearBuffer()` for interruption handling (no equivalent in input)
- No `IsCapturing` — uses `IsPlaying` (semantic difference)

**1.2 — Create Windows `WindowsSpeakerProvider`**

Extract logic from `WindowsAudioOutputService`. Place in `Platforms/Windows/WindowsSpeakerProvider.cs`.

Key differences from `WindowsAudioOutputService`:
- Implements `IAudioOutputProvider` (not `IAudioOutputService`)
- `StartAsync(int sampleRate, ...)` takes sample rate as parameter, not from `AppSettings`
- Parameterless constructor (no `AppSettings` injection)
- Adds `DisplayName`, `ProviderId`, `IsAvailable`, `Disconnected` event
- Same NAudio `WaveOutEvent` + `BufferedWaveProvider` internals
- 30s buffer, 200ms latency, back-pressure polling loop

```csharp
// Platforms/Windows/WindowsSpeakerProvider.cs
namespace BodyCam.Services.Audio;

public sealed class WindowsSpeakerProvider : IAudioOutputProvider, IDisposable
{
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _buffer;

    public string DisplayName => "Laptop Speaker";
    public string ProviderId => "windows-speaker";
    public bool IsAvailable => true;
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;
        var waveFormat = new WaveFormat(sampleRate, 16, 1);
        _buffer = new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(30),
            DiscardOnBufferOverflow = false
        };
        _waveOut = new WaveOutEvent { DesiredLatency = 200 };
        _waveOut.Init(_buffer);
        _waveOut.Play();
        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync() { ... } // Stop, ClearBuffer, IsPlaying = false
    public async Task PlayChunkAsync(byte[] pcmData, CancellationToken ct) { ... } // Back-pressure loop
    public void ClearBuffer() => _buffer?.ClearBuffer();
}
```

**1.3 — Create Android `PhoneSpeakerProvider`**

Extract logic from `AndroidAudioOutputService`. Place in `Platforms/Android/PhoneSpeakerProvider.cs`.

Key differences from `AndroidAudioOutputService`:
- Implements `IAudioOutputProvider` (not `IAudioOutputService`)
- `StartAsync(int sampleRate, ...)` takes sample rate as parameter
- Parameterless constructor
- Same `AudioTrack` with Media/Speech audio attributes

```csharp
// Platforms/Android/PhoneSpeakerProvider.cs
namespace BodyCam.Services.Audio;

public sealed class PhoneSpeakerProvider : IAudioOutputProvider, IDisposable
{
    private AudioTrack? _audioTrack;

    public string DisplayName => "Phone Speaker";
    public string ProviderId => "phone-speaker";
    public bool IsAvailable => true;
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    // Same AudioTrack logic as AndroidAudioOutputService
    // with sampleRate from parameter instead of AppSettings
}
```

**1.4 — Create `AudioOutputManager`**

```csharp
// Services/Audio/AudioOutputManager.cs
namespace BodyCam.Services.Audio;

public sealed class AudioOutputManager : IAudioOutputService
{
    private readonly List<IAudioOutputProvider> _providers;
    private readonly ISettingsService _settings;
    private readonly AppSettings _appSettings;
    private IAudioOutputProvider? _active;

    // IAudioOutputService implementation:
    public bool IsPlaying => _active?.IsPlaying ?? false;

    public Task StartAsync(CancellationToken ct = default)
    {
        // If no active, fallback to default
        // _active.StartAsync(_appSettings.SampleRate, ct)
    }

    public Task StopAsync() => _active?.StopAsync() ?? Task.CompletedTask;

    public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
        => _active?.PlayChunkAsync(pcmData, ct) ?? Task.CompletedTask;

    public void ClearBuffer() => _active?.ClearBuffer();

    // Manager methods:
    public IReadOnlyList<IAudioOutputProvider> Providers => _providers;
    public IAudioOutputProvider? Active => _active;
    public Task SetActiveAsync(string providerId, CancellationToken ct = default) { ... }
    public Task InitializeAsync(CancellationToken ct = default) { ... }
}
```

The manager reads `_appSettings.SampleRate` to pass to `IAudioOutputProvider.StartAsync`.
This bridges the gap between the old world (sample rate in `AppSettings`) and the new
world (sample rate as parameter).

**Verify:** All four files compile. No existing code changed yet.

---

### Wave 2: Wire into DI

**2.1 — Update `MauiProgram.cs`**

Replace the platform-specific `IAudioOutputService` registrations:

```csharp
// BEFORE (current)
#if WINDOWS
builder.Services.AddSingleton<IAudioOutputService, WindowsAudioOutputService>();
#elif ANDROID
builder.Services.AddSingleton<IAudioOutputService, AndroidAudioOutputService>();
#else
builder.Services.AddSingleton<IAudioOutputService, AudioOutputService>();
#endif

// AFTER
#if WINDOWS
builder.Services.AddSingleton<IAudioOutputProvider, WindowsSpeakerProvider>();
#elif ANDROID
builder.Services.AddSingleton<IAudioOutputProvider, PhoneSpeakerProvider>();
#endif
builder.Services.AddSingleton<AudioOutputManager>();
builder.Services.AddSingleton<IAudioOutputService>(sp => sp.GetRequiredService<AudioOutputManager>());
```

Key: `AudioOutputManager` is registered as both itself AND as `IAudioOutputService`.
`VoiceOutputAgent` resolves `IAudioOutputService` → gets `AudioOutputManager`.
Settings UI resolves `AudioOutputManager` directly.

Note: Platform-specific providers still use `#if WINDOWS` / `#elif ANDROID` because
the implementations reference platform-specific APIs (NAudio, Android.Media). No
stub provider needed — the manager handles the case of zero providers gracefully.

**2.2 — Add `using BodyCam.Services.Audio;`** to `MauiProgram.cs`.

**Verify:** App starts. `VoiceOutputAgent` receives `AudioOutputManager` as its
`IAudioOutputService`. Audio output pipeline works — AI speech plays through
the active provider.

---

### Wave 3: Settings + Audio Output Picker UI

**3.1 — Add `ActiveAudioOutputProvider` to `ISettingsService`**

```csharp
// Audio Output
string? ActiveAudioOutputProvider { get; set; }
```

**3.2 — Add implementation to `SettingsService`**

```csharp
public string? ActiveAudioOutputProvider
{
    get { var v = Preferences.Get(nameof(ActiveAudioOutputProvider), string.Empty); return v.Length == 0 ? null : v; }
    set => Preferences.Set(nameof(ActiveAudioOutputProvider), value ?? string.Empty);
}
```

**3.3 — Wire settings into `AudioOutputManager`**

`SetActiveAsync` persists `_settings.ActiveAudioOutputProvider = providerId`.
`InitializeAsync` restores from `_settings.ActiveAudioOutputProvider ?? fallback`.

**3.4 — Add audio output picker to `SettingsViewModel`**

```csharp
// --- Audio Output ---
private readonly AudioOutputManager _audioOutputManager;

public IReadOnlyList<IAudioOutputProvider> AudioOutputProviders
    => _audioOutputManager.Providers;

public IAudioOutputProvider? SelectedAudioOutputProvider
{
    get => _audioOutputManager.Active;
    set
    {
        if (value is not null && value != _audioOutputManager.Active)
        {
            _ = _audioOutputManager.SetActiveAsync(value.ProviderId);
            OnPropertyChanged();
        }
    }
}
```

Inject `AudioOutputManager` in `SettingsViewModel` constructor.

**3.5 — Add picker XAML to `SettingsPage.xaml`**

Add after the Audio Input section (before Debug):

```xml
<!-- SECTION: Audio Output -->
<Label Text="Audio Output" FontSize="18" FontAttributes="Bold" Margin="0,8,0,0" />
<Label Text="Speaker" FontSize="13" TextColor="Gray" />
<Picker AutomationId="AudioOutputPicker"
        ItemsSource="{Binding AudioOutputProviders}"
        ItemDisplayBinding="{Binding DisplayName}"
        SelectedItem="{Binding SelectedAudioOutputProvider}" />
```

**Verify:** Settings page shows picker with "Laptop Speaker" (Windows) or
"Phone Speaker" (Android). Selection persists across restarts.

---

### Wave 4: Initialize on App Start

**4.1 — Call `AudioOutputManager.InitializeAsync()` on app startup**

In `MainPage.xaml.cs`, add `AudioOutputManager` to the constructor and call
`InitializeAsync()` alongside the existing `AudioInputManager.InitializeAsync()`:

```csharp
public MainPage(
    MainViewModel viewModel,
    PhoneCameraProvider phoneCamera,
    AudioInputManager audioInputManager,
    AudioOutputManager audioOutputManager)
{
    InitializeComponent();
    BindingContext = viewModel;
    phoneCamera.SetCameraView(CameraPreview);

    Loaded += async (_, _) =>
    {
        await audioInputManager.InitializeAsync();
        await audioOutputManager.InitializeAsync();
    };
}
```

This restores the last-used audio output provider from settings (or defaults to
the platform speaker).

**Verify:** App start correctly initializes the audio output manager. AI voice
responses play through the selected speaker. Interruption handling (ClearBuffer)
works.

---

## Backward Compatibility Proof

### VoiceOutputAgent Flow (before → after)

```
BEFORE:
  VoiceOutputAgent(IAudioOutputService audioOutput)
    → audioOutput = WindowsAudioOutputService (singleton)
    → audioOutput.StartAsync() → starts WaveOutEvent with AppSettings.SampleRate
    → audioOutput.PlayChunkAsync(pcm) → adds to BufferedWaveProvider
    → audioOutput.ClearBuffer() → clears BufferedWaveProvider

AFTER:
  VoiceOutputAgent(IAudioOutputService audioOutput)
    → audioOutput = AudioOutputManager (singleton, registered as IAudioOutputService)
    → audioOutput.StartAsync()
      → AudioOutputManager reads AppSettings.SampleRate
      → _active.StartAsync(sampleRate) → WindowsSpeakerProvider → WaveOutEvent
    → audioOutput.PlayChunkAsync(pcm)
      → _active.PlayChunkAsync(pcm) → WindowsSpeakerProvider → BufferedWaveProvider
    → audioOutput.ClearBuffer()
      → _active.ClearBuffer() → WindowsSpeakerProvider → _buffer.ClearBuffer()
```

### Interruption Flow (unchanged)

```
User speaks → OnSpeechStarted (orchestrator)
  → _voiceOut.HandleInterruption()
    → _audioOutput.ClearBuffer()          // IAudioOutputService call
      → AudioOutputManager.ClearBuffer()  // delegates to active provider
        → WindowsSpeakerProvider.ClearBuffer()
          → _buffer.ClearBuffer()         // same NAudio call as before
  → _voiceOut.ResetTracker()
  → _realtime.TruncateResponseAudioAsync(itemId, playedMs)
```

### AgentOrchestrator (unchanged)

The orchestrator interacts with audio output only through `VoiceOutputAgent`:
- `_voiceOut.StartAsync()` / `_voiceOut.StopAsync()` — lifecycle
- `_voiceOut.PlayAudioDeltaAsync(pcmData)` — playback
- `_voiceOut.HandleInterruption()` — interruption
- `_voiceOut.Tracker` — byte position tracking

All of these go through `IAudioOutputService` → `AudioOutputManager` → active provider.
No orchestrator changes needed.

---

## Old Files Disposition

| File | Action |
|------|--------|
| `WindowsAudioOutputService.cs` | Keep but no longer registered in DI. Can delete later. |
| `AndroidAudioOutputService.cs` | Keep but no longer registered in DI. Can delete later. |
| `AudioOutputService.cs` (stub) | Keep but no longer registered in DI. Can delete later. |

These files are not deleted in Phase 1 to minimize risk. They become dead code once
the new providers are wired up.

---

## Test Plan

### Unit Tests (BodyCam.Tests)

| Test | Validates |
|------|-----------|
| `AudioOutputManager_DefaultsToWindowsSpeaker` | `InitializeAsync()` selects "windows-speaker" when no saved pref (Windows) |
| `AudioOutputManager_DefaultsToPhoneSpeaker` | `InitializeAsync()` selects "phone-speaker" when no saved pref (Android) |
| `AudioOutputManager_RestoresSavedProvider` | Reads `ActiveAudioOutputProvider` from settings |
| `AudioOutputManager_FallbackOnDisconnect` | When active provider fires `Disconnected`, switches to platform default |
| `AudioOutputManager_StopsOldProviderOnSwitch` | `SetActiveAsync` stops previous before starting new |
| `AudioOutputManager_DelegatesToActive` | `PlayChunkAsync` / `ClearBuffer` delegate to active provider |
| `AudioOutputManager_ImplementsIAudioOutputService` | `StartAsync`/`StopAsync`/`IsPlaying`/`PlayChunkAsync`/`ClearBuffer` delegate correctly |
| `AudioOutputManager_StartAsync_FallsBackIfNoActive` | Starts platform speaker if no provider is active |
| `AudioOutputManager_ClearBuffer_NoOpWhenNoActive` | `ClearBuffer` doesn't throw when no active provider |

### Integration Tests (manual)

| Scenario | Expected |
|----------|----------|
| Start Realtime session → AI responds | Audio plays through default speaker |
| Interrupt AI mid-speech | Audio stops immediately (ClearBuffer works) |
| Settings → Audio Output picker shows "Laptop Speaker" | Single option, selected |
| Start session → stop session → start again | Audio pipeline restarts cleanly |
| Settings → change audio output → start session | New provider used |

### Regression

All existing voice features must continue to work:
- AI voice responses play correctly
- Interruption handling (ClearBuffer) works
- Start/stop session lifecycle
- Audio doesn't cut out or stutter

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| `AudioOutputManager` DI resolution fails | Test `IAudioOutputService` resolves to `AudioOutputManager` instance |
| Double registration — old and new both registered | Remove old `#if WINDOWS` block entirely, not comment out |
| Sample rate mismatch | Manager reads `AppSettings.SampleRate` and passes to `StartAsync(sampleRate)` — same value path as before |
| Back-pressure regression on Windows | `WindowsSpeakerProvider` uses identical polling loop from `WindowsAudioOutputService` |
| `ClearBuffer` latency on provider switch | `ClearBuffer` is a no-op if no active provider — safe during transitions |
| Old test helpers construct agents without new params | Test files may need `AudioOutputManager` added to constructor calls (same pattern as M11/M12 fixes) |

---

## Exit Criteria

- [ ] `IAudioOutputProvider` interface created
- [ ] `WindowsSpeakerProvider` wraps NAudio WaveOutEvent with back-pressure
- [ ] `PhoneSpeakerProvider` wraps AudioTrack with Media/Speech attributes
- [ ] `AudioOutputManager` implements `IAudioOutputService` for backward compatibility
- [ ] `AudioOutputManager` manages active provider with platform default fallback
- [ ] `VoiceOutputAgent` works without code changes
- [ ] Interruption handling (ClearBuffer → stop playback) works through the abstraction
- [ ] Settings page has audio output picker (single "Laptop/Phone Speaker" entry for now)
- [ ] `ActiveAudioOutputProvider` persisted in settings
- [ ] All existing voice output features pass regression
- [ ] Build succeeds on both Windows and Android targets
- [ ] 229+ existing tests pass
