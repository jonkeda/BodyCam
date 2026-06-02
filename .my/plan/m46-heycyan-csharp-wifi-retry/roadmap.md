# M46 Roadmap - HeyCyan C# WiFi Retry

## Goal

Recover a repeatable C# HeyCyan WiFi/media path using mobile-app oracle evidence
and Android platform APIs from .NET.

## Milestones

### Phase 1 - Mobile App Oracle Capture

- [ ] Install/run the official HeyCyan app on the Android phone with the glasses.
- [ ] Capture `adb logcat` around connect, enter transfer, peer discovery,
      connection, IP discovery, and media download.
- [ ] Capture network/socket state before and after transfer.
- [ ] Capture BLE write/notify frames if possible.
- [ ] Save timestamped artifacts under `captures/`.
- [ ] Write a short report with the exact observed sequence.

### Phase 1a - First Android Oracle Shot

- [x] Verify ADB device state.
- [x] Identify installed HeyCyan package and launchable activity.
- [x] Save baseline WiFi/P2P/network/logcat artifacts.
- [x] Attempt one official-app observation run.
- [x] Update the high-level log and create the next phase if the first run
      reveals a sharper plan.

### Phase 1b - Logged-In Location-On Oracle Run

- [x] Confirm phone is logged in to HeyCyan.
- [x] Confirm Android Location is enabled.
- [x] Save current logged-in UI and network state.
- [x] Clear logcat and run a clean official-app observation window.
- [x] Capture WiFi/P2P, BLE, app, screenshot, and UI XML after the action.
- [x] Update the high-level log and create the next phase if this run reveals a
      sharper plan.

### Phase 1c - Import Transfer Observation

- [x] Identify the Album `Import` action and visible new-content count.
- [x] Clear logcat before pressing Import.
- [x] Capture transfer logs and network/P2P state at intervals.
- [x] Determine whether Import uses WiFi P2P, normal WiFi, BLE-only, or cached
      media.
- [x] Update the high-level log and create the next phase if this run reveals a
      sharper plan.

### Phase 1d - Single-Photo Endpoint Probe

- [x] Create one fresh media item so import is repeatable.
- [x] Capture a clean single-photo import window.
- [x] Probe the active glasses IP and likely HTTP ports while P2P is alive.
- [x] Try to capture exact BLE write payload bytes.
- [x] Update Phase 2 with confirmed command and endpoint facts.

### Phase 1e - Direct Media Download Proof

- [x] Locate official-app imported media on Android storage.
- [x] Pull and verify at least one JPEG artifact.
- [x] Pull and verify at least one MP4 artifact.
- [x] Trigger a fresh P2P import and fetch `/files/{name}` directly.
- [x] Confirm mixed image/video entries in `media.config`.
- [x] Update Phase 4 with direct download facts.

### Phase 2 - BLE And WiFi Protocol Map

- [ ] Reconcile app-observed bytes with existing M36 commands.
- [ ] Confirm whether `02 01 04` is enough or needs pre/post commands.
- [ ] Confirm reset/cleanup command behavior.
- [ ] Confirm whether `02 03` actively polls IP readiness.
- [ ] Model Android P2P as phone group-owner plus glasses client IP, not
      `groupOwnerAddress` as the media host.
- [ ] Confirm media listing at `http://{glassesClientIp}/files/media.config`
      on port `80`.
- [ ] Document all response payload shapes with byte offsets.
- [ ] Add parser/build tests before touching runtime code.

### Phase 3 - Android C# WiFi Direct Connector

- [ ] Implement an Android C# probe using `WifiP2pManager`.
- [ ] Register P2P broadcast receiver in C#.
- [ ] Discover/select the glasses peer.
- [ ] Connect via WPS PBC from C#.
- [ ] Read P2P group state and resolve the glasses client IP.
- [ ] Bind HTTP traffic to the active P2P network where Android requires it.
- [ ] Save a connection report and timing metrics.

### Phase 4 - Media Download And Camera Provider Path

- [ ] Use the C# connector to call `/files/media.config`.
- [ ] Parse photo/video/audio file names.
- [ ] Download the newest image.
- [ ] Download MP4 samples when present.
- [ ] Reuse or simplify `HeyCyanMediaTransfer`.
- [ ] Prove `HeyCyanCameraProvider.CaptureFrameAsync()` through the C# path.

### Phase 5 - Real Hardware Test Harness

- [x] Add hardware-gated tests behind `BODYCAM_REAL_HEYCYAN_WIFI=1`.
- [x] Add env vars for BLE MAC/name and expected device model.
- [x] Add a host-side Android probe test for BLE command path, WiFi/P2P
      connect, media listing, image download, video download, and cleanup.
- [ ] Add a dedicated warm-reuse reconnect-count assertion.
- [x] Collect log artifacts automatically on failure.
- [x] Record pass/fail in M46 reports.
- [x] Save first green real-hardware run with pulled artifacts.

### Phase 6 - BodyCam Integration And UX Gate

- [ ] Add an experimental setting for C# HeyCyan WiFi transfer.
- [ ] Keep existing SDK/fallback path available until M46 is proven.
- [ ] Show clear diagnostics on the glasses settings page.
- [ ] Add telemetry for BLE command, WiFi P2P state, IP discovery, HTTP latency,
      and error category.
- [x] Make the existing `HeyCyanCameraProvider` use the proven capture-first C#
      transfer order on Android.

### Phase 7 - Windows C# Wi-Fi Direct Route

- [x] Align Windows video start/stop commands with the Android proof.
- [x] Add a Windows real-hardware probe matching the successful Android probe.
- [ ] Discover/connect through `WindowsWiFiDirectManager`.
- [ ] Validate candidate media IPs by fetching `/files/media.config`.
- [ ] Download valid JPEG and MP4 artifacts when fresh media exists.
- [ ] Wire the successful route through existing Windows session/transfer code.
- [ ] Prove `HeyCyanCameraProvider.CaptureFrameAsync()` on Windows, if the
      Windows Wi-Fi Direct route is reliable.

### Phase 7a - Windows Field Guide And First Implementation Slice

- [x] Create a Windows field guide that carries the Android-proven command and
      media sequence across sessions.
- [x] Align `WindowsHeyCyanGlassesSession.StopVideoAsync()` with
      `02 01 03`.
- [x] Route Windows production DI through `HeyCyanMediaTransfer` instead of the
      stored-image fallback.
- [x] Add focused regression coverage for the Windows transfer registration.
- [x] Run focused HeyCyan tests and a Windows build.

### Phase 7b - Windows Route Diagnostics And Candidate Selection

- [x] Create a focused phase doc for Windows route diagnostics.
- [x] Store matched WiFi Direct peer and endpoint-pair diagnostics.
- [x] Feed endpoint-pair remote hosts into transfer endpoint probing.
- [x] Include Android-proven P2P candidate hosts only after a P2P route exists.
- [x] Stop returning unvalidated transfer hosts after failed media.config
      probes.
- [x] Add candidate-selection unit tests.
- [x] Run focused HeyCyan tests and a Windows build.

### Phase 7c - Windows Artifact Probe

- [x] Create a focused phase doc for a Windows route artifact probe.
- [x] Expose last transfer candidate/validated IP diagnostics for real tests.
- [x] Add a hardware-gated Windows route probe test.
- [x] Save a timestamped JSON report and downloaded media artifacts.
- [x] Compile/run the real-test project with hardware skipped.

### Phase 7d - Windows Route Boundary And Pivot

- [x] Preserve full WiFi Direct attempt diagnostics in the artifact JSON.
- [x] Try pair-first Windows WiFi Direct before the no-prepair path.
- [x] Pass the BLE-provided password into custom pairing if Windows requests a
      PIN.
- [x] Rediscover the WiFi Direct peer between failed attempts.
- [x] Capture Windows driver/WLAN AutoConfig diagnostics.
- [x] Document the current Intel BE200 route boundary and pivot options.

### Phase 8 - Remove Android Vendor AAR BLE Bridge

- [x] Inventory the AAR APIs currently used by `HeyCyanSdkBridge`.
- [x] Capture direct GATT UUIDs, characteristics, MTU, notify, and write
      behavior.
- [x] Build a minimal Android C# BLE probe for scan/connect/write/notify.
- [x] Implement a direct C# BLE bridge under the existing session abstraction.
- [x] Pass version, battery, photo, video, and transfer commands without the
      AAR.
- [x] Remove `BodyCam.HeyCyan.Android.Bindings` and
      `glasses_sdk_20250723_v01.aar` from the Android app.
- [x] Re-run the Phase 5 real-hardware harness without the AAR.

## Success Criteria

- Android C# connects to the glasses WiFi/P2P transport without the HeyCyan app
  running.
- C# downloads `/files/media.config`.
- C# downloads one JPEG and verifies it starts with `FF D8`.
- `HeyCyanCameraProvider.CaptureFrameAsync()` returns that JPEG on Android.
- The path is covered by hardware-gated tests and a timestamped report.
- Phase 8, if implemented, removes the Android vendor AAR while preserving the
  Phase 5 green hardware path.

## Stop Criteria

Stop or pivot if:

- the glasses require signed/encrypted vendor messages we cannot reproduce
  without proprietary code;
- Android blocks P2P operations from our app despite correct permissions;
- the official app works only by using private system permissions unavailable to
  BodyCam;
- repeated captures show firmware variants with incompatible BLE/WiFi flows.
