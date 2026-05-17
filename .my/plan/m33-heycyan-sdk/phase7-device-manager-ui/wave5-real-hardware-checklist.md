# M33 Phase 7 — Wave 5: Real-hardware acceptance checklist

**Parent:** [../phase7-device-manager-ui.md](../phase7-device-manager-ui.md)
**Siblings:** [wave1-heycyan-device-manager.md](wave1-heycyan-device-manager.md) ·
[wave2-glasses-page.md](wave2-glasses-page.md) ·
[wave3-shell-battery-widget.md](wave3-shell-battery-widget.md) ·
[wave4-fallback-verification.md](wave4-fallback-verification.md)

## Goal

Run the M17 milestone exit-criteria checklist with HeyCyan glasses as the
backing device. This is the **milestone-level acceptance test for M33**:
when every checkbox below is ticked on both Android and iOS, the milestone
is complete. Each row maps to a specific M33 exit criterion (table at the
bottom), so this wave doubles as the M33 sign-off document.

## Steps

1. **Pre-flight.** Before starting:
   - Glasses charged ≥ 60 %, firmware version recorded.
   - Phone unpaired from any prior glasses (clear Bluetooth cache on Android).
   - App built in Release; `HEYCYAN_E2E=1` env var set for the harness in
     Wave 4 if running it alongside.
   - Create folder `TestResults/m33-phase7/<yyyy-mm-dd>/` to hold logs,
     screenshots, and the filled-in tables.

2. **Manual test plan (run on Android first, then iOS).** Record results in
   `TestResults/m33-phase7/<yyyy-mm-dd>/wave5-checklist-<platform>.md`.

   | # | Step | Expected | Pass? |
   |---|------|----------|-------|
   | 1 | Cold-boot phone, open BodyCam | Glasses widget hidden in shell | [ ] |
   | 2 | `GlassesPage` → Scan | HeyCyan device appears with name + MAC + RSSI | [ ] |
   | 3 | Tap device → Connect | Status panel populated (battery, MAC, fw, hw); shell widget shows battery | [ ] |
   | 4 | Start a Realtime conversation | Mic = glasses HFP, speaker = glasses A2DP, camera = `HeyCyanCameraProvider` | [ ] |
   | 5 | Single-tap glasses button | Configured action fires (default: start/stop conversation) | [ ] |
   | 6 | Double-tap glasses button | Photo captured; `VisionAgent` receives JPG round-trip | [ ] |
   | 7 | Long-press glasses button | Conversation ends cleanly | [ ] |
   | 8 | Power off glasses mid-call | Fallback within 2 s (Wave 4 latency table); call continues on phone | [ ] |
   | 9 | Power glasses back on | Auto-reconnect; all four providers re-bind without user action | [ ] |
   | 10 | Disconnect manually from `GlassesPage` | Returns to scan list; shell widget hidden | [ ] |
   | 11 | (Optional, P5 only) Open recorded media gallery | OPUS / MP4 / JPG files download via WiFi-Direct | [ ] |

3. **Status panel field check (during step 3).**

   - Battery %: matches the SDK's `GetBatteryAsync` result within ±1 %.
   - MAC: matches the BLE address shown in the OS Bluetooth settings.
   - Hardware / Firmware: non-empty, matches the values logged by
     `LargeDataHandler.getVersion` / `QCSDKManager` version callback.
   - Photos / Videos / Audio: increments **live** as steps 6, 7, and any
     audio command issued during step 5 produce new files. Watch the
     numbers tick on-screen.

4. **Battery widget freshness check (during steps 3–9).**

   - Steady-state update within 1 s of `BatteryUpdated`.
   - Charging bolt appears within 1 s of placing glasses on the cradle.
   - Low-battery red tint appears at ≤ 15 % when not charging
     (Wave 3 affordance).

5. **Integration test harness (gates the manual run).** Re-run the
   harness from Wave 4 plus the longer end-to-end test below. Both
   tests must be green for the same hardware/session as the manual
   checklist run.

   ```csharp
   // src/BodyCam.IntegrationTests/Glasses/HeyCyanEndToEndTests.cs
   [Trait("Category", "RealHardware")]
   [Collection("HeyCyanHardware")]
   public class HeyCyanEndToEndTests
   {
       [SkippableFact]
       public async Task Connect_Disconnect_Reconnect_FallsBackAndRestores()
       {
           Skip.IfNot(Environment.GetEnvironmentVariable("HEYCYAN_E2E") == "1");

           var mgr = TestHost.Resolve<HeyCyanGlassesDeviceManager>();
           var devices = await mgr.ScanAsync(TimeSpan.FromSeconds(10), default);
           devices.Should().NotBeEmpty();

           await mgr.ConnectAsync(devices[0], default);
           mgr.State.Should().Be(GlassesConnectionState.Connected);
           mgr.Battery!.Percentage.Should().BeGreaterThan(0);
           mgr.Version!.MacAddress.Should().NotBeNullOrEmpty();
           mgr.MediaCount.Should().NotBeNull();

           // Disconnect + verify fallback (cross-checked against Wave 4).
           await mgr.DisconnectAsync(default);
           await Task.Delay(2_500);
           TestHost.Resolve<CameraManager>().Active
               .Should().BeOfType<PhoneCameraProvider>();

           // Reconnect manually (auto-reconnect is exercised in Wave 4).
           await mgr.ConnectAsync(devices[0], default);
           mgr.State.Should().Be(GlassesConnectionState.Connected);
       }
   }
   ```

6. **Sign-off artifacts.** Commit the following under
   `TestResults/m33-phase7/<yyyy-mm-dd>/`:
   - `wave5-checklist-android.md` (filled-in table 11/11)
   - `wave5-checklist-ios.md` (filled-in table 11/11)
   - `wave4-fallback.md` with the latency table from Wave 4
   - `logcat-android.log`, `console-ios.log`
   - `screenshot-status-panel.png`, `screenshot-shell-widget.png`
   - `device-info.json` (firmware/hardware/mac per platform)

7. **Hand-off.** When all checkboxes are ticked on both platforms, mark
   M33 complete in `../overview.md` and link this folder from the M33
   exit-criteria section.

## Phase 7 → M33 exit-criteria mapping

| M33 exit criterion | Covered by |
|--------------------|------------|
| Photo via `HeyCyanCameraProvider` round-trips through `VisionAgent` | Step 6 |
| BT live mic + speaker route through glasses during a conversation  | Step 4 |
| Glasses button (tap/double/long) triggers configured actions       | Steps 5–7 |
| Auto-fallback to phone camera + mic + speaker on disconnect        | Step 8 + [wave4-fallback-verification.md](wave4-fallback-verification.md) |
| Battery + firmware shown in status panel                           | Step 3 + [wave2-glasses-page.md](wave2-glasses-page.md) + [wave3-shell-battery-widget.md](wave3-shell-battery-widget.md) |
| M17 exit criteria pass end-to-end against HeyCyan hardware         | Entire table (steps 1–11) |
| (Optional) Recorded `.opus` voice notes import into M16 dictation  | Step 11 |

## Verify

- [ ] All 11 manual steps pass on **Android** real hardware
- [ ] All 11 manual steps pass on **iOS** real hardware
- [ ] `HeyCyanEndToEndTests` green when `HEYCYAN_E2E=1` on both platforms
- [ ] `HeyCyanFallbackTests` (Wave 4) green when `HEYCYAN_E2E=1`
- [ ] Test results, logs, and screenshots archived under `TestResults/m33-phase7/<yyyy-mm-dd>/`
- [ ] No crashes in `logcat` / Console during the full sequence on either platform
- [ ] M33 exit-criteria mapping table fully ticked in `../overview.md`
