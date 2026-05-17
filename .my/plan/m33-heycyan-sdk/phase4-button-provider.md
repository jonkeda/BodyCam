# M33 Phase 4 — Button Provider

Implement `HeyCyanButtonProvider : IButtonInputProvider` as a thin adapter
over `IHeyCyanGlassesSession.ButtonPressed`. The QCSDK firmware on the
glasses has a single physical multi-function button and recognizes
**tap / double-tap / long-press** on-device, debounced, and pushes them via
a parsed BLE notify frame (`cmdType=2`). This provider therefore emits
**pre-recognized gestures** straight into `ButtonInputManager` — bypassing
the central `GestureRecognizer` — exactly the same path used by BTHome
remotes (see `m14-buttons/ble-remotes.md`).

**Depends on:** M33 Phase 1 (`IHeyCyanGlassesSession`, `HeyCyanButtonEvent`,
`HeyCyanButtonGesture`), M14 Phase 1 (`IButtonInputProvider`,
`ButtonInputManager`, `ButtonGestureEvent`, `ActionMap`,
`PreRecognizedGesture` extension point).

**Does not replace:** `GattButtonProvider`, `BtHomeButtonProvider`, or
`AvrcpButtonProvider` from M14 — those remain for non-HeyCyan hardware.
The HeyCyan provider is only registered when a HeyCyan session is active.

---

## Wave 1: `HeyCyanButtonProvider`

A platform-agnostic provider that subscribes to the active session and
forwards each gesture as a `ButtonGestureEvent` on the
`PreRecognizedGesture` channel.

```csharp
// Services/Glasses/HeyCyan/HeyCyanButtonProvider.cs
namespace BodyCam.Services.Glasses.HeyCyan;

public sealed class HeyCyanButtonProvider : IButtonInputProvider
{
    public const string ProviderIdConst = "heycyan-glasses";
    private const string ButtonIdConst = "glasses-button";

    private readonly IHeyCyanGlassesSession _session;
    private bool _started;

    public HeyCyanButtonProvider(IHeyCyanGlassesSession session)
    {
        _session = session;
    }

    public string ProviderId => ProviderIdConst;
    public string DisplayName => "HeyCyan Glasses Button";
    public bool IsAvailable => _session.State == HeyCyanState.Connected
                            || _session.State == HeyCyanState.TransferMode;

    public event EventHandler<RawButtonEvent>? RawButtonEvent;          // never raised
    public event EventHandler<ButtonGestureEvent>? PreRecognizedGesture;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_started) return Task.CompletedTask;
        _session.ButtonPressed += OnButtonPressed;
        _started = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        if (!_started) return Task.CompletedTask;
        _session.ButtonPressed -= OnButtonPressed;
        _started = false;
        return Task.CompletedTask;
    }

    private void OnButtonPressed(object? sender, HeyCyanButtonEvent evt)
    {
        var gesture = MapGesture(evt.Gesture);
        PreRecognizedGesture?.Invoke(this, new ButtonGestureEvent
        {
            ProviderId = ProviderId,
            ButtonId = ButtonIdConst,
            Gesture = gesture,
            TimestampMs = evt.Timestamp.ToUnixTimeMilliseconds(),
        });
    }

    internal static ButtonGesture MapGesture(HeyCyanButtonGesture g) => g switch
    {
        HeyCyanButtonGesture.Tap       => ButtonGesture.Tap,
        HeyCyanButtonGesture.DoubleTap => ButtonGesture.DoubleTap,
        HeyCyanButtonGesture.LongPress => ButtonGesture.LongPress,
        _ => ButtonGesture.Tap,
    };

    public void Dispose() => _ = StopAsync();
}
```

### DI registration

Registered alongside `HeyCyanGlassesDeviceManager` so it lives only when a
HeyCyan session has been constructed:

```csharp
services.AddSingleton<IHeyCyanGlassesSession>(sp => /* platform impl */);
services.AddSingleton<HeyCyanButtonProvider>();
services.AddSingleton<IButtonInputProvider>(sp => sp.GetRequiredService<HeyCyanButtonProvider>());
```

### Verify
- [ ] `ProviderId == "heycyan-glasses"`
- [ ] `IsAvailable` toggles with session `StateChanged`
- [ ] Each session `ButtonPressed` raises exactly one `PreRecognizedGesture`
- [ ] `RawButtonEvent` is never raised
- [ ] `StopAsync` unsubscribes and is idempotent

---

## Wave 2: Default Gesture-to-Action Mapping

Per the M33 overview, default mapping for the single glasses button is:

| Gesture     | `ButtonAction`                    |
|-------------|-----------------------------------|
| `Tap`       | `ToggleConversation` (start/stop) |
| `DoubleTap` | `CapturePhoto`                    |
| `LongPress` | `EndSession`                      |

These are seeded into the central `ActionMap` (M14 Phase 1) at startup if
no user override is present:

```csharp
// Services/Glasses/HeyCyan/HeyCyanButtonDefaults.cs
public static class HeyCyanButtonDefaults
{
    public const string ProviderId = HeyCyanButtonProvider.ProviderIdConst;
    public const string ButtonId   = "glasses-button";

    public static void SeedDefaults(ActionMap map)
    {
        map.SetIfUnset(ProviderId, ButtonId, ButtonGesture.Tap,       ButtonAction.ToggleConversation);
        map.SetIfUnset(ProviderId, ButtonId, ButtonGesture.DoubleTap, ButtonAction.CapturePhoto);
        map.SetIfUnset(ProviderId, ButtonId, ButtonGesture.LongPress, ButtonAction.EndSession);
    }
}
```

`SetIfUnset` is the existing M14 helper that respects user remappings
already persisted in settings.

### Verify
- [ ] Defaults are applied on first launch only
- [ ] Existing user mappings survive app upgrades (no overwrite)
- [ ] All three actions resolve to handlers registered in `BodyCamSession`

---

## Wave 3: Settings UI Integration

Add a **Glasses Button** section to the existing `ButtonMappingsPage`
(M14 Phase 3). It enumerates the three fixed gestures of the HeyCyan
button and exposes a dropdown of available `ButtonAction` values for
each. Mappings are stored through the same `IButtonMappingStore` used by
BTHome and GATT providers.

```csharp
// ViewModels/Settings/HeyCyanButtonMappingsViewModel.cs
public sealed class HeyCyanButtonMappingsViewModel : ViewModelBase
{
    private readonly IButtonMappingStore _store;

    public HeyCyanButtonMappingsViewModel(IButtonMappingStore store)
    {
        _store = store;
        AvailableActions = Enum.GetValues<ButtonAction>();
        Tap       = new GestureRow(_store, HeyCyanButtonDefaults.ProviderId, HeyCyanButtonDefaults.ButtonId, ButtonGesture.Tap);
        DoubleTap = new GestureRow(_store, HeyCyanButtonDefaults.ProviderId, HeyCyanButtonDefaults.ButtonId, ButtonGesture.DoubleTap);
        LongPress = new GestureRow(_store, HeyCyanButtonDefaults.ProviderId, HeyCyanButtonDefaults.ButtonId, ButtonGesture.LongPress);
    }

    public IReadOnlyList<ButtonAction> AvailableActions { get; }
    public GestureRow Tap { get; }
    public GestureRow DoubleTap { get; }
    public GestureRow LongPress { get; }
}

public sealed class GestureRow : ViewModelBase
{
    private readonly IButtonMappingStore _store;
    private readonly string _provider;
    private readonly string _button;
    private readonly ButtonGesture _gesture;
    private ButtonAction _action;

    public GestureRow(IButtonMappingStore store, string provider, string button, ButtonGesture gesture)
    {
        _store = store; _provider = provider; _button = button; _gesture = gesture;
        _action = store.Get(provider, button, gesture);
    }

    public string Label => _gesture.ToString();
    public ButtonAction Action
    {
        get => _action;
        set
        {
            if (SetProperty(ref _action, value))
                _store.Set(_provider, _button, _gesture, value);
        }
    }
}
```

XAML: a `CollectionView` of three rows with `Picker ItemsSource="AvailableActions"`
bound to `Action`. The page is shown only when `IHeyCyanGlassesSession`
is registered (i.e. on platforms with the HeyCyan binding).

### Verify
- [ ] Page renders three rows: Tap, DoubleTap, LongPress
- [ ] Changing a dropdown persists via `IButtonMappingStore`
- [ ] New mapping takes effect on the next gesture without restart

---

## Wave 4: Tests

Located in `BodyCam.Tests/Services/Glasses/HeyCyan/`.

### Fake session

```csharp
internal sealed class FakeHeyCyanSession : IHeyCyanGlassesSession
{
    public HeyCyanState State { get; set; } = HeyCyanState.Connected;
    public HeyCyanDeviceInfo? Device => null;
    public event EventHandler<HeyCyanState>? StateChanged;
    public event EventHandler<HeyCyanBattery>? BatteryUpdated;
    public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
    public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
    public event EventHandler<byte[]>? AiPhotoReceived;

    public void RaiseButton(HeyCyanButtonGesture g)
        => ButtonPressed?.Invoke(this, new HeyCyanButtonEvent(g, DateTimeOffset.UtcNow));

    // Other members throw NotImplementedException — not needed for these tests.
}
```

### Provider tests

```csharp
public class HeyCyanButtonProviderTests
{
    [Theory]
    [InlineData(HeyCyanButtonGesture.Tap,       ButtonGesture.Tap)]
    [InlineData(HeyCyanButtonGesture.DoubleTap, ButtonGesture.DoubleTap)]
    [InlineData(HeyCyanButtonGesture.LongPress, ButtonGesture.LongPress)]
    public async Task ForwardsGesture(HeyCyanButtonGesture input, ButtonGesture expected)
    {
        var session  = new FakeHeyCyanSession();
        var provider = new HeyCyanButtonProvider(session);
        await provider.StartAsync();

        ButtonGestureEvent? captured = null;
        provider.PreRecognizedGesture += (_, e) => captured = e;
        session.RaiseButton(input);

        captured.Should().NotBeNull();
        captured!.Gesture.Should().Be(expected);
        captured.ProviderId.Should().Be("heycyan-glasses");
    }

    [Fact]
    public async Task StopAsync_Unsubscribes()
    {
        var session  = new FakeHeyCyanSession();
        var provider = new HeyCyanButtonProvider(session);
        await provider.StartAsync();
        await provider.StopAsync();

        var fired = false;
        provider.PreRecognizedGesture += (_, _) => fired = true;
        session.RaiseButton(HeyCyanButtonGesture.Tap);

        fired.Should().BeFalse();
    }
}
```

### Manager + remap test

```csharp
public class HeyCyanButtonDispatchTests
{
    [Fact]
    public async Task RemappedGesture_TriggersNewAction()
    {
        var session  = new FakeHeyCyanSession();
        var provider = new HeyCyanButtonProvider(session);
        var store    = new InMemoryButtonMappingStore();
        var actionMap = new ActionMap(store);
        HeyCyanButtonDefaults.SeedDefaults(actionMap);

        var manager = new ButtonInputManager(new[] { provider }, actionMap);
        await manager.StartAsync();

        // User remaps Tap → CapturePhoto
        store.Set(HeyCyanButtonDefaults.ProviderId,
                  HeyCyanButtonDefaults.ButtonId,
                  ButtonGesture.Tap,
                  ButtonAction.CapturePhoto);

        ButtonAction? triggered = null;
        manager.ActionTriggered += (_, e) => triggered = e.Action;
        session.RaiseButton(HeyCyanButtonGesture.Tap);

        triggered.Should().Be(ButtonAction.CapturePhoto);
    }
}
```

### Verify
- [ ] All forwarding tests pass for Tap / DoubleTap / LongPress
- [ ] Stop-unsubscribe test passes
- [ ] Remap-dispatch test confirms new action fires after store update
- [ ] No reliance on `GestureRecognizer` timing (events fire synchronously)

---

## Phase 4 Verify Checklist

- [ ] `HeyCyanButtonProvider` implements `IButtonInputProvider` with
      `ProviderId == "heycyan-glasses"`
- [ ] Subscribes to `IHeyCyanGlassesSession.ButtonPressed` only between
      `StartAsync` / `StopAsync`
- [ ] Emits `PreRecognizedGesture` only — never `RawButtonEvent`
- [ ] Default mapping seeded: Tap→ToggleConversation, DoubleTap→CapturePhoto,
      LongPress→EndSession
- [ ] Existing user mappings preserved across launches
- [ ] Settings page lists the three gestures with action dropdowns
- [ ] Remapping persists via `IButtonMappingStore` and takes effect live
- [ ] Generic M14 providers (GATT/BTHome/AVRCP) remain unaffected
- [ ] Unit tests in `BodyCam.Tests` cover forward, stop, and remap flows
