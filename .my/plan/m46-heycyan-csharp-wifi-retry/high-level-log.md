# M46 High-Level Log

## 2026-05-31 - Phase 1a Started

- Phone connected over USB.
- User reports HeyCyan app is installed and glasses are switched on.
- Created Phase 1a to take the first Android oracle shot.
- First objective: identify device/package/log tags before changing app code.

## 2026-05-31 - Phase 1a Result

- ADB detected `SM_S931B`.
- Official app package is `com.glasssutdio.wear`.
- Launch activity is `.home.activity.SplashQcActivity`.
- App version is `1.0.121_20260529`.
- Locked/AOD phone prevented visible app interaction; app reached `LoginActivity`
  behind `NotificationShade`.
- Android Location was off, and Bluetooth logs reported `Location is off`.
- Created Phase 1b for logged-in, location-on capture.

## 2026-05-31 - Phase 1b Started

- User logged into HeyCyan.
- User enabled Android Location.
- ADB confirmed `cmd location is-location-enabled` returns `true`.
- Next objective: capture the logged-in official-app flow and look for BLE,
  WiFi/P2P, IP, and media endpoint evidence.

## 2026-05-31 - Phase 1b Result

- HeyCyan home screen showed `M01 Pro_E6C9` connected.
- Album tab opened successfully.
- Album UI showed `There are 64 new contents available to import to your
  smartphone.`
- Opening Album caused two BLE GATT writes of length 8 and GATT notifications,
  but no active WiFi P2P group.
- `p2p0` remained down; phone stayed on normal WiFi `jobaboe`
  `192.168.1.67/24`.
- Created Phase 1c to press `Import` and capture the actual transfer path.

## 2026-05-31 - Phase 1c Result

- Pressing Album `Import` immediately produced one BLE GATT write with length
  `10`.
- The phone formed a WiFi Direct group as group owner on `p2p-wlan0-0`.
- Group owner IP was `192.168.49.1/24`.
- The glasses joined as `M01 Pro_D879B87FE6C9` at `192.168.49.183`.
- The P2P group used `DIRECT-Vr-daan's S25`, channel frequency `2437`, WPS
  `PBC`, and connection type `REINVOKE`.
- The import UI showed progress (`22/64`) and speed (`38.2 KB/s`).
- Android media scanner events fired during the active P2P window.
- The P2P session disconnected after about `49.8s`.
- Logcat did not expose the exact BLE payload bytes or the HeyCyan HTTP URL.
- Created Phase 1d to make a single-photo transfer and probe endpoints while
  P2P is alive.

## 2026-05-31 - Phase 1d Result

- `Take photo` produced a length-`9` BLE write but did not restore the import
  banner.
- A short `Record video` action produced a length-`9` BLE write and restored an
  Album banner for `1` new item.
- One-item imports produced:
  - transfer BLE write length `10`;
  - follow-up BLE write length `8`;
  - disconnect/cleanup BLE write length `8`.
- One-item P2P windows were much shorter than the backlog import, around
  `9.5s`.
- The phone remained WiFi Direct group owner at `192.168.49.1`.
- The glasses client settled at `192.168.49.183`; logcat also briefly showed
  `192.168.49.200`.
- Confirmed the media endpoint:
  - `GET http://192.168.49.183/files/media.config`;
  - port `80`;
  - response `HTTP/1.1 200 OK`;
  - body `20260531184722907.mp4`.
- Directory listing at `/files/` timed out.
- Exact BLE payload bytes are still not visible from logcat and must be handled
  in Phase 2.

## 2026-05-31 - Phase 1e Result

- Found official-app imported media under
  `/sdcard/Android/data/com.glasssutdio.wear/files/DCIM_1/`.
- Pulled and verified a cached/imported original JPEG:
  - `20260531183944038.jpg`;
  - `1,931,742` bytes;
  - dimensions `6560x4928`;
  - JPEG signature `FF D8`.
- Pulled and verified a cached/imported MP4:
  - `20260531184239920.mp4`;
  - `10,522,957` bytes;
  - MP4 `ftypisom` signature.
- Triggered a fresh import with `3` new contents.
- Directly downloaded from the glasses over P2P:
  - `20260531184722907.mp4`;
  - `20260531190723036.jpg`;
  - `20260531190726933.mp4`.
- Direct HTTP `media.config` body was newline-delimited and mixed image/video
  entries.
- JPEG direct download was valid at `3280x2464`.
- MP4 direct downloads had valid `ftypisom` signatures.
- MP4 responses advertised `Content-Type: text/plain`; implementation should
  validate by extension/signature, not content type alone.
- Phase 4 can now target real image and video downloads, not only listing.

## 2026-05-31 - Phase 4f Started

- Reviewed the official and alternative HeyCyan flows against the C# probe.
- Confirmed `02 01 04` is the file/media import command to keep.
- Confirmed `02 01 14 01` belongs to realtime preview/RTSP behavior and is not
  the right HTTP media import trigger.
- Updated the C# P2P path to start discovery before the BLE command and to
  connect only to likely glasses peers.
- Removed the broad peer fallback after a bad run connected to a Samsung TV.
- Latest C# probe correctly formed Wi-Fi Direct with
  `M01 Pro_D879B87FE6C9/60:c2:2a:1a:b6:1b`.
- Phone was group owner at `192.168.49.1`; Android link properties showed a
  `192.168.49.0/24` P2P route.
- Current blocker: HTTP probes to the glasses IP failed with `ENONET` even
  after process binding.
- Added Phase 4f to test a targeted Android network refresh/fallback around
  stale P2P `Network.OpenConnection` routing.

## 2026-05-31 - Phase 4f Result

- Targeted routing patch moved the failure from Android `ENONET` to a real
  port-80 connection against `192.168.49.183`.
- Shell `ping` to `192.168.49.183` worked during the C#-formed P2P window.
- Shell `curl /` returned `HTTP 400 Invalid Request`, proving the glasses HTTP
  service was reachable.
- `/files/media.config` hung/empty-replied when there was no fresh importable
  media in the current C# flow.
- Added probe options to capture fresh media before transfer.
- Implemented C# video start/stop and found a protocol bug:
  - start video is `02 01 02`;
  - stop video is `02 01 03`;
  - `02 01 0b` is not the right video stop command for this flow.
- Final probe run `20260531-223525` succeeded end-to-end:
  - captured fresh photo;
  - recorded and stopped fresh video;
  - formed P2P with `M01 Pro_D879B87FE6C9`;
  - fetched `/files/media.config`;
  - listed `6` media entries;
  - downloaded `20260531223521015.jpg`, `477,137` bytes, valid JPEG
    `3280x2464`;
  - downloaded `20260531223525915.mp4`, `6,171,209` bytes, valid `ftypisom`
    MP4.
- Pulled artifacts to
  `.my/plan/m46-heycyan-csharp-wifi-retry/captures/phase-4f-20260531-bodycam-csharp-fresh-media-success/20260531-223525/`.

## 2026-05-31 - Phase 7 Planned

- Added a Windows route plan after the Android C# proof.
- The Windows plan reuses existing `WindowsWiFiDirectManager`,
  `WindowsHeyCyanGlassesSession`, `HeyCyanMediaTransfer`, and
  `HeyCyanCameraProvider` instead of creating a new camera path.
- Main feasibility risk is Windows Wi-Fi Direct routing and pairing behavior,
  not the HeyCyan BLE/media protocol.

## 2026-05-31 - Phase 6 Partial Implementation

- Kept the existing `HeyCyanCameraProvider` instead of adding a second camera
  provider.
- Changed `CaptureFrameAsync()` to trigger the photo first, wait for file
  finalization, then list/download through `HeyCyanMediaTransfer`.
- Removed the pre-capture `/files/media.config` listing that could cold-start
  transfer mode before fresh media existed.
- Added test coverage for the `photo` then `list` ordering.
- Focused HeyCyan tests passed: `46` passed.
- Full test suite passed: `1166` passed, `1` skipped.
- Android build passed with `0` errors.

## 2026-05-31 - Phase 5 Harness Implementation

- Added an opt-in xUnit hardware test gated by `BODYCAM_REAL_HEYCYAN_WIFI=1`.
- The host-side test launches the installed Android BodyCam app with the
  `com.companyname.bodycam.HEYCYAN_PROBE` action through `adb`.
- The Android probe creates fresh photo/video media, lists `media.config`, pulls
  the newest JPEG and MP4, and writes `probe-result.json`.
- The host-side test pulls the Android probe folder and asserts valid JPEG and
  MP4 signatures.
- Diagnostics are captured automatically:
  - full logcat;
  - HeyCyan-filtered logcat;
  - `ip addr`;
  - `ip route`;
  - `dumpsys wifi p2p`;
  - `dumpsys connectivity`.
- Artifact target:
  `.my/plan/m46-heycyan-csharp-wifi-retry/captures/phase-5-real-hardware-test-harness/{timestamp}/`.
- Normal test run result after adding the harness: `1166` passed, `2` skipped.

## 2026-05-31 - Phase 5 First Green Hardware Run

- First gated run failed because Android could not resolve the implicit custom
  probe action by package alone.
- Fixed the harness to resolve the launcher activity via
  `cmd package resolve-activity` and start that explicit component with the
  probe action.
- Gated hardware test then passed: `1` passed, `0` failed, duration about `38s`.
- Run folder:
  `.my/plan/m46-heycyan-csharp-wifi-retry/captures/phase-5-real-hardware-test-harness/20260531-230532/`.
- Device: `M01 Pro_E6C9` / `D8:79:B8:7F:E6:C9`.
- Version: `AM01C_V2.0/AM01C_2.00.03_250718`.
- WiFi firmware: `WIFIAM01C_1.00.15_2507111740`.
- Media list returned `6` entries.
- Downloaded fresh photo:
  - `20260531230515014.jpg`;
  - `329,665` bytes;
  - valid JPEG signature `FF D8`.
- Downloaded fresh video:
  - `20260531230520943.mp4`;
  - `5,164,495` bytes;
  - valid MP4 signature `ftypisom`.

## 2026-05-31 - Phase 8 Planned

- Clarified that the current Android C# success covers Wi-Fi/media transfer,
  while BLE/control still uses the vendor AAR binding.
- Added Phase 8 to remove `BodyCam.HeyCyan.Android.Bindings` and
  `glasses_sdk_20250723_v01.aar`.
- Phase 8 will replace `HeyCyanSdkBridge` with a direct C# Android BLE bridge
  using scan/connect/GATT write/notify APIs.
- Acceptance requires the Phase 5 hardware harness to pass after the AAR is
  removed.

## 2026-05-31 - Phase 8 Implementation

- Replaced the Android `HeyCyanSdkBridge` vendor-AAR wrapper with a direct C#
  BLE implementation.
- Direct bridge now scans likely HeyCyan names, connects with Android GATT,
  discovers the serial-port service, enables notifications through CCCD, writes
  command chunks, and maps notifications into the existing session core.
- Confirmed direct BLE UUIDs:
  - service `de5bf728-d711-4e47-af26-65e3012a5dc7`;
  - notify `de5bf729-d711-4e47-af26-65e3012a5dc7`;
  - write `de5bf72a-d711-4e47-af26-65e3012a5dc7`.
- Removed the Android production project reference to
  `BodyCam.HeyCyan.Android.Bindings`.
- Added direct-BLE protocol tests and session tests for video start/stop,
  transfer IP notify handling, and SDK-compatible device-config payload.
- Added a transfer activation/keepalive pulse using opcode `0x47` payload
  `01 00`, matching the decompiled Android SDK `wearFunctionSupport` call.

## 2026-05-31 - Phase 8 Green Hardware Run

- Full unit suite passed: `1171` passed, `2` skipped.
- Android build passed for `net10.0-android`: `0` errors.
- Installed the no-AAR Android APK on `SM-S931B`.
- Gated hardware test passed: `1` passed, `0` failed.
- Run folder:
  `.my/plan/m46-heycyan-csharp-wifi-retry/captures/phase-5-real-hardware-test-harness/20260531-234952/`.
- Device: `M01 Pro_E6C9` / `D8:79:B8:7F:E6:C9`.
- Version: `AM01C_V2.0/AM01C_2.00.03_250718`.
- WiFi firmware: `WIFIAM01C_1.00.15_2507111740`.
- Direct BLE sent transfer command `02 01 04`, `GetWifiIP`, then device-config
  pulse `BC47020000200100`.
- P2P connected to `M01 Pro_D879B87FE6C9/60:c2:2a:1a:b6:1b`.
- `/files/media.config` succeeded at `192.168.49.183`.
- Downloaded fresh photo `20260531234943012.jpg`, `371145` bytes, valid JPEG.
- Downloaded fresh video `20260531234947896.mp4`, `4816656` bytes, valid MP4.

## 2026-06-01 - Phase 7a Started

- Created a Windows field guide to carry the Android-proven command sequence
  and the Windows code map across sessions.
- First implementation slice: align Windows video stop with `02 01 03` and
  route Windows production DI through the real `HeyCyanMediaTransfer`.
- Keep `StoredImageHeyCyanMediaTransfer` available, but stop making it the
  default Windows app transfer path.

## 2026-06-01 - Phase 7a First Slice Complete

- Updated `WindowsHeyCyanGlassesSession.StopVideoAsync()` to use the proven
  video-stop payload `02 01 03`.
- Changed Windows production DI so `IHeyCyanMediaTransfer` resolves to
  `HeyCyanMediaTransfer` instead of the stored-image fallback.
- Added `HeyCyanServiceRegistrationTests` to guard the Windows transfer
  registration.
- Focused HeyCyan tests passed: `292` passed, `1` skipped.
- Windows app build passed with `0` errors.

## 2026-06-01 - Phase 7b Started

- Created Phase 7b for Windows WiFi Direct diagnostics and validation-first
  media-host selection.
- Next objective: preserve endpoint-pair diagnostics and feed all plausible
  remote hosts into `/files/media.config` probing.

## 2026-06-01 - Phase 7b Complete

- `WindowsWiFiDirectManager` now preserves matched peer, endpoint pairs, and a
  bounded discovery-event log for diagnostics.
- `WindowsHeyCyanGlassesSession` now probes BLE IP, connector route IP,
  endpoint-pair remote hosts, and Android-proven P2P media-host candidates.
- Removed the previous fallback that could return the first unvalidated
  candidate after `media.config` probe failure.
- Focused HeyCyan tests passed: `295` passed, `1` skipped.
- Windows app build passed with `0` errors.

## 2026-06-01 - Phase 7c Started

- Created Phase 7c for a hardware-gated Windows artifact probe.
- Goal: one run folder with route candidates, validated IP, WiFi Direct
  endpoint pairs, media entries, and downloaded JPEG/MP4 signatures.

## 2026-06-01 - Phase 7c Complete

- Added last-transfer candidate and validated-IP diagnostics to the Windows
  session.
- Exposed the Windows real-test fixture's WiFi Direct manager diagnostics.
- Added `WindowsRouteProbeTests` as a hardware-gated probe that writes a JSON
  report and downloaded JPEG/MP4 artifacts under the M46 captures folder.
- Real-test probe compile/run without hardware passed as skipped: `1` skipped.
- Focused HeyCyan unit tests passed: `295` passed, `1` skipped.
- Current blocker: a real Windows hardware run is required to prove route
  formation and media download.

## 2026-06-01 - Phase 7c Hardware Result

- Ran the Windows route probe with real hardware and fresh media capture.
- BLE/control remained healthy:
  - fresh photo command accepted;
  - fresh video start/stop accepted;
  - transfer command returned SSID `M01 Pro_D879B87FE6C9`, password length `9`,
    and BLE IP `192.168.31.1`.
- WiFi Direct discovery found the glasses peer:
  - `M01 Pro_D879B87FE6C9`;
  - `WiFiDirect#60:C2:2A:1A:B6:1B`.
- No-prepair WinRT WiFi Direct with `GO=0` failed with
  `COMException HRESULT=0x8007001F`.
- Pair-first WinRT WiFi Direct reached a `ConfirmOnly` pairing request, accepted
  it, then returned `PairAsync` status `Failed`; `FromIdAsync` then failed with
  `COMException HRESULT=0x80004005`.
- After the failed WiFi Direct attempt, the peer did not reappear within the
  same transfer window.
- WLAN fallback repeatedly went `associating -> disconnected`.
- Manual WLAN AutoConfig inspection showed:
  - failure reason `The specific network is not available`;
  - `RSSI: 255`.
- Best artifact:
  `.my/plan/m46-heycyan-csharp-wifi-retry/captures/phase-7c-windows-route-probe/20260601-105348/windows-route-probe-result.json`.

## 2026-06-01 - Phase 7d Complete

- Created Phase 7d to document the Windows route boundary and pivot.
- Kept diagnostic improvements in production/test code:
  - retained matched peer and discovery event history;
  - pair-first attempt ordering;
  - BLE password passed to custom pairing if a PIN is requested;
  - rediscovery between WiFi Direct attempts;
  - PowerShell WLAN diagnostics now use `-EncodedCommand`.
- Focused Windows WiFi Direct/candidate tests passed: `41` passed.
- Decision: native Windows HeyCyan media transfer is blocked on the current
  Intel BE200/Windows stack.
- Recommended next options:
  - test a second WiFi Direct/Miracast-capable USB adapter;
  - test an unmanaged Windows laptop;
  - build an Android bridge fallback for Windows media access.
