# M12 Phase 1 — Audio Input Abstraction & Platform Mic

**Status:** NOT STARTED  
**Prerequisite:** M11 Phase 1 (Camera Abstraction) — completed, established the provider pattern  
**Goal:** Extract the tightly-coupled platform audio implementations into an
`IAudioInputProvider` → `AudioInputManager` → `IAudioInputService` pipeline.
`AudioInputManager` implements `IAudioInputService` for backward compatibility —
VoiceInputAgent and MicrophoneCoordinator require zero changes.

---

## Current State (What Exists)

| Component | Location | Problem |
|-----------|----------|---------|
| `IAudioInputService` | `Services/IAudioInputService.cs` | No device selection, no availability, no disconnect detection |
| `WindowsAudioInputService` | `Platforms/Windows/WindowsAudioInputService.cs` | NAudio WaveInEvent, hardcoded to default mic |
| `AndroidAudioInputService` | `Platforms/Android/AndroidAudioInputService.cs` | AudioRecord, hardcoded to VoiceCommunication source |
| `AudioInputService` (stub) | `Services/AudioInputService.cs` | Silent fallback for unsupported platforms |
| `VoiceInputAgent` | `Agents/VoiceInputAgent.cs` | Consumes `IAudioInputService` — subscribes to `AudioChunkAvailable`, pipes to Realtime |
| `MicrophoneCoordinator` | `Services/MicrophoneCoordinator.cs` | Coordinates wake word ↔ session mic handoff (uses `IWakeWordService`, not `IAudioInputService` directly) |
| DI registration | `MauiProgram.cs` | `#if WINDOWS` / `#elif ANDROID` / `#else` → `IAudioInputService` |

**Key constraint:** `VoiceInputAgent` and `MicrophoneCoordinator` must work without
code changes. `AudioInputManager` implements `IAudioInputService` to achieve this.

**Audio format:** 16-bit signed PCM, mono, configurable sample rate (default 24kHz).
Chunk size = `SampleRate * 2 * ChunkDurationMs / 1000` bytes.

---

## Deliverables

### New Files

| File | Purpose |
|------|---------|
| `Services/Audio/IAudioInputProvider.cs` | Interface — all audio sources implement this |
| `Services/Audio/AudioInputManager.cs` | Manages active provider, implements `IAudioInputService` |
| `Platforms/Windows/PlatformMicProvider.cs` | Wraps NAudio WaveInEvent (from WindowsAudioInputService) |
| `Platforms/Android/PlatformMicProvider.cs` | Wraps AudioRecord (from AndroidAudioInputService) |

### Modified Files

| File | Change |
|------|--------|
| `MauiProgram.cs` | Replace `IAudioInputService` registrations with providers + `AudioInputManager` |
| `Services/ISettingsService.cs` | Add `ActiveAudioInputProvider` property |
| `Services/SettingsService.cs` | Add `ActiveAudioInputProvider` implementation |
| `ViewModels/SettingsViewModel.cs` | Add audio input picker properties |
| `SettingsPage.xaml` | Add audio input device picker UI |

### Unchanged Files (backward compatible)

| File | Why Unchanged |
|------|---------------|
| `Agents/VoiceInputAgent.cs` | Consumes `IAudioInputService` — `AudioInputManager` implements it |
| `Services/MicrophoneCoordinator.cs` | Uses `IWakeWordService`, not audio input directly |
| `Orchestration/AgentOrchestrator.cs` | Audio handled by `VoiceInputAgent`, not orchestrator |

---

## Implementation Waves

### Wave 1: Interface + Providers (no integration yet)

Create new files without modifying any existing code. Compile to verify.

**1.1 — Create `IAudioInputProvider` interface**

```csharp
// Services/Audio/IAudioInputProvider.cs
namespace BodyCam.Services.Audio;

public interface IAudioInputProvider : IAsyncDisposable
{
    string DisplayName { get; }
    string ProviderId { get; }
    bool IsAvailable { get; }
    bool IsCapturing { get; }

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();

    event EventHandler<byte[]>? AudioChunkAvailable;
    event EventHandler? Disconnected;
}
```

**1.2 — Create Windows `PlatformMicProvider`**

Extract logic from `WindowsAudioInputService`. Place in `Platforms/Windows/PlatformMicProvider.cs`.

Key differences from `WindowsAudioInputService`:
- Implements `IAudioInputProvider` (not `IAudioInputService`)
- Adds `DisplayName`, `ProviderId`, `IsAvailable`, `Disconnected` event
- Same NAudio `WaveInEvent` internals

```csharp
// Platforms/Windows/PlatformMicProvider.cs
namespace BodyCam.Services.Audio;

public sealed class PlatformMicProvider : IAudioInputProvider, IDisposable
{
    private readonly AppSettings _settings;
    private WaveInEvent? _waveIn;

    public string DisplayName => "System Microphone";
    public string ProviderId => "platform";
    public bool IsAvailable => true;
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

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

    public Task StopAsync() { ... } // StopRecording, set IsCapturing = false
}
```

**1.3 — Create Android `PlatformMicProvider`**

Extract logic from `AndroidAudioInputService`. Place in `Platforms/Android/PlatformMicProvider.cs`.

Key differences:
- Implements `IAudioInputProvider` (not `IAudioInputService`)
- Same `AudioRecord` + `RecordLoopAsync` internals
- Permission check in `StartAsync`

```csharp
// Platforms/Android/PlatformMicProvider.cs
namespace BodyCam.Services.Audio;

public sealed class PlatformMicProvider : IAudioInputProvider, IDisposable
{
    public string DisplayName => "Phone Microphone";
    public string ProviderId => "platform";
    public bool IsAvailable => true;
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    // Same AudioRecord logic as AndroidAudioInputService
}
```

**1.4 — Create `AudioInputManager`**

```csharp
// Services/Audio/AudioInputManager.cs
namespace BodyCam.Services.Audio;

public sealed class AudioInputManager : IAudioInputService, IAsyncDisposable
{
    private readonly IReadOnlyList<IAudioInputProvider> _providers;
    private readonly ISettingsService _settings;
    private IAudioInputProvider? _active;

    public event EventHandler<byte[]>? AudioChunkAvailable;

    // IAudioInputService implementation:
    public bool IsCapturing => _active?.IsCapturing ?? false;
    public Task StartAsync(CancellationToken ct) { /* start active provider */ }
    public Task StopAsync() { /* stop active provider */ }

    // Manager methods:
    public IReadOnlyList<IAudioInputProvider> Providers => _providers;
    public IAudioInputProvider? Active => _active;
    public Task SetActiveAsync(string providerId, CancellationToken ct) { ... }
    public Task InitializeAsync(CancellationToken ct) { ... }
}
```

The manager subscribes to the active provider's `AudioChunkAvailable` event and
re-emits it as its own `AudioChunkAvailable` — this is how `VoiceInputAgent`
receives audio without knowing about providers.

**Verify:** All four files compile. No existing code changed yet.

---

### Wave 2: Wire into DI

**2.1 — Update `MauiProgram.cs`**

Replace the platform-specific `IAudioInputService` registrations:

```csharp
// BEFORE (current)
#if WINDOWS
    builder.Services.AddSingleton<IAudioInputService, WindowsAudioInputService>();
#elif ANDROID
    builder.Services.AddSingleton<IAudioInputService, AndroidAudioInputService>();
#else
    builder.Services.AddSingleton<IAudioInputService, AudioInputService>();
#endif

// AFTER
builder.Services.AddSingleton<IAudioInputProvider, PlatformMicProvider>();
builder.Services.AddSingleton<AudioInputManager>();
builder.Services.AddSingleton<IAudioInputService>(sp => sp.GetRequiredService<AudioInputManager>());
```

Key: `AudioInputManager` is registered as both itself AND as `IAudioInputService`.
`VoiceInputAgent` resolves `IAudioInputService` → gets `AudioInputManager`.
Settings UI resolves `AudioInputManager` directly.

**2.2 — Add `using BodyCam.Services.Audio;`** to `MauiProgram.cs`.

**Verify:** App starts. `VoiceInputAgent` receives `AudioInputManager` as its
`IAudioInputService`. Audio pipeline works because `AudioInputManager.StartAsync()`
delegates to the active `PlatformMicProvider`.

---

### Wave 3: Settings + Audio Picker UI

**3.1 — Add `ActiveAudioInputProvider` to `ISettingsService`**

```csharp
// Camera
string? ActiveCameraProvider { get; set; }

// Audio Input
string? ActiveAudioInputProvider { get; set; }
```

**3.2 — Add implementation to `SettingsService`**

```csharp
public string? ActiveAudioInputProvider
{
    get { var v = Preferences.Get(nameof(ActiveAudioInputProvider), string.Empty); return v.Length == 0 ? null : v; }
    set => Preferences.Set(nameof(ActiveAudioInputProvider), value ?? string.Empty);
}
```

**3.3 — Update `CameraManager` usage in `AudioInputManager`**

The `AudioInputManager` was already designed to use `_settings.ActiveAudioInputProvider`
(see abstraction doc). Wire it up in `SetActiveAsync` and `InitializeAsync`.

**3.4 — Add audio picker to `SettingsViewModel`**

```csharp
// --- Audio Input ---
public IReadOnlyList<IAudioInputProvider> AudioInputProviders
    => _audioInputManager.Providers;

public IAudioInputProvider? SelectedAudioInputProvider
{
    get => _audioInputManager.Active;
    set
    {
        if (value is not null && value != _audioInputManager.Active)
        {
            _ = _audioInputManager.SetActiveAsync(value.ProviderId);
            OnPropertyChanged();
        }
    }
}
```

Inject `AudioInputManager` in `SettingsViewModel` constructor.

**3.5 — Add picker XAML to `SettingsPage.xaml`**

Add after the Camera section (before Debug):

```xml
<!-- SECTION: Audio Input -->
<Label Text="Audio Input" FontSize="18" FontAttributes="Bold" Margin="0,8,0,0" />
<Label Text="Microphone Source" FontSize="13" TextColor="Gray" />
<Picker AutomationId="AudioInputPicker"
        ItemsSource="{Binding AudioInputProviders}"
        ItemDisplayBinding="{Binding DisplayName}"
        SelectedItem="{Binding SelectedAudioInputProvider}" />
```

**Verify:** Settings page shows picker with "System Microphone" (Windows) or
"Phone Microphone" (Android). Selection persists across restarts.

---

### Wave 4: Initialize on App Start

**4.1 — Call `AudioInputManager.InitializeAsync()` on app startup**

In `MainPage.xaml.cs` or `App.xaml.cs`, resolve `AudioInputManager` and call
`InitializeAsync()` after the page loads:

```csharp
// MainPage.xaml.cs constructor
public MainPage(MainViewModel viewModel, PhoneCameraProvider phoneCamera, AudioInputManager audioInputManager)
{
    InitializeComponent();
    BindingContext = viewModel;
    phoneCamera.SetCameraView(CameraPreview);

    Loaded += async (_, _) =>
    {
        await audioInputManager.InitializeAsync();
    };
    ...
}
```

This restores the last-used audio provider from settings (or defaults to "platform").

**Verify:** App start correctly initializes the audio manager. Existing voice
pipeline works end-to-end.

---

## Backward Compatibility Proof

The key invariant: **VoiceInputAgent and MicrophoneCoordinator work without changes.**

### VoiceInputAgent Flow (before → after)

```
BEFORE:
  VoiceInputAgent(IAudioInputService audioInput)
    → audioInput = WindowsAudioInputService (singleton)
    → audioInput.StartAsync() starts NAudio WaveInEvent
    → audioInput.AudioChunkAvailable fires with PCM16

AFTER:
  VoiceInputAgent(IAudioInputService audioInput)
    → audioInput = AudioInputManager (singleton, registered as IAudioInputService)
    → audioInput.StartAsync() → AudioInputManager.StartAsync()
      → _active.StartAsync() → PlatformMicProvider.StartAsync() → NAudio WaveInEvent
    → PlatformMicProvider.AudioChunkAvailable → AudioInputManager.AudioChunkAvailable
    → Same PCM16 data reaches VoiceInputAgent
```

### MicrophoneCoordinator (unchanged)

`MicrophoneCoordinator` calls `IWakeWordService.StartAsync/StopAsync`, not
`IAudioInputService`. It coordinates mic ownership at a higher level. No change needed.

---

## Old Files Disposition

| File | Action |
|------|--------|
| `WindowsAudioInputService.cs` | Keep but no longer registered in DI. Can delete later. |
| `AndroidAudioInputService.cs` | Keep but no longer registered in DI. Can delete later. |
| `AudioInputService.cs` (stub) | Keep but no longer registered in DI. Can delete later. |

These files are not deleted in Phase 1 to minimize risk. They become dead code once
the new providers are wired up. Clean up in a future pass.

---

## Test Plan

### Unit Tests (BodyCam.Tests)

| Test | Validates |
|------|-----------|
| `AudioInputManager_DefaultsToPlatform` | `InitializeAsync()` selects "platform" when no saved pref |
| `AudioInputManager_RestoresSavedProvider` | Reads `ActiveAudioInputProvider` from settings |
| `AudioInputManager_FallbackOnDisconnect` | When active provider fires `Disconnected`, switches to platform |
| `AudioInputManager_StopsOldProviderOnSwitch` | `SetActiveAsync` stops previous before starting new |
| `AudioInputManager_ForwardsAudioChunks` | Provider chunk → Manager chunk event |
| `AudioInputManager_ImplementsIAudioInputService` | `StartAsync`/`StopAsync`/`IsCapturing` delegate correctly |
| `AudioInputManager_StartAsync_FallsBackIfNoActive` | Starts platform mic if no provider is active |

### Integration Tests (manual)

| Scenario | Expected |
|----------|----------|
| App starts → start Realtime session → speak | Audio captured, transcribed, AI responds |
| Settings → Audio Input picker shows "System Microphone" | Single option, selected |
| Start session → stop session → start again | Audio pipeline restarts cleanly |
| Settings → change audio provider → start session | New provider used |

### Regression

All existing voice features must continue to work:
- Start/stop Realtime session
- Voice transcription (input transcript)
- Voice interruption (speech detection)
- Wake word → active session transition (when implemented)

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| `AudioInputManager` DI resolution fails | Test `IAudioInputService` resolves to `AudioInputManager` instance |
| Double registration — old and new both registered | Remove old `#if WINDOWS` block entirely, not comment out |
| Android permission flow different in provider vs service | Copy exact permission logic from `AndroidAudioInputService` |
| Event re-emission adds latency | Direct delegate forwarding (`OnProviderChunk` → `AudioChunkAvailable?.Invoke`) — negligible |
| `PlatformMicProvider` created before settings loaded | `InitializeAsync` called after DI is built and page loaded |
| Old test helpers construct orchestrator without new params | Test files may need updated (same pattern as M11 camera manager fix) |

---

## Exit Criteria

- [ ] `IAudioInputProvider` interface created
- [ ] `PlatformMicProvider` wraps NAudio (Windows) and AudioRecord (Android)
- [ ] `AudioInputManager` implements `IAudioInputService` for backward compatibility
- [ ] `AudioInputManager` manages active provider with platform mic default
- [ ] `VoiceInputAgent` works without code changes
- [ ] `MicrophoneCoordinator` works without code changes
- [ ] Settings page has audio input picker (single "System/Phone Microphone" entry for now)
- [ ] `ActiveAudioInputProvider` persisted in settings
- [ ] All existing voice features pass regression
- [ ] Build succeeds on both Windows and Android targets
- [ ] 229+ existing tests pass
