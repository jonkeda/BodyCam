# M33 Phase 7 — Wave 4: End-to-end fallback verification

**Parent:** [../phase7-device-manager-ui.md](../phase7-device-manager-ui.md)
**Siblings:** [wave1-heycyan-device-manager.md](wave1-heycyan-device-manager.md) ·
[wave2-glasses-page.md](wave2-glasses-page.md) ·
[wave3-shell-battery-widget.md](wave3-shell-battery-widget.md) ·
[wave5-real-hardware-checklist.md](wave5-real-hardware-checklist.md)

## Goal

Prove that auto-fallback survives a real HeyCyan disconnect mid-conversation.
**No new fallback logic is written here.** M17 Phase 1 already implemented:

- `GlassesDeviceManager` raises `Disconnected` when any glasses provider drops.
- `CameraManager`, `AudioInputManager`, `AudioOutputManager`, and
  `ButtonInputManager` all re-pick the next provider by priority.
- `GlassesDeviceManager.AutoReconnectAsync` retries 3× with exponential backoff.

This wave wires the HeyCyan-specific hooks (Wave 1 already projects
`HeyCyanState.Disconnected`), then *verifies* the chain end-to-end with a
scripted manual test, an automated harness, and a logged latency budget.

## Steps

1. **Confirm the projection path.** Re-read Wave 1's `OnSessionStateChanged`
   and ensure `HeyCyanState.Disconnected` maps to
   `GlassesConnectionState.Disconnected` and calls `RaiseStateChanged()`.
   The base class then raises each glasses provider's `Disconnected`
   event, which is the trigger M17's per-capability managers listen for.
   If anything is missing, fix in Wave 1 — *do not* duplicate logic here.

2. **Add a structured fallback log.** In
   `HeyCyanGlassesDeviceManager.OnSessionStateChanged`, when transitioning
   to `Disconnected`, emit a single info-level log line that downstream
   tests can grep:

   ```csharp
   if (state == HeyCyanState.Disconnected)
   {
       _log.LogInformation(
           "HeyCyan disconnected — fallback initiated (lastDevice={Mac})",
           _lastDevice?.Address ?? "<unknown>");
   }
   ```

3. **Notification toast.** Verify the M17 `INotificationService` toast
   ("Glasses disconnected — switched to phone audio") fires once per
   disconnect — *not* once per provider. If the M17 implementation
   currently fires per-provider, raise it from
   `HeyCyanGlassesDeviceManager` instead and gate the per-provider path.

4. **Scripted test plan (real hardware, both Android + iOS).** Document
   each step in a markdown file under
   `TestResults/m33-phase7/<yyyy-mm-dd>/wave4-fallback.md` as the test
   runs. Capture `logcat` (Android) or Console (iOS) per step.

   1. Pair glasses; open `GlassesPage`; Connect. Confirm shell widget
      shows live battery %.
   2. Open the Voice / Realtime page; start a conversation.
      `VoiceAgent` should be using:
      - `AudioInputManager.Active` = `HeyCyanAudioInputProvider` (HFP mic)
      - `AudioOutputManager.Active` = `HeyCyanAudioOutputProvider` (A2DP)
      - vision frame source = `HeyCyanCameraProvider`
   3. Power off glasses **OR** walk out of BLE range
      (≥ 10 m through a wall is reliable). **Do not** disconnect via UI —
      we are testing the unexpected-disconnect path.
   4. Start a stopwatch the moment the BLE link drops (visible in
      `logcat` as `STATE_DISCONNECTED`). Within **≤ 2 s**:
      - `AudioInputManager.Active` → `PlatformMicProvider`
      - `AudioOutputManager.Active` → `PlatformSpeakerProvider`
      - `CameraManager.Active` → `PhoneCameraProvider`
      - `ButtonInputManager` re-binds to keyboard / phone-button provider
      - The Realtime conversation **must not drop** — audio simply re-routes.
   5. Toast/notification appears: "Glasses disconnected — switched to phone audio".
   6. Power glasses back on. Auto-reconnect (M17, 3 attempts, exponential
      backoff) restores all four providers without user intervention.
      Shell widget should reappear and reach steady-state battery within
      ~30 s of reconnect.

5. **Latency budget.** Record the measured fallback latencies in a
   simple table per run; the conversation must remain audible
   throughout.

   | Capability | Target | Measured |
   |------------|--------|----------|
   | Camera fallback     | ≤ 2 s |   |
   | Mic fallback        | ≤ 2 s |   |
   | Speaker fallback    | ≤ 2 s |   |
   | Button re-bind      | ≤ 1 s |   |
   | Auto-reconnect      | ≤ 30 s |   |

6. **Automated harness (best-effort).** Add an integration test that runs
   on real hardware behind an env-var gate. Live audio cannot be asserted
   from a test runner, but provider swap **can** be — and that's the
   actual contract the rest of the stack relies on.

   ```csharp
   // src/BodyCam.IntegrationTests/Glasses/HeyCyanFallbackTests.cs
   [Trait("Category", "RealHardware")]
   [Collection("HeyCyanHardware")]
   public class HeyCyanFallbackTests
   {
       [SkippableFact]
       public async Task Disconnect_FallsBackToPhoneProviders()
       {
           Skip.IfNot(Environment.GetEnvironmentVariable("HEYCYAN_E2E") == "1");

           var mgr     = TestHost.Resolve<HeyCyanGlassesDeviceManager>();
           var camera  = TestHost.Resolve<CameraManager>();
           var mic     = TestHost.Resolve<AudioInputManager>();
           var speaker = TestHost.Resolve<AudioOutputManager>();

           var devices = await mgr.ScanAsync(TimeSpan.FromSeconds(10), default);
           devices.Should().NotBeEmpty();
           await mgr.ConnectAsync(devices[0], default);

           camera.Active.Should().BeOfType<HeyCyanCameraProvider>();
           mic.Active.Should().BeOfType<HeyCyanAudioInputProvider>();
           speaker.Active.Should().BeOfType<HeyCyanAudioOutputProvider>();

           await mgr.DisconnectAsync(default);
           await Task.Delay(2_500); // M17 fallback window

           camera.Active.Should().BeOfType<PhoneCameraProvider>();
           mic.Active.Should().BeOfType<PlatformMicProvider>();
           speaker.Active.Should().BeOfType<PlatformSpeakerProvider>();
       }
   }
   ```

7. **Failure modes to watch for during the run** (open a bug + link
   back here if any are observed):
   - Realtime audio session breaks instead of re-routing → check
     `IAudioInputProvider.Disconnected` ordering vs. session restart.
   - Provider swap exceeds 2 s → `M17` priority list missing phone
     fallback, or `StopAsync` blocking on a BLE timeout.
   - Auto-reconnect never fires → `HeyCyanGlassesDeviceManager` lost
     `_lastDevice` reference (Wave 1 stores it on `ConnectAsync`).

## Verify

- [ ] Camera fallback ≤ 2 s, no agent stall
- [ ] Mic fallback ≤ 2 s, agent keeps hearing the user
- [ ] Speaker fallback ≤ 2 s, agent reply remains audible on phone
- [ ] Button fallback re-binds within 1 s
- [ ] Auto-reconnect restores glasses without manual scan (≤ 30 s)
- [ ] Toast notification fires exactly once per disconnect
- [ ] No exceptions in `logcat` / Console during the disconnect/reconnect cycle
- [ ] `HeyCyanFallbackTests` passes when `HEYCYAN_E2E=1`
- [ ] Latency table filled in and committed under `TestResults/m33-phase7/`
