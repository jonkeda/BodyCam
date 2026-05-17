# M33 Phase 4 — Wave 1: `HeyCyanButtonProvider`

**Parent:** [`../phase4-button-provider.md`](../phase4-button-provider.md)
**Siblings:** [wave2](wave2-default-gesture-mapping.md) · [wave3](wave3-settings-ui.md) · [wave4](wave4-tests.md)
**Depends on:** M33 Phase 1 (`IHeyCyanGlassesSession`, `HeyCyanButtonEvent`,
`HeyCyanButtonGesture`), M14 Phase 1 (`IButtonInputProvider`,
`ButtonGestureEvent`, `PreRecognizedGesture` channel).

> See [sdk-api-reference.md](../sdk-api-reference.md) for the authoritative
> Android SDK type/method names.

## Goal

Implement `HeyCyanButtonProvider` as a thin, platform-agnostic adapter over
`IHeyCyanGlassesSession.ButtonPressed`. The QCSDK firmware does **on-device
debouncing and gesture recognition** for the single multi-function button
and publishes the result as a parsed `GlassesDeviceNotifyRsp` notify frame
over the multiplexed device-notify channel
(`LargeDataHandler.GetInstance().AddOutDeviceListener(100, listener)`),
where the type byte at `LoadData[6]` distinguishes the two button
notifications:

- `LoadData[6] == 0x02` — **AI-photo button** (single physical button,
  short-press / photo gesture)
- `LoadData[6] == 0x03` — **AI-voice button** (same physical button,
  long-press / voice gesture, with `LoadData[7] == 1`)

Note that one physical button raises **two distinct notification types**
depending on the gesture / glasses firmware mode. (Frames with
`LoadData[6] == 0x08` IP and `LoadData[6] == 0x09` P2P-error also arrive
on the transfer-only channel `AddOutDeviceListener(2, listener)`; the
button provider does not consume those.) Reassembly of fragmented BLE
notifications is handled upstream by `LargeDataParser.GetInstance()`.

This provider therefore raises `PreRecognizedGesture` only — bypassing
the central `GestureRecognizer` — exactly the same path the BTHome remote
provider uses (see `m14-buttons/ble-remotes.md`). The iOS QCSDK
framework happens to surface the same data on a combined `cmdType=2`
channel; on Android the same information arrives via the multiplexed
`AddOutDeviceListener(100, ...)` listener and is parsed from the
`GlassesDeviceNotifyRsp.LoadData` payload as described above.

> **Threading:** all `ILargeDataResponse<T>.ParseData` callbacks fire on
> the BLE I/O `HandlerThread` owned by `BleOperateManager`.
> `IHeyCyanGlassesSession` (M33 Phase 1) is responsible for marshalling
> these onto a known `SynchronizationContext` before raising
> `ButtonPressed`; this provider therefore assumes events arrive on a
> safe context.

## Steps

1. **Create the provider class** at
   `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanButtonProvider.cs`.

    ```csharp
    namespace BodyCam.Services.Glasses.HeyCyan;

    public sealed class HeyCyanButtonProvider : IButtonInputProvider, IDisposable
    {
        public const string ProviderIdConst = "heycyan-glasses";
        internal const string ButtonIdConst = "glasses-button";

        private readonly IHeyCyanGlassesSession _session;
        private readonly ILogger<HeyCyanButtonProvider> _log;
        private bool _started;

        public HeyCyanButtonProvider(
            IHeyCyanGlassesSession session,
            ILogger<HeyCyanButtonProvider> log)
        {
            _session = session;
            _log = log;
        }

        public string ProviderId  => ProviderIdConst;
        public string DisplayName => "HeyCyan Glasses Button";

        public bool IsAvailable =>
            _session.State == HeyCyanState.Connected ||
            _session.State == HeyCyanState.TransferMode;

        public event EventHandler<RawButtonEvent>?     RawButtonEvent;          // never raised
        public event EventHandler<ButtonGestureEvent>? PreRecognizedGesture;

        public Task StartAsync(CancellationToken ct = default)
        {
            if (_started) return Task.CompletedTask;
            _session.ButtonPressed += OnButtonPressed;
            _started = true;
            _log.LogInformation("HeyCyanButtonProvider started");
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
                ProviderId   = ProviderIdConst,
                ButtonId     = ButtonIdConst,
                Gesture      = gesture,
                TimestampMs  = evt.Timestamp.ToUnixTimeMilliseconds(),
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

2. **Subscription scope.** The provider subscribes to `ButtonPressed` only
   between `StartAsync` and `StopAsync`. It does **not** subscribe at
   construction time; `ButtonInputManager` controls the lifecycle. Re-entry
   into `StartAsync` is a no-op when already started.

3. **Channel discipline.** Always raise `PreRecognizedGesture`, never
   `RawButtonEvent`. Downstream `ButtonInputManager` will route
   pre-recognized events directly into `ActionMap.Resolve(...)`, skipping
   `GestureRecognizer` entirely (per the M14 contract).

4. **Availability tracking.** `IsAvailable` is computed from
   `_session.State`. To keep `ButtonInputManager`'s availability cache fresh,
   raise an `AvailabilityChanged` notification by re-evaluating on
   `_session.StateChanged`. Add inside `StartAsync`:

    ```csharp
    _session.StateChanged += OnSessionStateChanged;
    // and the inverse in StopAsync
    ```

    where `OnSessionStateChanged` calls
    `AvailabilityChanged?.Invoke(this, IsAvailable);` (the standard
    `IButtonInputProvider` event from M14 Phase 1).

5. **DI registration.** Register the provider on platforms that have a
   HeyCyan session (Android in M33 Phase 1, iOS in Phase 6). In
   `MauiProgram.cs` (or the platform-specific `ConfigureHeyCyanServices`
   extension):

    ```csharp
    services.AddSingleton<HeyCyanButtonProvider>();
    services.AddSingleton<IButtonInputProvider>(
        sp => sp.GetRequiredService<HeyCyanButtonProvider>());
    ```

    The `IHeyCyanGlassesSession` registration comes from M33 Phase 1; do not
    duplicate it here.

6. **No platform `#if` guards.** This file is fully platform-agnostic
   because it only consumes `IHeyCyanGlassesSession`. Keep it under
   `Services/Glasses/HeyCyan/`, **not** under `Platforms/`.

## Verify

- [ ] `ProviderId == "heycyan-glasses"` and `ButtonId == "glasses-button"`
- [ ] `IsAvailable` returns `true` when session is `Connected` or
      `TransferMode`, `false` otherwise
- [ ] `AvailabilityChanged` fires on `IHeyCyanGlassesSession.StateChanged`
- [ ] Each session `ButtonPressed` raises exactly one `PreRecognizedGesture`
      with the expected `ButtonGesture` mapping
- [ ] `RawButtonEvent` is never invoked
- [ ] `StopAsync` unsubscribes from both `ButtonPressed` and `StateChanged`
      and is idempotent
- [ ] `Dispose` triggers `StopAsync`
- [ ] No XAML, no platform code, no MAUI dependencies in the provider file
- [ ] Provider is registered as `IButtonInputProvider` and resolves at runtime
      alongside the existing M14 providers (GATT/BTHome/AVRCP) without
      conflict
