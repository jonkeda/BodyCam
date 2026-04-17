# M14 Phase 3 — Phone Buttons, Gestures & Keyboard Shortcuts

## Goal

Capture input from the phone itself — volume buttons, shake gesture, and keyboard
shortcuts (Windows) — as `IButtonInputProvider` implementations.

---

## Volume Button Provider

Intercept the phone's hardware volume buttons to trigger BodyCam actions. This is
optional and configurable — users who don't want BodyCam stealing their volume keys
can disable it.

### Android — Overriding dispatchKeyEvent

On Android, volume button events arrive via `Activity.dispatchKeyEvent()`.
MAUI's `MainActivity` can override this to intercept volume keys before the
system handles them.

```csharp
namespace BodyCam.Platforms.Android.Input;

/// <summary>
/// Intercepts phone volume button presses on Android.
/// When active, volume-up and volume-down are captured as button events
/// instead of changing the system volume.
/// </summary>
public class VolumeButtonProvider : IButtonInputProvider
{
    public string DisplayName => "Phone Volume Keys";
    public string ProviderId => "volume";
    public bool IsAvailable => true; // Always available on Android
    public bool IsActive { get; private set; }

    public event EventHandler<RawButtonEvent>? RawButtonEvent;
    public event EventHandler? Disconnected;

    /// <summary>
    /// Called from MainActivity.dispatchKeyEvent() when a volume key is pressed.
    /// Returns true if the event was consumed.
    /// </summary>
    public bool HandleKeyEvent(Android.Views.KeyEvent? keyEvent)
    {
        if (!IsActive || keyEvent is null) return false;

        // Only intercept volume keys
        if (keyEvent.KeyCode != Android.Views.Keycode.VolumeUp &&
            keyEvent.KeyCode != Android.Views.Keycode.VolumeDown)
            return false;

        var eventType = keyEvent.Action switch
        {
            Android.Views.KeyEventActions.Down => RawButtonEventType.ButtonDown,
            Android.Views.KeyEventActions.Up => RawButtonEventType.ButtonUp,
            _ => (RawButtonEventType?)null,
        };

        if (eventType is null) return false;

        var buttonId = keyEvent.KeyCode == Android.Views.Keycode.VolumeUp
            ? "volume-up"
            : "volume-down";

        RawButtonEvent?.Invoke(this, new Services.Input.RawButtonEvent
        {
            ProviderId = ProviderId,
            EventType = eventType.Value,
            TimestampMs = Environment.TickCount64,
            ButtonId = buttonId,
        });

        return true; // Consume the event — don't change system volume
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        IsActive = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsActive = false;
        return Task.CompletedTask;
    }

    public void Dispose() { }
}
```

### MainActivity Integration

```csharp
// In Platforms/Android/MainActivity.cs

public class MainActivity : MauiAppCompatActivity
{
    private VolumeButtonProvider? _volumeProvider;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _volumeProvider = IPlatformApplication.Current?.Services
            .GetService<IButtonInputProvider>() as VolumeButtonProvider;
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (_volumeProvider?.HandleKeyEvent(e) == true)
            return true; // Consumed — don't change volume

        return base.DispatchKeyEvent(e);
    }
}
```

### Android Considerations

- **User expectation.** Volume buttons normally change volume. Stealing them is
  disruptive. The setting should be OFF by default with a clear explanation.
- **Separate up/down mapping.** Volume-up and volume-down are independent buttons
  with separate `ButtonId` ("volume-up", "volume-down"). Users might map volume-up
  to "Look" and volume-down to "Photo".
- **Media playback conflict.** If the user is playing music and BodyCam intercepts
  volume keys, they can't adjust volume. Consider only intercepting when a BodyCam
  session is active (`CurrentLayer == ActiveSession`).
- **Screen-off behavior.** `dispatchKeyEvent` only fires when the app is in
  foreground. For screen-off interception, the foreground service would need to
  register a `BroadcastReceiver` for `Intent.ACTION_MEDIA_BUTTON`. This is a
  stretch goal.

### Windows — Volume Key Provider

On Windows, volume keys can be intercepted via WinUI's `KeyDown` event or
Raw Input. For development, the keyboard shortcut provider (below) is more
useful, but volume key interception follows the same pattern.

```csharp
namespace BodyCam.Platforms.Windows.Input;

/// <summary>
/// Intercepts volume key presses on Windows via WinUI keyboard events.
/// Primarily for testing parity with Android.
/// </summary>
public class VolumeButtonProvider : IButtonInputProvider
{
    public string DisplayName => "Phone Volume Keys";
    public string ProviderId => "volume";
    public bool IsAvailable => true;
    public bool IsActive { get; private set; }

    public event EventHandler<RawButtonEvent>? RawButtonEvent;
    public event EventHandler? Disconnected;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsActive) return Task.CompletedTask;

        // Hook into the WinUI window's key events
        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
            as Microsoft.UI.Xaml.Window;

        if (window?.Content is Microsoft.UI.Xaml.UIElement content)
            content.KeyDown += OnKeyDown;

        IsActive = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsActive = false;
        return Task.CompletedTask;
    }

    private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (!IsActive) return;

        var buttonId = e.Key switch
        {
            Windows.System.VirtualKey.VolumeUp => "volume-up",
            Windows.System.VirtualKey.VolumeDown => "volume-down",
            _ => null,
        };

        if (buttonId is null) return;

        RawButtonEvent?.Invoke(this, new Services.Input.RawButtonEvent
        {
            ProviderId = ProviderId,
            EventType = RawButtonEventType.Click,
            TimestampMs = Environment.TickCount64,
            ButtonId = buttonId,
        });

        e.Handled = true;
    }

    public void Dispose() { }
}
```

---

## Shake Gesture Provider

Detect phone shakes using the accelerometer. A shake triggers a `Click` event —
the `GestureRecognizer` converts it to a `SingleTap`. Shakes are inherently
imprecise, so we only support single actions (no double-shake or long-shake).

### Implementation

Uses MAUI Essentials `Accelerometer` API, which works cross-platform.

```csharp
namespace BodyCam.Services.Input;

/// <summary>
/// Detects phone shake gestures using the accelerometer.
/// Emits a Click event on each detected shake.
/// </summary>
public class ShakeGestureProvider : IButtonInputProvider
{
    public string DisplayName => "Phone Shake";
    public string ProviderId => "shake";
    public bool IsAvailable => Accelerometer.IsSupported;
    public bool IsActive { get; private set; }

    public event EventHandler<RawButtonEvent>? RawButtonEvent;
    public event EventHandler? Disconnected;

    /// <summary>
    /// Acceleration magnitude threshold (in g) to trigger a shake.
    /// Default 2.5g — requires a deliberate shake, not normal movement.
    /// </summary>
    public double ShakeThresholdG { get; set; } = 2.5;

    /// <summary>
    /// Minimum time between shake detections to prevent rapid re-triggering.
    /// </summary>
    public int CooldownMs { get; set; } = 1000;

    private long _lastShakeTimestamp;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsActive) return Task.CompletedTask;
        if (!Accelerometer.IsSupported) return Task.CompletedTask;

        Accelerometer.ShakeDetected += OnShakeDetected;
        Accelerometer.Start(SensorSpeed.Game);

        IsActive = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsActive) return Task.CompletedTask;

        Accelerometer.ShakeDetected -= OnShakeDetected;
        Accelerometer.Stop();

        IsActive = false;
        return Task.CompletedTask;
    }

    private void OnShakeDetected(object? sender, EventArgs e)
    {
        var now = Environment.TickCount64;

        // Enforce cooldown
        if (now - _lastShakeTimestamp < CooldownMs) return;
        _lastShakeTimestamp = now;

        RawButtonEvent?.Invoke(this, new RawButtonEvent
        {
            ProviderId = ProviderId,
            EventType = RawButtonEventType.Click,
            TimestampMs = now,
        });
    }

    public void Dispose()
    {
        if (IsActive)
        {
            Accelerometer.ShakeDetected -= OnShakeDetected;
            Accelerometer.Stop();
        }
    }
}
```

### Shake Considerations

- **MAUI Essentials `ShakeDetected`** handles the acceleration math internally.
  It uses a default threshold around 1.3g. If we need custom thresholds, we
  subscribe to `Accelerometer.ReadingChanged` instead and compute magnitude:

  ```csharp
  private void OnReadingChanged(object? sender, AccelerometerChangedEventArgs e)
  {
      var data = e.Reading;
      var magnitude = Math.Sqrt(data.Acceleration.X * data.Acceleration.X
          + data.Acceleration.Y * data.Acceleration.Y
          + data.Acceleration.Z * data.Acceleration.Z);

      if (magnitude > ShakeThresholdG)
      {
          // Trigger shake
      }
  }
  ```

- **False positives.** Walking, driving, or placing the phone down can trigger
  accidental shakes. The 2.5g threshold is deliberately high. The cooldown
  prevents rapid re-triggering.
- **Battery impact.** `SensorSpeed.Game` polls at ~60Hz. Consider `SensorSpeed.UI`
  (~30Hz) if battery is a concern. Shake detection doesn't need high frequency.
- **Default off.** Like volume keys, shake should be opt-in. Users enable it in
  settings.

---

## Keyboard Shortcut Provider (Windows)

For Windows development, keyboard shortcuts provide the best experience. No need
to pair BT glasses or shake the phone — just press F5 to look, F6 for photo, etc.

```csharp
namespace BodyCam.Platforms.Windows.Input;

/// <summary>
/// Keyboard shortcuts for development on Windows.
/// Maps function keys and key combinations to button events.
/// </summary>
public class KeyboardShortcutProvider : IButtonInputProvider
{
    public string DisplayName => "Keyboard Shortcuts";
    public string ProviderId => "keyboard";
    public bool IsAvailable => true;
    public bool IsActive { get; private set; }

    public event EventHandler<RawButtonEvent>? RawButtonEvent;
    public event EventHandler? Disconnected;

    /// <summary>
    /// Keyboard shortcuts mapped to button IDs.
    /// Each shortcut emits a Click with the corresponding buttonId.
    /// </summary>
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

        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
            as Microsoft.UI.Xaml.Window;

        if (window?.Content is Microsoft.UI.Xaml.UIElement content)
        {
            content.KeyDown += OnKeyDown;
            content.KeyUp += OnKeyUp;
        }

        IsActive = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsActive) return Task.CompletedTask;

        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
            as Microsoft.UI.Xaml.Window;

        if (window?.Content is Microsoft.UI.Xaml.UIElement content)
        {
            content.KeyDown -= OnKeyDown;
            content.KeyUp -= OnKeyUp;
        }

        IsActive = false;
        return Task.CompletedTask;
    }

    private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (!IsActive || !KeyMap.TryGetValue(e.Key, out var buttonId)) return;

        RawButtonEvent?.Invoke(this, new Services.Input.RawButtonEvent
        {
            ProviderId = ProviderId,
            EventType = RawButtonEventType.ButtonDown,
            TimestampMs = Environment.TickCount64,
            ButtonId = buttonId,
        });

        e.Handled = true;
    }

    private void OnKeyUp(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (!IsActive || !KeyMap.TryGetValue(e.Key, out var buttonId)) return;

        RawButtonEvent?.Invoke(this, new Services.Input.RawButtonEvent
        {
            ProviderId = ProviderId,
            EventType = RawButtonEventType.ButtonUp,
            TimestampMs = Environment.TickCount64,
            ButtonId = buttonId,
        });

        e.Handled = true;
    }

    public void Dispose()
    {
        if (IsActive)
            StopAsync().GetAwaiter().GetResult();
    }
}
```

### Keyboard Shortcut Mapping

Unlike BT glasses (where all button presses go through gesture recognition and
the action map), keyboard shortcuts are **direct-mapped**. Each key has a unique
`buttonId`, and the `ActionMap` maps each buttonId's `SingleTap` to a specific action:

```csharp
// Default keyboard shortcut mappings (set in ActionMap initialization)
actionMap.SetAction("keyboard:look", ButtonGesture.SingleTap, ButtonAction.Look);
actionMap.SetAction("keyboard:photo", ButtonGesture.SingleTap, ButtonAction.Photo);
actionMap.SetAction("keyboard:read", ButtonGesture.SingleTap, ButtonAction.Read);
actionMap.SetAction("keyboard:find", ButtonGesture.SingleTap, ButtonAction.Find);
actionMap.SetAction("keyboard:toggle-session", ButtonGesture.SingleTap, ButtonAction.ToggleSession);
```

This means keyboard shortcuts bypass double-tap and long-press detection entirely
(since each action has its own key). The `GestureRecognizer` still processes them —
a quick F5 tap is a SingleTap, holding F5 is a LongPress — but the default mapping
only uses `SingleTap` for keyboard.

### Key Choices

| Key | Action | Rationale |
|-----|--------|-----------|
| **F5** | Look | "Refresh/see" — familiar from browser |
| **F6** | Photo | Next key after F5 |
| **F7** | Read | Next key |
| **F8** | Find | Next key |
| **F9** | Toggle Session | Start/stop — separated from action keys |

Function keys were chosen because they're rarely used in MAUI dev (unlike
Ctrl+S, F5-debug, etc.). If conflicts arise, the mapping is configurable.

---

## Settings Integration

### Per-Provider Enable/Disable

Each provider has an enable/disable toggle in settings. Disabled providers
are not started by `ButtonInputManager`.

```csharp
// In ISettingsService
bool IsButtonProviderEnabled(string providerId);
void SetButtonProviderEnabled(string providerId, bool enabled);

// Default states:
// "keyboard" → enabled (Windows only)
// "avrcp"   → enabled (when glasses paired)
// "volume"  → disabled (opt-in)
// "shake"   → disabled (opt-in)
```

### Shake Sensitivity

```csharp
// In ISettingsService
double ShakeThresholdG { get; set; } // Default 2.5

// In settings UI — slider from 1.5 (very sensitive) to 4.0 (hard shake required)
```

### Settings Page UI

```xml
<!-- In SettingsPage.xaml — Button Input section -->
<VerticalStackLayout Spacing="8">
    <Label Text="Button Input" FontSize="18" FontAttributes="Bold" />

    <!-- Provider toggles -->
    <HorizontalStackLayout Spacing="8">
        <Label Text="Keyboard Shortcuts" VerticalOptions="Center" />
        <Switch IsToggled="{Binding KeyboardEnabled}" />
    </HorizontalStackLayout>

    <HorizontalStackLayout Spacing="8">
        <Label Text="BT Glasses Media Button" VerticalOptions="Center" />
        <Switch IsToggled="{Binding AvrcpEnabled}" />
    </HorizontalStackLayout>

    <HorizontalStackLayout Spacing="8">
        <Label Text="Volume Keys" VerticalOptions="Center" />
        <Switch IsToggled="{Binding VolumeKeysEnabled}" />
    </HorizontalStackLayout>

    <HorizontalStackLayout Spacing="8">
        <Label Text="Phone Shake" VerticalOptions="Center" />
        <Switch IsToggled="{Binding ShakeEnabled}" />
    </HorizontalStackLayout>

    <!-- Shake sensitivity slider (visible when shake is enabled) -->
    <VerticalStackLayout IsVisible="{Binding ShakeEnabled}" Spacing="4">
        <Label Text="Shake Sensitivity" />
        <Slider Minimum="1.5" Maximum="4.0"
                Value="{Binding ShakeThresholdG}"
                MinimumTrackColor="{StaticResource Primary}" />
        <Label Text="{Binding ShakeThresholdG, StringFormat='Threshold: {0:F1}g'}"
               FontSize="12" TextColor="Gray" />
    </VerticalStackLayout>
</VerticalStackLayout>
```

---

## Testing

### FakeButtonProvider for Unit Tests

```csharp
namespace BodyCam.Tests.Fakes;

/// <summary>
/// Fake button provider for unit testing.
/// Allows test code to simulate button presses.
/// </summary>
public class FakeButtonProvider : IButtonInputProvider
{
    public string DisplayName { get; set; } = "Fake";
    public string ProviderId { get; set; } = "fake";
    public bool IsAvailable { get; set; } = true;
    public bool IsActive { get; private set; }

    public event EventHandler<RawButtonEvent>? RawButtonEvent;
    public event EventHandler? Disconnected;

    public Task StartAsync(CancellationToken ct = default)
    {
        IsActive = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsActive = false;
        return Task.CompletedTask;
    }

    /// <summary>Simulate a button click.</summary>
    public void SimulateClick(string buttonId = "primary")
    {
        RawButtonEvent?.Invoke(this, new Services.Input.RawButtonEvent
        {
            ProviderId = ProviderId,
            EventType = RawButtonEventType.Click,
            TimestampMs = Environment.TickCount64,
            ButtonId = buttonId,
        });
    }

    /// <summary>Simulate button down.</summary>
    public void SimulateDown(string buttonId = "primary")
    {
        RawButtonEvent?.Invoke(this, new Services.Input.RawButtonEvent
        {
            ProviderId = ProviderId,
            EventType = RawButtonEventType.ButtonDown,
            TimestampMs = Environment.TickCount64,
            ButtonId = buttonId,
        });
    }

    /// <summary>Simulate button up.</summary>
    public void SimulateUp(string buttonId = "primary")
    {
        RawButtonEvent?.Invoke(this, new Services.Input.RawButtonEvent
        {
            ProviderId = ProviderId,
            EventType = RawButtonEventType.ButtonUp,
            TimestampMs = Environment.TickCount64,
            ButtonId = buttonId,
        });
    }

    /// <summary>Simulate disconnection.</summary>
    public void SimulateDisconnect() => Disconnected?.Invoke(this, EventArgs.Empty);

    public void Dispose() { }
}
```

### ButtonInputManager Integration Tests

```csharp
public class ButtonInputManagerTests
{
    [Fact]
    public async Task SingleTap_TriggersDefaultLookAction()
    {
        var fake = new FakeButtonProvider();
        var settings = new FakeSettingsService();
        var manager = new ButtonInputManager(new[] { fake }, settings);

        var actions = new List<ButtonActionEvent>();
        manager.ActionTriggered += (_, e) => actions.Add(e);

        await manager.StartAsync();

        fake.SimulateClick();

        // Wait for gesture recognition (single tap has 300ms window)
        await Task.Delay(400);

        actions.Should().ContainSingle()
            .Which.Action.Should().Be(ButtonAction.Look);
    }

    [Fact]
    public async Task DoubleTap_TriggersPhotoAction()
    {
        var fake = new FakeButtonProvider();
        var settings = new FakeSettingsService();
        var manager = new ButtonInputManager(new[] { fake }, settings);

        var actions = new List<ButtonActionEvent>();
        manager.ActionTriggered += (_, e) => actions.Add(e);

        await manager.StartAsync();

        fake.SimulateClick();
        await Task.Delay(100);
        fake.SimulateClick();

        await Task.Delay(100);

        actions.Should().ContainSingle()
            .Which.Action.Should().Be(ButtonAction.Photo);
    }

    [Fact]
    public async Task DisconnectedProvider_IsRemovedFromListening()
    {
        var fake = new FakeButtonProvider();
        var settings = new FakeSettingsService();
        var manager = new ButtonInputManager(new[] { fake }, settings);

        await manager.StartAsync();
        fake.SimulateDisconnect();

        var actions = new List<ButtonActionEvent>();
        manager.ActionTriggered += (_, e) => actions.Add(e);

        // Events after disconnect should not trigger actions
        fake.SimulateClick();
        await Task.Delay(400);

        actions.Should().BeEmpty();
    }
}
```
