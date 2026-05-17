# M33 Phase 2 — Wave 4: `CameraManager` Integration & Settings UI

**Parent:** [`../phase2-camera-provider.md`](../phase2-camera-provider.md)
**Siblings:** [wave1](wave1-wifi-p2p-http-client.md) · [wave2](wave2-heycyan-media-transfer.md) · [wave3](wave3-heycyan-camera-provider.md) · [wave5](wave5-latency-benchmarks.md)

## Goal

Wire `HeyCyanCameraProvider` (Wave 3) into the M11 `CameraManager` so the
rest of the app gets glasses frames automatically when the glasses are
connected, and falls back to the phone camera when they are not. Surface
state and a "Test Capture" button on the Settings page so users can
validate the end-to-end pipeline on real hardware without invoking the
full agent flow.

## Steps

1. **Confirm the M11 selection contract.** `CameraManager` (from M11
   Phase 1) already accepts an `IEnumerable<ICameraProvider>` and
   exposes an `ActiveProvider` property. Re-read
   [src/BodyCam/Services/Camera/CameraManager.cs](../../../../src/BodyCam/Services/Camera/CameraManager.cs)
   and confirm the selection-rule extension point. If a strategy
   pattern is not yet in place, add a simple
   `ICameraProviderSelector` whose default implementation prefers the
   first `IsAvailable` provider.

2. **Add the HeyCyan-aware selection rule.** Create
   [src/BodyCam/Services/Camera/HeyCyanCameraSelector.cs](../../../../src/BodyCam/Services/Camera/HeyCyanCameraSelector.cs):

   ```csharp
   internal sealed class HeyCyanCameraSelector : ICameraProviderSelector
   {
       private readonly IHeyCyanGlassesSession? _session;

       public ICameraProvider Select(IReadOnlyList<ICameraProvider> providers)
       {
           // Prefer glasses when the session is Connected (or warm in TransferMode).
           if (_session?.State is HeyCyanState.Connected or HeyCyanState.TransferMode)
           {
               var glasses = providers.FirstOrDefault(p => p.ProviderId == "heycyan-glasses");
               if (glasses is { IsAvailable: true }) return glasses;
           }
           // Otherwise: first available, with phone camera as the natural fallback.
           return providers.First(p => p.IsAvailable);
       }
   }
   ```

   Register on Android only:

   ```csharp
   builder.Services.AddSingleton<ICameraProviderSelector, HeyCyanCameraSelector>();
   ```

3. **Hot-swap on session state change.** `CameraManager` MUST re-run
   selection when the HeyCyan session transitions
   `Connected ↔ Disconnected`. Subscribe in `CameraManager`'s
   constructor (Android-only; gate behind a `nullable` injected
   `IHeyCyanGlassesSession?`):

   ```csharp
   if (_heyCyanSession is not null)
   {
       _heyCyanSession.StateChanged += (_, state) =>
       {
           _log.LogInformation("HeyCyan state changed to {State}; reselecting camera", state);
           ReselectActiveProvider();
       };
   }
   ```

   Disconnect MUST revert to the phone camera within one frame request
   (no in-flight capture stuck on the dead provider).

4. **Stop in-flight capture cleanly on swap.** When `ReselectActiveProvider`
   demotes the HeyCyan provider, cancel any pending
   `CaptureFrameAsync` via the manager-owned `CancellationTokenSource`
   and dispose the warm transfer session through
   `IHeyCyanMediaTransfer.ExitAsync`.

5. **Settings page entry.** Edit
   [src/BodyCam/Views/SettingsPage.xaml](../../../../src/BodyCam/Views/SettingsPage.xaml)
   and its viewmodel
   [src/BodyCam/ViewModels/SettingsViewModel.cs](../../../../src/BodyCam/ViewModels/SettingsViewModel.cs).
   Add a "Glasses Camera" group bound to a new
   `GlassesCameraSectionViewModel` that exposes:

   - `Status` (string): "Disconnected", "Connected", "Capturing…",
     "Transfer mode (warm)", "Error: …".
   - `IsTestCaptureEnabled` (bool): mirrors `HeyCyanCameraProvider.IsAvailable`.
   - `TestCaptureCommand` (`AsyncRelayCommand` per
     [`.github/copilot-instructions.md`](../../../../.github/copilot-instructions.md)):
     calls `CaptureFrameAsync`, decodes to `ImageSource.FromStream`,
     and shows the elapsed milliseconds.
   - `LastCaptureImage` (`ImageSource?`).
   - `LastCaptureLatencyMs` (long?).

   Use `SetProperty(ref _field, value)` for all setters. Do NOT raise
   `PropertyChanged` manually.

6. **Wire status text to session events.** Subscribe to
   `IHeyCyanGlassesSession.StateChanged`,
   `BatteryUpdated`, and `MediaCountUpdated` and fold them into the
   `Status` string. Always unsubscribe in
   `GlassesCameraSectionViewModel.Dispose` (the page lifetime).

7. **Test Capture command implementation.**

   ```csharp
   public AsyncRelayCommand TestCaptureCommand { get; }

   private async Task TestCaptureAsync(CancellationToken ct)
   {
       var sw = Stopwatch.StartNew();
       try
       {
           Status = "Capturing…";
           var jpg = await _provider.CaptureFrameAsync(ct);
           sw.Stop();
           LastCaptureImage = ImageSource.FromStream(() => new MemoryStream(jpg));
           LastCaptureLatencyMs = sw.ElapsedMilliseconds;
           Status = $"Captured {jpg.Length:N0} bytes in {sw.ElapsedMilliseconds} ms";
       }
       catch (Exception ex)
       {
           Status = $"Error: {ex.Message}";
           _log.LogError(ex, "Test capture failed");
       }
   }
   ```

8. **Document the latency contract in M11 docs.** Add a section
   "Glasses camera latency" to
   [docs/services.md](../../../../docs/services.md) (or the M11
   docs page if one exists), explicitly stating:

   - Cold capture (group formation included): 2-5 s.
   - Warm capture (within 8 s of last call): 700 ms-1.5 s.
   - Phone camera baseline: <50 ms.
   - `StartStreamAsync` is unsupported on `heycyan-glasses` —
     consumers must use `CaptureFrameAsync` exclusively.

9. **Integration test.** Add
   [src/BodyCam.IntegrationTests/Camera/HeyCyanCameraSelectionTests.cs](../../../../src/BodyCam.IntegrationTests/Camera/HeyCyanCameraSelectionTests.cs)
   using fake session + fake providers:

   - `WhenGlassesConnected_ActiveProviderIsHeyCyan`.
   - `WhenGlassesDisconnect_ActiveProviderRevertsToPhoneCamera`.
   - `Reselection_CancelsInFlightCapture`.

## Verify

- [ ] `CameraManager.ActiveProvider.ProviderId == "heycyan-glasses"`
      after `IHeyCyanGlassesSession.State` becomes `Connected`.
- [ ] Setting state to `Disconnected` reverts `ActiveProvider` to the
      phone camera within one frame request (no exception thrown to
      the caller).
- [ ] In-flight `CaptureFrameAsync` is cancelled when the session
      drops mid-call; `IHeyCyanMediaTransfer.ExitAsync` is invoked
      exactly once.
- [ ] Settings page "Glasses Camera" section displays current state
      (`Connected` / `Disconnected` / battery %) and updates live on
      session events.
- [ ] "Test Capture" button is disabled when `IsAvailable` is false.
- [ ] "Test Capture" on real hardware renders a valid JPG and shows
      a non-zero latency in ms.
- [ ] Page navigation away from Settings unsubscribes all session
      event handlers (no leaked observers).
- [ ] Latency contract documented in M11 docs alongside the phone
      camera baseline.
- [ ] Selector falls back gracefully when registered on a platform
      without `IHeyCyanGlassesSession` (no DI resolution failure).
