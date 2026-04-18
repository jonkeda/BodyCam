# M14 Phase 1 — Abstraction, Gesture Recognition & Keyboard Shortcuts

**Status:** NOT STARTED  
**Prerequisite:** M11 Phase 1 (Camera Abstraction) — completed, established the provider pattern  
**Goal:** Create the button/gesture input infrastructure — `IButtonInputProvider`,
`GestureRecognizer`, `ButtonInputManager`, `ActionMap` — and implement
`KeyboardShortcutProvider` for Windows dev testing. Wire into `MainViewModel` so
keyboard shortcuts trigger existing commands (Look, Read, Find, Photo, Toggle Session).

---

## Current State (What Exists)

| Component | Location | Status |
|-----------|----------|--------|
| `MainViewModel` | `ViewModels/MainViewModel.cs` | Has `LookCommand`, `ReadCommand`, `FindCommand`, `PhotoCommand`, `AskCommand`, `SetStateCommand` |
| `MainPage.xaml` | `MainPage.xaml` | Buttons bound to ViewModel commands — touch only |
| `SendVisionCommandAsync` | `MainViewModel` | Two paths: session running → Realtime API, not running → VisionAgent directly |
| `SetLayerAsync` | `MainViewModel` | Escalation/de-escalation: Sleep ↔ WakeWord ↔ ActiveSession |
| No button abstraction | — | All actions require touching the phone screen |

**Key design decision (Option B — Unified Dispatch):** Both XAML touch buttons
and `ButtonInputManager` route through a single `DispatchActionAsync(ButtonAction)`
method in `MainViewModel`. The existing commands (`LookCommand`, `ReadCommand`, etc.)
become thin wrappers around `DispatchActionAsync`. This gives one dispatch point for
logging, throttling, and future extensions — without routing touch taps through
gesture recognition infrastructure they don't need.

---

## Deliverables

### New Files

| File | Purpose |
|------|---------|
| `Services/Input/ButtonAction.cs` | Enum of triggerable actions |
| `Services/Input/RawButtonEvent.cs` | Raw button event types and data |
| `Services/Input/ButtonGesture.cs` | Gesture types and gesture event data |
| `Services/Input/IButtonInputProvider.cs` | Interface — all button sources implement this |
| `Services/Input/GestureRecognizer.cs` | Converts raw down/up/click → SingleTap/DoubleTap/LongPress |
| `Services/Input/ActionMap.cs` | Maps (providerId, gesture) → ButtonAction |
| `Services/Input/ButtonInputManager.cs` | Aggregates providers, wires gesture recognition + action dispatch |
| `Platforms/Windows/Input/KeyboardShortcutProvider.cs` | Windows keyboard shortcuts (F5-F9) |

### Modified Files

| File | Change |
|------|--------|
| `MauiProgram.cs` | Register `KeyboardShortcutProvider`, `ButtonInputManager` |
| `ViewModels/MainViewModel.cs` | Add `DispatchActionAsync(ButtonAction)`, refactor existing commands to call it, inject `ButtonInputManager` and subscribe to `ActionTriggered` |
| `MainPage.xaml.cs` | Inject `ButtonInputManager`, call `StartAsync()` on load |

### Unchanged Files

| File | Why Unchanged |
|------|---------------|
| `MainPage.xaml` | Touch buttons still bind to ViewModel commands (which now call `DispatchActionAsync`) |
| `Orchestration/AgentOrchestrator.cs` | Actions route through ViewModel, not orchestrator |
| All agents | Button input triggers ViewModel commands, agents are downstream |

---

## Implementation Waves

### Wave 1: Enums, Events & Interface (no integration yet)

Create the type system. Compile to verify. No existing code modified.

**1.1 — Create `ButtonAction` enum**

```csharp
// Services/Input/ButtonAction.cs
namespace BodyCam.Services.Input;

public enum ButtonAction
{
    None,
    Look,
    Read,
    Find,
    ToggleSession,
    Photo,
    ToggleSleepActive,
    PushToTalk,
}
```

**1.2 — Create `RawButtonEvent` types**

```csharp
// Services/Input/RawButtonEvent.cs
namespace BodyCam.Services.Input;

public enum RawButtonEventType
{
    ButtonDown,
    ButtonUp,
    Click,  // Discrete click (no separate down/up)
}

public sealed class RawButtonEvent
{
    public required string ProviderId { get; init; }
    public required RawButtonEventType EventType { get; init; }
    public required long TimestampMs { get; init; }
    public string ButtonId { get; init; } = "primary";
}
```

**1.3 — Create `ButtonGesture` types**

```csharp
// Services/Input/ButtonGesture.cs
namespace BodyCam.Services.Input;

public enum ButtonGesture
{
    SingleTap,
    DoubleTap,
    LongPress,
    LongPressRelease,
}

public sealed class ButtonGestureEvent
{
    public required string ProviderId { get; init; }
    public required ButtonGesture Gesture { get; init; }
    public string ButtonId { get; init; } = "primary";
    public required long TimestampMs { get; init; }
}

public sealed class ButtonActionEvent
{
    public required ButtonAction Action { get; init; }
    public required string SourceProviderId { get; init; }
    public required ButtonGesture SourceGesture { get; init; }
    public required long TimestampMs { get; init; }
}
```

**1.4 — Create `IButtonInputProvider` interface**

```csharp
// Services/Input/IButtonInputProvider.cs
namespace BodyCam.Services.Input;

public interface IButtonInputProvider : IDisposable
{
    string DisplayName { get; }
    string ProviderId { get; }
    bool IsAvailable { get; }
    bool IsActive { get; }

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();

    event EventHandler<RawButtonEvent>? RawButtonEvent;

    /// <summary>
    /// Optional: for providers with firmware-level gesture recognition
    /// (e.g. BTHome remotes). ButtonInputManager routes these directly
    /// to ActionMap, bypassing GestureRecognizer.
    /// </summary>
    event EventHandler<ButtonGestureEvent>? PreRecognizedGesture;

    event EventHandler? Disconnected;
}
```

Key design: Multiple providers can be active simultaneously (unlike camera/audio
where only one is active). `PreRecognizedGesture` supports BTHome-style devices
that report gestures from firmware (Phase 2+).

**Verify:** All four files compile. No existing code changed.

---

### Wave 2: GestureRecognizer & ActionMap

**2.1 — Create `GestureRecognizer`**

```csharp
// Services/Input/GestureRecognizer.cs
namespace BodyCam.Services.Input;

public sealed class GestureRecognizer : IDisposable
{
    public int DoubleTapWindowMs { get; set; } = 300;
    public int LongPressThresholdMs { get; set; } = 500;

    public event EventHandler<ButtonGestureEvent>? GestureRecognized;

    private readonly Dictionary<string, ButtonState> _states = new();
    private readonly object _lock = new();

    public void ProcessEvent(RawButtonEvent evt) { ... }
    internal void RaiseGesture(ButtonGestureEvent gesture) { ... }
    public void Dispose() { ... }

    private sealed class ButtonState : IDisposable { ... }
}
```

Per-button state machine:

- **ButtonDown** → cancel pending tap timer, start long-press timer
- **ButtonUp** before long-press threshold → increment tap count, start double-tap timer
- **Double-tap timer expires** (300ms) → emit `SingleTap`
- **Second tap within window** → emit `DoubleTap` immediately
- **Long-press timer fires** (500ms while held) → emit `LongPress`
- **ButtonUp after long press** → emit `LongPressRelease`
- **Click** (discrete) → treated as ButtonDown + immediate ButtonUp

Full implementation in [button-abstraction.md](button-abstraction.md).

**2.2 — Create `ActionMap`**

```csharp
// Services/Input/ActionMap.cs
namespace BodyCam.Services.Input;

public sealed class ButtonMapping
{
    public required string ProviderId { get; init; }
    public required ButtonGesture Gesture { get; init; }
    public required ButtonAction Action { get; init; }
}

public sealed class ActionMap
{
    private readonly Dictionary<(string ProviderId, ButtonGesture Gesture), ButtonAction> _map = new();

    public ButtonAction GetAction(string providerId, ButtonGesture gesture)
    {
        // Try provider+buttonId-specific mapping first
        if (_map.TryGetValue((providerId, gesture), out var action))
            return action;
        // Fall back to defaults
        return GetDefaultAction(gesture);
    }

    public void SetAction(string providerId, ButtonGesture gesture, ButtonAction action)
        => _map[(providerId, gesture)] = action;

    public void LoadMappings(IEnumerable<ButtonMapping> mappings) { ... }
    public IReadOnlyList<ButtonMapping> ExportMappings() { ... }

    private static ButtonAction GetDefaultAction(ButtonGesture gesture) => gesture switch
    {
        ButtonGesture.SingleTap => ButtonAction.Look,
        ButtonGesture.DoubleTap => ButtonAction.Photo,
        ButtonGesture.LongPress => ButtonAction.ToggleSession,
        _ => ButtonAction.None,
    };
}
```

Default mapping: SingleTap=Look, DoubleTap=Photo, LongPress=ToggleSession. This
applies to any provider that doesn't have custom mappings (e.g. a single-button
BLE remote).

**Verify:** Compile. No existing code changed.

---

### Wave 3: ButtonInputManager & KeyboardShortcutProvider

**3.1 — Create `ButtonInputManager`**

```csharp
// Services/Input/ButtonInputManager.cs
namespace BodyCam.Services.Input;

public sealed class ButtonInputManager : IDisposable
{
    private readonly IReadOnlyList<IButtonInputProvider> _providers;
    private readonly GestureRecognizer _gestureRecognizer;
    private readonly ActionMap _actionMap;

    public event EventHandler<ButtonActionEvent>? ActionTriggered;

    public ButtonInputManager(IEnumerable<IButtonInputProvider> providers)
    {
        _providers = providers.ToList();
        _gestureRecognizer = new GestureRecognizer();
        _actionMap = new ActionMap();

        _gestureRecognizer.GestureRecognized += OnGestureRecognized;
    }

    public IReadOnlyList<IButtonInputProvider> Providers => _providers;
    public ActionMap ActionMap => _actionMap;

    public async Task StartAsync(CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            if (!provider.IsAvailable) continue;
            provider.RawButtonEvent += OnRawButtonEvent;
            provider.PreRecognizedGesture += OnPreRecognizedGesture;
            provider.Disconnected += OnProviderDisconnected;
            await provider.StartAsync(ct);
        }
    }

    public async Task StopAsync() { ... }

    private void OnRawButtonEvent(object? sender, RawButtonEvent evt)
        => _gestureRecognizer.ProcessEvent(evt);

    private void OnPreRecognizedGesture(object? sender, ButtonGestureEvent gesture)
        => DispatchAction(gesture);

    private void OnGestureRecognized(object? sender, ButtonGestureEvent gesture)
        => DispatchAction(gesture);

    private void DispatchAction(ButtonGestureEvent gesture)
    {
        var action = _actionMap.GetAction(
            $"{gesture.ProviderId}:{gesture.ButtonId}", gesture.Gesture);
        if (action == ButtonAction.None) return;

        ActionTriggered?.Invoke(this, new ButtonActionEvent
        {
            Action = action,
            SourceProviderId = gesture.ProviderId,
            SourceGesture = gesture.Gesture,
            TimestampMs = gesture.TimestampMs,
        });
    }
}
```

Key: The manager aggregates all providers simultaneously. Events go through
`GestureRecognizer` (raw down/up) or directly to `ActionMap` (pre-recognized).
Output is `ActionTriggered` event consumed by `MainViewModel`.

**3.2 — Create `KeyboardShortcutProvider` (Windows)**

```csharp
// Platforms/Windows/Input/KeyboardShortcutProvider.cs
namespace BodyCam.Services.Input;

public class KeyboardShortcutProvider : IButtonInputProvider
{
    public string DisplayName => "Keyboard Shortcuts";
    public string ProviderId => "keyboard";
    public bool IsAvailable => true;
    public bool IsActive { get; private set; }

    public event EventHandler<RawButtonEvent>? RawButtonEvent;
    public event EventHandler<ButtonGestureEvent>? PreRecognizedGesture;
    public event EventHandler? Disconnected;

    private static readonly Dictionary<Windows.System.VirtualKey, string> KeyMap = new()
    {
        [Windows.System.VirtualKey.F5] = "look",
        [Windows.System.VirtualKey.F6] = "photo",
        [Windows.System.VirtualKey.F7] = "read",
        [Windows.System.VirtualKey.F8] = "find",
        [Windows.System.VirtualKey.F9] = "toggle-session",
    };

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsActive) return Task.CompletedTask;
        var window = Application.Current?.Windows.FirstOrDefault()
            ?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (window?.Content is Microsoft.UI.Xaml.UIElement content)
        {
            content.KeyDown += OnKeyDown;
            content.KeyUp += OnKeyUp;
        }
        IsActive = true;
        return Task.CompletedTask;
    }

    public Task StopAsync() { ... } // Unsubscribe KeyDown/KeyUp
    private void OnKeyDown(...) { ... } // Emit ButtonDown with mapped buttonId
    private void OnKeyUp(...) { ... }   // Emit ButtonUp with mapped buttonId
    public void Dispose() { ... }
}
```

**Keyboard shortcut mapping:**

| Key | `ButtonId` | Default Action |
|-----|-----------|---------------|
| F5 | `"look"` | Look |
| F6 | `"photo"` | Photo |
| F7 | `"read"` | Read |
| F8 | `"find"` | Find |
| F9 | `"toggle-session"` | ToggleSession |

Each key emits raw `ButtonDown`/`ButtonUp` events. The `GestureRecognizer` converts
them to `SingleTap` (quick press), `LongPress` (held >500ms), etc. The `ActionMap`
has per-buttonId mappings so each key maps to a specific action:

```csharp
// Set during ButtonInputManager initialization
actionMap.SetAction("keyboard:look", ButtonGesture.SingleTap, ButtonAction.Look);
actionMap.SetAction("keyboard:photo", ButtonGesture.SingleTap, ButtonAction.Photo);
actionMap.SetAction("keyboard:read", ButtonGesture.SingleTap, ButtonAction.Read);
actionMap.SetAction("keyboard:find", ButtonGesture.SingleTap, ButtonAction.Find);
actionMap.SetAction("keyboard:toggle-session", ButtonGesture.SingleTap, ButtonAction.ToggleSession);
```

**Verify:** All new files compile. No existing code changed yet.

---

### Wave 4: Wire into DI and MainViewModel (Unified Dispatch)

**4.1 — Register in `MauiProgram.cs`**

```csharp
// Button input
#if WINDOWS
builder.Services.AddSingleton<IButtonInputProvider, KeyboardShortcutProvider>();
#endif
builder.Services.AddSingleton<ButtonInputManager>();
```

Add `using BodyCam.Services.Input;` to `MauiProgram.cs`.

**4.2 — Add `DispatchActionAsync` to `MainViewModel`**

This is the single dispatch point for ALL action sources — touch buttons AND
hardware buttons. Extract the logic that was previously inline in each command.

```csharp
public async Task DispatchActionAsync(ButtonAction action)
{
    try
    {
        switch (action)
        {
            case ButtonAction.Look:
                await SendVisionCommandAsync("Describe what you see in front of me.");
                break;
            case ButtonAction.Read:
                await SendVisionCommandAsync("Read any text you can see in front of me.");
                break;
            case ButtonAction.Find:
                await SendVisionCommandAsync("Look around and tell me what objects you can find.");
                break;
            case ButtonAction.Photo:
                await SendVisionCommandAsync("Take a photo of what you see.");
                break;
            case ButtonAction.ToggleSession:
                if (IsRunning)
                    await SetLayerAsync("Sleep");
                else
                    await SetLayerAsync("Active");
                break;
            case ButtonAction.ToggleSleepActive:
                if (CurrentLayer == ListeningLayer.Sleep)
                    await SetLayerAsync("Active");
                else
                    await SetLayerAsync("Sleep");
                break;
        }
    }
    catch (Exception ex)
    {
        DebugLog += $"[{DateTime.Now:HH:mm:ss}] Action error ({action}): {ex.Message}{Environment.NewLine}";
    }
}
```

**4.3 — Refactor existing XAML commands to use `DispatchActionAsync`**

The existing commands currently call `SendVisionCommandAsync` and `SetLayerAsync`
directly. Refactor them to route through the unified dispatch:

```csharp
// BEFORE (current)
LookCommand = new AsyncRelayCommand(async () =>
{
    await SendVisionCommandAsync("Describe what you see in front of me.");
});
ReadCommand = new AsyncRelayCommand(async () =>
{
    await SendVisionCommandAsync("Read any text you can see in front of me.");
});
FindCommand = new AsyncRelayCommand(async () =>
{
    await SendVisionCommandAsync("Look around and tell me what objects you can find.");
});
AskCommand = new AsyncRelayCommand(async () =>
{
    await SetLayerAsync("Active");
});
PhotoCommand = new AsyncRelayCommand(async () =>
{
    await SendVisionCommandAsync("Take a photo of what you see.");
});

// AFTER (unified)
LookCommand = new AsyncRelayCommand(() => DispatchActionAsync(ButtonAction.Look));
ReadCommand = new AsyncRelayCommand(() => DispatchActionAsync(ButtonAction.Read));
FindCommand = new AsyncRelayCommand(() => DispatchActionAsync(ButtonAction.Find));
PhotoCommand = new AsyncRelayCommand(() => DispatchActionAsync(ButtonAction.Photo));
AskCommand = new AsyncRelayCommand(() => DispatchActionAsync(ButtonAction.ToggleSession));
```

Now touch buttons, keyboard shortcuts, BLE remotes, and any future input source
all flow through the same `DispatchActionAsync` method.

**4.4 — Inject `ButtonInputManager` and subscribe to `ActionTriggered`**

Add `ButtonInputManager` to the constructor:

```csharp
public MainViewModel(
    AgentOrchestrator orchestrator,
    IApiKeyService apiKeyService,
    ISettingsService settingsService,
    CameraManager cameraManager,
    ButtonInputManager buttonInput)
{
    // ... existing setup ...
    _buttonInput = buttonInput;
    _buttonInput.ActionTriggered += OnButtonAction;
}
```

The handler is a thin bridge to `DispatchActionAsync`:

```csharp
private async void OnButtonAction(object? sender, ButtonActionEvent e)
{
    await DispatchActionAsync(e.Action);
}
```

**4.5 — Start `ButtonInputManager` on page load**

In `MainPage.xaml.cs`, add `ButtonInputManager` to the constructor and call
`StartAsync()`:

```csharp
public MainPage(
    MainViewModel viewModel,
    PhoneCameraProvider phoneCamera,
    AudioInputManager audioInputManager,
    AudioOutputManager audioOutputManager,
    ButtonInputManager buttonInput)
{
    InitializeComponent();
    BindingContext = viewModel;
    phoneCamera.SetCameraView(CameraPreview);

    Loaded += async (_, _) =>
    {
        await audioInputManager.InitializeAsync();
        await audioOutputManager.InitializeAsync();
        await buttonInput.StartAsync();
    };
}
```

**Verify:** Build succeeds. Press F5 on Windows → triggers "Look" action. Tap
"Look" button on screen → also triggers "Look" action. Both go through
`DispatchActionAsync(ButtonAction.Look)`.

---

## Unified Dispatch Proof

### Path 1: Touch button (XAML)

```
User taps "Look" button on screen
  → LookCommand (AsyncRelayCommand) fires
    → DispatchActionAsync(ButtonAction.Look)
      → case ButtonAction.Look
      → await SendVisionCommandAsync("Describe what you see in front of me.")
```

### Path 2: Keyboard shortcut (F5)

```
User presses F5 (Windows keyboard)
  → KeyboardShortcutProvider.OnKeyDown
    → RawButtonEvent { ProviderId="keyboard", ButtonId="look", EventType=ButtonDown }
  → (user releases F5 quickly)
  → KeyboardShortcutProvider.OnKeyUp
    → RawButtonEvent { ProviderId="keyboard", ButtonId="look", EventType=ButtonUp }

  → GestureRecognizer.ProcessEvent (ButtonDown)
    → starts long-press timer (500ms)
  → GestureRecognizer.ProcessEvent (ButtonUp)
    → cancels long-press timer
    → tapCount=1, starts double-tap timer (300ms)
  → (300ms elapses, no second tap)
    → emit ButtonGestureEvent { Gesture=SingleTap, ProviderId="keyboard", ButtonId="look" }

  → ButtonInputManager.OnGestureRecognized
    → ActionMap.GetAction("keyboard:look", SingleTap)
    → returns ButtonAction.Look (from pre-configured mapping)
    → ActionTriggered event fires

  → MainViewModel.OnButtonAction
    → DispatchActionAsync(ButtonAction.Look)   ← SAME method as touch
      → case ButtonAction.Look
      → await SendVisionCommandAsync("Describe what you see in front of me.")
```

### Path 3: Future BLE remote (BTHome)

```
User presses button on Shelly BLU Remote
  → BtHomeButtonProvider parses BTHome advertisement
    → PreRecognizedGesture { Gesture=SingleTap, ProviderId="bthome-XX:XX" }

  → ButtonInputManager.OnPreRecognizedGesture (bypasses GestureRecognizer)
    → ActionMap.GetAction("bthome-XX:XX:primary", SingleTap)
    → returns ButtonAction.Look
    → ActionTriggered event fires

  → MainViewModel.OnButtonAction
    → DispatchActionAsync(ButtonAction.Look)   ← SAME method
```

All three paths converge at `DispatchActionAsync` — one place for logging,
throttling, and error handling.

---

## Test Plan

### Unit Tests (BodyCam.Tests)

| Test | Validates |
|------|-----------|
| `GestureRecognizer_SingleTap` | ButtonDown + ButtonUp (quick) → SingleTap after 300ms |
| `GestureRecognizer_DoubleTap` | Two quick taps within 300ms → DoubleTap immediately |
| `GestureRecognizer_LongPress` | ButtonDown held >500ms → LongPress, then ButtonUp → LongPressRelease |
| `GestureRecognizer_Click` | Click event → treated as down+up → SingleTap after 300ms |
| `GestureRecognizer_IndependentButtons` | Two different buttonIds don't interfere |
| `ActionMap_DefaultMapping` | SingleTap=Look, DoubleTap=Photo, LongPress=ToggleSession |
| `ActionMap_CustomMapping` | SetAction overrides default |
| `ActionMap_PerProviderMapping` | Different providers map same gesture to different actions |
| `ButtonInputManager_DispatchesActions` | RawEvent → Gesture → ActionMap → ActionTriggered |
| `ButtonInputManager_PreRecognized` | PreRecognizedGesture → ActionMap → ActionTriggered (bypasses GestureRecognizer) |
| `ButtonInputManager_StartsProviders` | StartAsync calls each available provider's StartAsync |
| `MainViewModel_DispatchAction_Look` | `DispatchActionAsync(Look)` calls `SendVisionCommandAsync` |
| `MainViewModel_DispatchAction_Toggle` | `DispatchActionAsync(ToggleSession)` calls `SetLayerAsync` |
| `MainViewModel_LookCommand_RoutesViaDispatch` | LookCommand → `DispatchActionAsync(Look)` (unified path) |

### Integration Tests (manual, Windows)

| Scenario | Expected |
|----------|----------|
| App starts → press F5 | "Look" action triggers, camera captures, transcript shows result |
| App starts → press F6 | "Photo" action triggers |
| App starts → press F7 | "Read" action triggers |
| App starts → press F8 | "Find" action triggers |
| App starts → press F9 | Session starts (or stops if already running) |
| Hold F5 for >500ms | Long press action triggers (default: ToggleSession) |
| Quick F5, F5 (within 300ms) | Double tap triggers (default: Photo) |
| Touch "Look" button on screen | Triggers via `DispatchActionAsync(Look)` — same path as F5 |

### Regression

All existing features must continue to work:
- Touch-based Look/Read/Find/Photo/Ask buttons
- Start/stop session via segmented control
- Camera capture and vision pipeline
- Voice pipeline (audio in/out)

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| GestureRecognizer timer race conditions | State machine uses lock + CancellationTokenSource; per-button state isolation |
| 300ms SingleTap delay feels laggy for keyboard | Acceptable for keyboard (user won't double-tap F5). For future BTHome providers, `PreRecognizedGesture` bypasses this delay |
| `async void OnButtonAction` exception swallowing | Wrap in try/catch, log to DebugLog |
| WinUI KeyDown only fires when app has focus | Expected — keyboard shortcuts are for dev, not background operation |
| F-key conflicts with IDE debugger | F5-F9 chosen to avoid common IDE shortcuts. Mapping is configurable via ActionMap |
| Test files need updated constructors | MainViewModel gains `ButtonInputManager` parameter — test helpers may need updated |

---

## Exit Criteria

- [ ] `IButtonInputProvider` interface defined with `RawButtonEvent`, `PreRecognizedGesture`, `Disconnected`
- [ ] `GestureRecognizer` correctly distinguishes SingleTap, DoubleTap, LongPress, LongPressRelease
- [ ] `ActionMap` maps (provider+button, gesture) → action with defaults
- [ ] `ButtonInputManager` aggregates providers, wires gesture recognition, dispatches actions
- [ ] `KeyboardShortcutProvider` captures F5-F9 on Windows
- [ ] `MainViewModel` subscribes to `ActionTriggered` and routes to `DispatchActionAsync`
- [ ] Existing XAML commands (Look, Read, Find, Photo, Ask) refactored to call `DispatchActionAsync`
- [ ] Touch buttons and keyboard shortcuts both flow through unified `DispatchActionAsync`
- [ ] F5=Look, F6=Photo, F7=Read, F8=Find, F9=ToggleSession work on Windows
- [ ] All existing touch-based UI buttons continue to work (via refactored commands)
- [ ] Build succeeds on both Windows and Android targets
- [ ] GestureRecognizer has unit tests for all gesture types
- [ ] 229+ existing tests pass
