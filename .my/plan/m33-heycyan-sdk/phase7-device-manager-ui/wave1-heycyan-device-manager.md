# M33 Phase 7 — Wave 1: `HeyCyanGlassesDeviceManager`

**Parent:** [../phase7-device-manager-ui.md](../phase7-device-manager-ui.md)
**Siblings:** [wave2-glasses-page.md](wave2-glasses-page.md) ·
[wave3-shell-battery-widget.md](wave3-shell-battery-widget.md) ·
[wave4-fallback-verification.md](wave4-fallback-verification.md) ·
[wave5-real-hardware-checklist.md](wave5-real-hardware-checklist.md)

## Goal

Create a concrete `GlassesDeviceManager` subclass — `HeyCyanGlassesDeviceManager`
— that aggregates the HeyCyan session (P1/P6), the four provider adapters
(camera P2, mic+speaker P3, button P4) and the optional media-transfer helper
(P5), then projects QCSDK session events onto the existing
`GlassesDeviceManager` observables defined in M17 Phase 1. This is the
single composition root that the rest of the app (and Waves 2–5) bind to.

> **Reminder:** auto-fallback is **already implemented** by the M17
> base class plus the M11/M12/M13/M14 manager classes. This wave only
> *plugs HeyCyan in* — it does **not** invent new fallback logic.

## Steps

1. **Create the file** `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanGlassesDeviceManager.cs`
   in the existing `BodyCam.Services.Glasses.HeyCyan` namespace alongside the
   P1–P5 providers. It must inherit from the M17
   `BodyCam.Services.Glasses.GlassesDeviceManager` base.

2. **Constructor — inject all dependencies** that already exist from prior
   phases. Pass the four providers up to the base ctor; keep the session
   and the optional media-transfer helper as private fields.

   ```csharp
   namespace BodyCam.Services.Glasses.HeyCyan;

   public sealed class HeyCyanGlassesDeviceManager : GlassesDeviceManager
   {
       private readonly IHeyCyanGlassesSession _session;
       private readonly HeyCyanCameraProvider _camera;
       private readonly HeyCyanAudioInputProvider _mic;
       private readonly HeyCyanAudioOutputProvider _speaker;
       private readonly HeyCyanButtonProvider _button;
       private readonly HeyCyanMediaTransfer? _media;
       private readonly ILogger<HeyCyanGlassesDeviceManager> _log;

       private HeyCyanDeviceInfo? _lastDevice;

       public HeyCyanGlassesDeviceManager(
           IHeyCyanGlassesSession session,
           HeyCyanCameraProvider camera,
           HeyCyanAudioInputProvider mic,
           HeyCyanAudioOutputProvider speaker,
           HeyCyanButtonProvider button,
           HeyCyanMediaTransfer? media,
           ILogger<HeyCyanGlassesDeviceManager> log)
           : base(camera, mic, speaker, button)
       {
           _session = session;
           _camera = camera;
           _mic = mic;
           _speaker = speaker;
           _button = button;
           _media = media;
           _log = log;

           _session.StateChanged      += OnSessionStateChanged;
           _session.BatteryUpdated    += OnBatteryUpdated;
           _session.MediaCountUpdated += OnMediaCountUpdated;
           _session.ButtonPressed     += OnButtonPressed;
       }
   }
   ```

3. **Status surface** — expose the live HeyCyan-specific data the status
   panel (Wave 2) and shell widget (Wave 3) need. Use `SetProperty`-style
   private setters via backing fields so changes raise `PropertyChanged`
   for free; also keep a `StatusChanged` event for non-MVVM consumers
   (tests, integration harness).

   ```csharp
   public HeyCyanBattery?     Battery    { get; private set; }
   public HeyCyanVersionInfo? Version    { get; private set; }
   public HeyCyanMediaCount?  MediaCount { get; private set; }
   public string?             MacAddress => Version?.MacAddress;

   public event EventHandler? StatusChanged;
   ```

4. **Scan / connect / disconnect** — thin pass-throughs to the session,
   plus the post-connect "fan-out" that pulls version/battery, syncs the
   clock, and starts each provider so the M11/M12/M13/M14 managers will
   prefer the glasses (priority order is established in M17 P1).

   ```csharp
   public Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(
       TimeSpan timeout, CancellationToken ct)
       => _session.ScanAsync(timeout, ct);

   public async Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct)
   {
       _lastDevice = device;
       await _session.ConnectAsync(device, ct);

       Version    = await _session.GetVersionAsync(ct);
       Battery    = await _session.GetBatteryAsync(ct);
       await _session.SyncTimeAsync(ct);

       await _camera.StartAsync(ct);
       await _mic.StartAsync(ct);
       await _speaker.StartAsync(ct);
       await _button.StartAsync(ct);

       StatusChanged?.Invoke(this, EventArgs.Empty);
   }

   public async Task DisconnectAsync(CancellationToken ct)
   {
       try { await _session.DisconnectAsync(ct); }
       finally { /* base + per-capability managers handle fallback */ }
   }
   ```

5. **Session → manager projection** — translate `HeyCyanState` to
   `GlassesConnectionState`, surface battery/media-count updates, and
   forward button gestures into the M14 `ButtonInputManager` via
   `HeyCyanButtonProvider.RaiseGesture`. `TransferMode` is reported as
   `Connected` because from the manager's POV the glasses are still
   present — only the live capture path is briefly unavailable.

   ```csharp
   private void OnSessionStateChanged(object? s, HeyCyanState state)
   {
       State = state switch
       {
           HeyCyanState.Disconnected => GlassesConnectionState.Disconnected,
           HeyCyanState.Scanning     => GlassesConnectionState.Scanning,
           HeyCyanState.Connecting   => GlassesConnectionState.Connecting,
           HeyCyanState.Connected    => GlassesConnectionState.Connected,
           HeyCyanState.TransferMode => GlassesConnectionState.Connected,
           _                         => GlassesConnectionState.Disconnected,
       };
       RaiseStateChanged();
   }

   private void OnBatteryUpdated(object? s, HeyCyanBattery b)
   {
       Battery = b;
       StatusChanged?.Invoke(this, EventArgs.Empty);
   }

   private void OnMediaCountUpdated(object? s, HeyCyanMediaCount c)
   {
       MediaCount = c;
       StatusChanged?.Invoke(this, EventArgs.Empty);
   }

   private void OnButtonPressed(object? s, HeyCyanButtonEvent e)
       => _button.RaiseGesture(e.Gesture);
   ```

6. **DI registration.** Update both `MauiProgram` partials. Register the
   manager twice so consumers can inject either the base abstraction
   (most code) or the concrete type (status panel, battery widget).

   ```csharp
   // Platforms/Android/MauiProgram.Android.cs
   services.AddSingleton<IHeyCyanGlassesSession, AndroidHeyCyanGlassesSession>();
   services.AddSingleton<HeyCyanCameraProvider>();
   services.AddSingleton<HeyCyanAudioInputProvider>();
   services.AddSingleton<HeyCyanAudioOutputProvider>();
   services.AddSingleton<HeyCyanButtonProvider>();
   services.AddSingleton<HeyCyanMediaTransfer>();

   services.AddSingleton<HeyCyanGlassesDeviceManager>();
   services.AddSingleton<GlassesDeviceManager>(sp =>
       sp.GetRequiredService<HeyCyanGlassesDeviceManager>());
   ```

   The iOS partial does the same with `IosHeyCyanGlassesSession` (P6).

7. **Unit tests** in `BodyCam.Tests/Services/Glasses/HeyCyan/`:
   - `HeyCyanGlassesDeviceManagerTests.Connect_PopulatesStatus`
   - `HeyCyanGlassesDeviceManagerTests.SessionStateChanged_MapsToGlassesState`
   - `HeyCyanGlassesDeviceManagerTests.BatteryUpdated_UpdatesBatteryAndRaisesStatusChanged`
   - `HeyCyanGlassesDeviceManagerTests.ButtonPressed_ForwardsToProvider`

   Use a fake `IHeyCyanGlassesSession` (xUnit + FluentAssertions, no
   CommunityToolkit.Mvvm — see `.github/copilot-instructions.md`).

## Verify

- [ ] `HeyCyanGlassesDeviceManager` compiles on `net10.0-android` and `net10.0-ios`
- [ ] Resolves from DI as both `GlassesDeviceManager` and the concrete type
- [ ] `ScanAsync` returns the BLE list from the session
- [ ] `ConnectAsync` populates `Battery`, `Version`, `MediaCount` and starts all four providers
- [ ] `StateChanged` fires on every QCSDK state transition (incl. `TransferMode → Connected`)
- [ ] `StatusChanged` fires on battery + media-count updates
- [ ] Button gestures from the session reach `ButtonInputManager` via `HeyCyanButtonProvider`
- [ ] Unit tests with a fake `IHeyCyanGlassesSession` pass
