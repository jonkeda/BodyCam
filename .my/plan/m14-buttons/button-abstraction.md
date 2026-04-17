# M14 — Button Abstraction Layer

## ButtonAction Enum

The set of actions that a button gesture can trigger. These map directly to
existing MainViewModel commands and orchestrator methods.

```csharp
namespace BodyCam.Services.Input;

/// <summary>
/// Actions that can be triggered by button input.
/// Maps to existing MainViewModel commands.
/// </summary>
public enum ButtonAction
{
    /// <summary>No action (unmapped gesture).</summary>
    None,

    /// <summary>Describe what the camera sees ("Look" button).</summary>
    Look,

    /// <summary>Read text visible to the camera ("Read" button).</summary>
    Read,

    /// <summary>Find objects in the camera view ("Find" button).</summary>
    Find,

    /// <summary>Start/stop active session ("Ask" button).</summary>
    ToggleSession,

    /// <summary>Capture a photo and describe it ("Photo" button).</summary>
    Photo,

    /// <summary>Toggle between Sleep and Active states.</summary>
    ToggleSleepActive,

    /// <summary>Push-to-talk: hold to listen, release to stop.</summary>
    PushToTalk,
}
```

---

## Raw Button Events

Providers emit raw button events. The `GestureRecognizer` converts these into
semantic gestures (single tap, double tap, long press).

```csharp
namespace BodyCam.Services.Input;

/// <summary>
/// The type of raw button event from a hardware source.
/// </summary>
public enum RawButtonEventType
{
    /// <summary>Button was pressed down (finger on button).</summary>
    ButtonDown,

    /// <summary>Button was released (finger lifted).</summary>
    ButtonUp,

    /// <summary>
    /// A discrete click event (some sources don't report down/up separately).
    /// GestureRecognizer treats this as a ButtonDown immediately followed by ButtonUp.
    /// </summary>
    Click,
}

/// <summary>
/// A raw button event from a hardware input source.
/// </summary>
public sealed class RawButtonEvent
{
    /// <summary>The provider that generated this event.</summary>
    public required string ProviderId { get; init; }

    /// <summary>The type of raw event.</summary>
    public required RawButtonEventType EventType { get; init; }

    /// <summary>When the event occurred (monotonic clock).</summary>
    public required long TimestampMs { get; init; }

    /// <summary>
    /// Optional button identifier when a provider has multiple buttons.
    /// Default "primary" for single-button sources.
    /// </summary>
    public string ButtonId { get; init; } = "primary";
}
```

---

## Button Gestures

The semantic gestures that the `GestureRecognizer` produces from raw events.

```csharp
namespace BodyCam.Services.Input;

/// <summary>
/// A recognized gesture from raw button events.
/// </summary>
public enum ButtonGesture
{
    /// <summary>Single quick press and release.</summary>
    SingleTap,

    /// <summary>Two quick presses in succession.</summary>
    DoubleTap,

    /// <summary>Button held down for an extended period.</summary>
    LongPress,

    /// <summary>Button released after a long press (for push-to-talk).</summary>
    LongPressRelease,
}

/// <summary>
/// A recognized button gesture with metadata.
/// </summary>
public sealed class ButtonGestureEvent
{
    /// <summary>The provider that generated the raw events.</summary>
    public required string ProviderId { get; init; }

    /// <summary>The recognized gesture.</summary>
    public required ButtonGesture Gesture { get; init; }

    /// <summary>The button that was pressed.</summary>
    public string ButtonId { get; init; } = "primary";

    /// <summary>When the gesture was recognized.</summary>
    public required long TimestampMs { get; init; }
}
```

---

## IButtonInputProvider

The central interface all button/gesture sources implement. Providers emit raw
button events; the `GestureRecognizer` handles tap/double-tap/long-press detection.

```csharp
namespace BodyCam.Services.Input;

/// <summary>
/// A source of button input events (BT glasses, volume keys, keyboard, etc.).
/// Multiple providers can be active simultaneously.
/// </summary>
public interface IButtonInputProvider : IDisposable
{
    /// <summary>
    /// Human-readable name for this input source (e.g. "BT Glasses", "Volume Keys").
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Unique identifier for this provider type (e.g. "avrcp", "gatt", "volume", "keyboard").
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Whether this input source is currently available and can produce events.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Whether this provider is currently active and listening for button events.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Start listening for button events. Idempotent.
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stop listening for button events. Idempotent.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Raised when a raw button event is detected.
    /// </summary>
    event EventHandler<RawButtonEvent>? RawButtonEvent;

    /// <summary>
    /// Raised when the input source disconnects (e.g. BT glasses turned off).
    /// </summary>
    event EventHandler? Disconnected;
}
```

### Design Decisions

**Multiple providers active simultaneously.** Unlike `CameraManager` (one active
camera), `ButtonInputManager` can listen to all connected providers at once. A user
might have glasses buttons AND volume keys active. No conflict — they're independent
input sources.

**Raw events, not gestures.** Providers emit `RawButtonEvent` (down/up/click), not
gestures. Gesture recognition (single/double/long) is centralized in `GestureRecognizer`
so all providers share the same timing thresholds and behavior. Providers that only
report discrete clicks emit `RawButtonEventType.Click`.

**`IDisposable` not `IAsyncDisposable`.** Button providers don't hold heavy async
resources like camera streams. Simple `Dispose()` suffices for unsubscribing from
platform events.

---

## GestureRecognizer

Converts raw button events into semantic gestures. Uses timing thresholds to
distinguish single tap, double tap, and long press.

```csharp
namespace BodyCam.Services.Input;

/// <summary>
/// Converts raw button events into semantic gestures (single tap, double tap, long press).
/// Maintains state per (providerId, buttonId) pair.
/// </summary>
public sealed class GestureRecognizer : IDisposable
{
    /// <summary>Max time between two taps to count as a double tap.</summary>
    public int DoubleTapWindowMs { get; set; } = 300;

    /// <summary>Min hold time to trigger a long press.</summary>
    public int LongPressThresholdMs { get; set; } = 500;

    /// <summary>Raised when a gesture is recognized.</summary>
    public event EventHandler<ButtonGestureEvent>? GestureRecognized;

    // State per (providerId, buttonId)
    private readonly Dictionary<string, ButtonState> _states = new();
    private readonly object _lock = new();

    /// <summary>
    /// Feed a raw button event into the recognizer.
    /// </summary>
    public void ProcessEvent(RawButtonEvent evt)
    {
        var key = $"{evt.ProviderId}:{evt.ButtonId}";

        lock (_lock)
        {
            if (!_states.TryGetValue(key, out var state))
            {
                state = new ButtonState(key, this);
                _states[key] = state;
            }

            state.ProcessEvent(evt);
        }
    }

    internal void RaiseGesture(ButtonGestureEvent gesture)
        => GestureRecognized?.Invoke(this, gesture);

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var state in _states.Values)
                state.Dispose();
            _states.Clear();
        }
    }

    /// <summary>
    /// Per-button state machine for gesture recognition.
    /// </summary>
    private sealed class ButtonState : IDisposable
    {
        private readonly string _key;
        private readonly GestureRecognizer _owner;
        private CancellationTokenSource? _tapTimerCts;
        private CancellationTokenSource? _longPressCts;

        private long _lastDownTimestamp;
        private int _tapCount;
        private bool _longPressFired;

        public ButtonState(string key, GestureRecognizer owner)
        {
            _key = key;
            _owner = owner;
        }

        public void ProcessEvent(RawButtonEvent evt)
        {
            switch (evt.EventType)
            {
                case RawButtonEventType.ButtonDown:
                    HandleButtonDown(evt);
                    break;

                case RawButtonEventType.ButtonUp:
                    HandleButtonUp(evt);
                    break;

                case RawButtonEventType.Click:
                    // Treat Click as instantaneous down+up
                    HandleButtonDown(evt);
                    HandleButtonUp(evt);
                    break;
            }
        }

        private void HandleButtonDown(RawButtonEvent evt)
        {
            _lastDownTimestamp = evt.TimestampMs;
            _longPressFired = false;

            // Cancel any pending tap timer (we might be starting a double tap)
            _tapTimerCts?.Cancel();
            _tapTimerCts = null;

            // Start long-press timer
            _longPressCts?.Cancel();
            _longPressCts = new CancellationTokenSource();
            var cts = _longPressCts;

            _ = Task.Delay(_owner.LongPressThresholdMs, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;

                _longPressFired = true;
                _tapCount = 0;
                _owner.RaiseGesture(new ButtonGestureEvent
                {
                    ProviderId = evt.ProviderId,
                    ButtonId = evt.ButtonId,
                    Gesture = ButtonGesture.LongPress,
                    TimestampMs = evt.TimestampMs + _owner.LongPressThresholdMs,
                });
            }, TaskScheduler.Default);
        }

        private void HandleButtonUp(RawButtonEvent evt)
        {
            // Cancel long-press timer
            _longPressCts?.Cancel();
            _longPressCts = null;

            // If long press already fired, emit release event
            if (_longPressFired)
            {
                _longPressFired = false;
                _owner.RaiseGesture(new ButtonGestureEvent
                {
                    ProviderId = evt.ProviderId,
                    ButtonId = evt.ButtonId,
                    Gesture = ButtonGesture.LongPressRelease,
                    TimestampMs = evt.TimestampMs,
                });
                return;
            }

            // Count this as a tap
            _tapCount++;

            if (_tapCount >= 2)
            {
                // Double tap confirmed
                _tapCount = 0;
                _tapTimerCts?.Cancel();
                _tapTimerCts = null;

                _owner.RaiseGesture(new ButtonGestureEvent
                {
                    ProviderId = evt.ProviderId,
                    ButtonId = evt.ButtonId,
                    Gesture = ButtonGesture.DoubleTap,
                    TimestampMs = evt.TimestampMs,
                });
                return;
            }

            // First tap — wait for possible second tap
            _tapTimerCts?.Cancel();
            _tapTimerCts = new CancellationTokenSource();
            var cts = _tapTimerCts;

            _ = Task.Delay(_owner.DoubleTapWindowMs, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;

                // No second tap arrived — single tap confirmed
                _tapCount = 0;
                _owner.RaiseGesture(new ButtonGestureEvent
                {
                    ProviderId = evt.ProviderId,
                    ButtonId = evt.ButtonId,
                    Gesture = ButtonGesture.SingleTap,
                    TimestampMs = evt.TimestampMs + _owner.DoubleTapWindowMs,
                });
            }, TaskScheduler.Default);
        }

        public void Dispose()
        {
            _tapTimerCts?.Cancel();
            _longPressCts?.Cancel();
        }
    }
}
```

### Timing Diagram

```
Single Tap:
  ┌──┐
  │  │                    (300ms wait — no second tap)
──┘  └──────────────────────── → SingleTap

Double Tap:
  ┌──┐    ┌──┐
  │  │    │  │            (second tap within 300ms window)
──┘  └────┘  └──────────────── → DoubleTap

Long Press:
  ┌──────────────────┐
  │   (>500ms held)  │
──┘                  └──────── → LongPress (at 500ms) + LongPressRelease (on up)
```

---

## Action Mapping

Maps (ProviderId, ButtonGesture) → ButtonAction. Stored in settings so users
can customize.

```csharp
namespace BodyCam.Services.Input;

/// <summary>
/// A single mapping entry: for a given provider and gesture, what action to perform.
/// </summary>
public sealed class ButtonMapping
{
    public required string ProviderId { get; init; }
    public required ButtonGesture Gesture { get; init; }
    public required ButtonAction Action { get; init; }
}

/// <summary>
/// Manages the gesture-to-action mappings. Loaded from settings, supports customization.
/// </summary>
public sealed class ActionMap
{
    private readonly Dictionary<(string ProviderId, ButtonGesture Gesture), ButtonAction> _map = new();

    /// <summary>
    /// Look up the action for a given provider and gesture.
    /// Returns ButtonAction.None if no mapping exists.
    /// </summary>
    public ButtonAction GetAction(string providerId, ButtonGesture gesture)
    {
        return _map.TryGetValue((providerId, gesture), out var action)
            ? action
            : GetDefaultAction(gesture);
    }

    /// <summary>
    /// Set a mapping for a provider and gesture.
    /// </summary>
    public void SetAction(string providerId, ButtonGesture gesture, ButtonAction action)
    {
        _map[(providerId, gesture)] = action;
    }

    /// <summary>
    /// Load mappings from a flat list (deserialized from settings).
    /// </summary>
    public void LoadMappings(IEnumerable<ButtonMapping> mappings)
    {
        _map.Clear();
        foreach (var m in mappings)
            _map[(m.ProviderId, m.Gesture)] = m.Action;
    }

    /// <summary>
    /// Export mappings as a flat list (for serialization to settings).
    /// </summary>
    public IReadOnlyList<ButtonMapping> ExportMappings()
    {
        return _map.Select(kvp => new ButtonMapping
        {
            ProviderId = kvp.Key.ProviderId,
            Gesture = kvp.Key.Gesture,
            Action = kvp.Value,
        }).ToList();
    }

    /// <summary>
    /// Default action for a gesture when no per-provider mapping exists.
    /// Matches the M4 design: SingleTap=push-to-talk, DoubleTap=photo, LongPress=toggle session.
    /// </summary>
    private static ButtonAction GetDefaultAction(ButtonGesture gesture) => gesture switch
    {
        ButtonGesture.SingleTap => ButtonAction.Look,
        ButtonGesture.DoubleTap => ButtonAction.Photo,
        ButtonGesture.LongPress => ButtonAction.ToggleSession,
        ButtonGesture.LongPressRelease => ButtonAction.None,
        _ => ButtonAction.None,
    };
}
```

---

## ButtonInputManager

Aggregates all connected providers, feeds events through the `GestureRecognizer`,
looks up actions in the `ActionMap`, and executes them.

```csharp
namespace BodyCam.Services.Input;

/// <summary>
/// Aggregates button input from all connected providers, recognizes gestures,
/// and dispatches mapped actions.
/// </summary>
public sealed class ButtonInputManager : IDisposable
{
    private readonly IReadOnlyList<IButtonInputProvider> _providers;
    private readonly GestureRecognizer _gestureRecognizer;
    private readonly ActionMap _actionMap;
    private readonly ISettingsService _settings;

    /// <summary>
    /// Raised when an action is triggered by a button gesture.
    /// The ViewModel subscribes to this to execute the action.
    /// </summary>
    public event EventHandler<ButtonActionEvent>? ActionTriggered;

    public ButtonInputManager(
        IEnumerable<IButtonInputProvider> providers,
        ISettingsService settings)
    {
        _providers = providers.ToList();
        _settings = settings;
        _gestureRecognizer = new GestureRecognizer();
        _actionMap = new ActionMap();

        // Load saved mappings from settings
        var saved = _settings.GetButtonMappings();
        if (saved is not null)
            _actionMap.LoadMappings(saved);

        // Wire gesture recognizer output
        _gestureRecognizer.GestureRecognized += OnGestureRecognized;
    }

    /// <summary>All registered button input providers.</summary>
    public IReadOnlyList<IButtonInputProvider> Providers => _providers;

    /// <summary>The action mapping configuration.</summary>
    public ActionMap ActionMap => _actionMap;

    /// <summary>
    /// Start all available providers.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            if (!provider.IsAvailable) continue;

            provider.RawButtonEvent += OnRawButtonEvent;
            provider.Disconnected += OnProviderDisconnected;
            await provider.StartAsync(ct);
        }
    }

    /// <summary>
    /// Stop all active providers.
    /// </summary>
    public async Task StopAsync()
    {
        foreach (var provider in _providers)
        {
            provider.RawButtonEvent -= OnRawButtonEvent;
            provider.Disconnected -= OnProviderDisconnected;

            if (provider.IsActive)
                await provider.StopAsync();
        }
    }

    /// <summary>
    /// Save current action mappings to settings.
    /// </summary>
    public void SaveMappings()
    {
        _settings.SetButtonMappings(_actionMap.ExportMappings());
    }

    private void OnRawButtonEvent(object? sender, RawButtonEvent evt)
    {
        _gestureRecognizer.ProcessEvent(evt);
    }

    private void OnGestureRecognized(object? sender, ButtonGestureEvent gesture)
    {
        var action = _actionMap.GetAction(gesture.ProviderId, gesture.Gesture);
        if (action == ButtonAction.None) return;

        ActionTriggered?.Invoke(this, new ButtonActionEvent
        {
            Action = action,
            SourceProviderId = gesture.ProviderId,
            SourceGesture = gesture.Gesture,
            TimestampMs = gesture.TimestampMs,
        });
    }

    private void OnProviderDisconnected(object? sender, EventArgs e)
    {
        if (sender is IButtonInputProvider provider)
        {
            provider.RawButtonEvent -= OnRawButtonEvent;
            provider.Disconnected -= OnProviderDisconnected;
        }
    }

    public void Dispose()
    {
        _gestureRecognizer.GestureRecognized -= OnGestureRecognized;
        _gestureRecognizer.Dispose();

        foreach (var provider in _providers)
        {
            provider.RawButtonEvent -= OnRawButtonEvent;
            provider.Disconnected -= OnProviderDisconnected;
            provider.Dispose();
        }
    }
}

/// <summary>
/// Event raised when a button gesture triggers a mapped action.
/// </summary>
public sealed class ButtonActionEvent
{
    public required ButtonAction Action { get; init; }
    public required string SourceProviderId { get; init; }
    public required ButtonGesture SourceGesture { get; init; }
    public required long TimestampMs { get; init; }
}
```

---

## DI Registration

```csharp
// In MauiProgram.cs

// Button input providers
#if WINDOWS
builder.Services.AddSingleton<IButtonInputProvider, KeyboardShortcutProvider>();
#endif
#if ANDROID
builder.Services.AddSingleton<IButtonInputProvider, VolumeButtonProvider>();
#endif
builder.Services.AddSingleton<IButtonInputProvider, ShakeGestureProvider>();
// BT providers registered dynamically when glasses are paired (via M4)

// Button input manager
builder.Services.AddSingleton<ButtonInputManager>();
```

---

## Integration with MainViewModel

The ViewModel subscribes to `ButtonInputManager.ActionTriggered` and executes
the corresponding command. This keeps the ViewModel as the single point of
action execution — buttons trigger the same code paths as UI touches.

```csharp
// In MainViewModel constructor
public MainViewModel(
    AgentOrchestrator orchestrator,
    IApiKeyService apiKeyService,
    ISettingsService settingsService,
    ButtonInputManager buttonInput)
{
    // ... existing setup ...

    _buttonInput = buttonInput;
    _buttonInput.ActionTriggered += OnButtonAction;
}

private async void OnButtonAction(object? sender, ButtonActionEvent evt)
{
    await MainThread.InvokeOnMainThreadAsync(async () =>
    {
        switch (evt.Action)
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
                if (CurrentLayer == ListeningLayer.ActiveSession)
                    await SetLayerAsync("Sleep");
                else
                    await SetLayerAsync("Active");
                break;

            case ButtonAction.ToggleSleepActive:
                await ToggleAsync();
                break;

            case ButtonAction.PushToTalk:
                // Push-to-talk uses LongPress/LongPressRelease pair
                // LongPress → start listening, LongPressRelease → stop
                if (evt.SourceGesture == ButtonGesture.LongPress)
                    await SetLayerAsync("Active");
                break;
        }
    });
}
```

---

## ISettingsService Extensions

New settings for button mappings:

```csharp
// Added to ISettingsService
IReadOnlyList<ButtonMapping>? GetButtonMappings();
void SetButtonMappings(IReadOnlyList<ButtonMapping> mappings);

// In SettingsService — stored as JSON in Preferences
public IReadOnlyList<ButtonMapping>? GetButtonMappings()
{
    var json = Preferences.Get("button_mappings", null as string);
    if (json is null) return null;
    return JsonSerializer.Deserialize<List<ButtonMapping>>(json);
}

public void SetButtonMappings(IReadOnlyList<ButtonMapping> mappings)
{
    var json = JsonSerializer.Serialize(mappings);
    Preferences.Set("button_mappings", json);
}
```

---

## Testing

Unit tests for `GestureRecognizer` are critical — timing-based gesture detection
is tricky to get right.

```csharp
// In BodyCam.Tests/Services/Input/GestureRecognizerTests.cs
public class GestureRecognizerTests
{
    [Fact]
    public void SingleTap_EmittedAfterDoubleTapWindow()
    {
        var recognizer = new GestureRecognizer { DoubleTapWindowMs = 100 };
        var gestures = new List<ButtonGestureEvent>();
        recognizer.GestureRecognized += (_, g) => gestures.Add(g);

        recognizer.ProcessEvent(new RawButtonEvent
        {
            ProviderId = "test",
            EventType = RawButtonEventType.ButtonDown,
            TimestampMs = 0,
        });
        recognizer.ProcessEvent(new RawButtonEvent
        {
            ProviderId = "test",
            EventType = RawButtonEventType.ButtonUp,
            TimestampMs = 50,
        });

        // Wait for double-tap window to expire
        Thread.Sleep(150);

        gestures.Should().ContainSingle()
            .Which.Gesture.Should().Be(ButtonGesture.SingleTap);
    }

    [Fact]
    public void DoubleTap_EmittedImmediatelyOnSecondTap()
    {
        var recognizer = new GestureRecognizer { DoubleTapWindowMs = 300 };
        var gestures = new List<ButtonGestureEvent>();
        recognizer.GestureRecognized += (_, g) => gestures.Add(g);

        // First tap
        recognizer.ProcessEvent(new RawButtonEvent
        {
            ProviderId = "test",
            EventType = RawButtonEventType.Click,
            TimestampMs = 0,
        });

        // Second tap within window
        recognizer.ProcessEvent(new RawButtonEvent
        {
            ProviderId = "test",
            EventType = RawButtonEventType.Click,
            TimestampMs = 100,
        });

        gestures.Should().ContainSingle()
            .Which.Gesture.Should().Be(ButtonGesture.DoubleTap);
    }

    [Fact]
    public void LongPress_EmittedAfterThreshold()
    {
        var recognizer = new GestureRecognizer { LongPressThresholdMs = 200 };
        var gestures = new List<ButtonGestureEvent>();
        recognizer.GestureRecognized += (_, g) => gestures.Add(g);

        recognizer.ProcessEvent(new RawButtonEvent
        {
            ProviderId = "test",
            EventType = RawButtonEventType.ButtonDown,
            TimestampMs = 0,
        });

        // Hold past threshold
        Thread.Sleep(250);

        gestures.Should().ContainSingle()
            .Which.Gesture.Should().Be(ButtonGesture.LongPress);

        // Release
        recognizer.ProcessEvent(new RawButtonEvent
        {
            ProviderId = "test",
            EventType = RawButtonEventType.ButtonUp,
            TimestampMs = 300,
        });

        gestures.Should().HaveCount(2);
        gestures[1].Gesture.Should().Be(ButtonGesture.LongPressRelease);
    }
}
```

### ActionMap Tests

```csharp
public class ActionMapTests
{
    [Fact]
    public void DefaultMapping_SingleTap_ReturnsLook()
    {
        var map = new ActionMap();
        map.GetAction("any-provider", ButtonGesture.SingleTap)
            .Should().Be(ButtonAction.Look);
    }

    [Fact]
    public void CustomMapping_OverridesDefault()
    {
        var map = new ActionMap();
        map.SetAction("glasses", ButtonGesture.SingleTap, ButtonAction.Photo);

        map.GetAction("glasses", ButtonGesture.SingleTap)
            .Should().Be(ButtonAction.Photo);

        // Other providers still get default
        map.GetAction("keyboard", ButtonGesture.SingleTap)
            .Should().Be(ButtonAction.Look);
    }

    [Fact]
    public void RoundTrip_Serialization()
    {
        var map = new ActionMap();
        map.SetAction("glasses", ButtonGesture.DoubleTap, ButtonAction.Read);

        var exported = map.ExportMappings();
        var newMap = new ActionMap();
        newMap.LoadMappings(exported);

        newMap.GetAction("glasses", ButtonGesture.DoubleTap)
            .Should().Be(ButtonAction.Read);
    }
}
```
