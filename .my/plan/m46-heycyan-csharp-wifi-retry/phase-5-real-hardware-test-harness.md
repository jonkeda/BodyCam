# Phase 5 - Real Hardware Test Harness

**Status:** Implemented and passed on real hardware. It launches the existing
Android probe over `adb`, pulls probe artifacts, and captures failure
diagnostics.

## Goal

Make the M46 path repeatable and debuggable on real glasses.

## Work

- [x] Add tests behind `BODYCAM_REAL_HEYCYAN_WIFI=1`.
- [x] Use env vars:
  - `BODYCAM_REAL_HEYCYAN_MAC`;
  - `BODYCAM_REAL_HEYCYAN_NAME`;
  - `BODYCAM_REAL_HEYCYAN_MODEL`;
  - `BODYCAM_REAL_HEYCYAN_WIFI_VERBOSE=1`.
- [x] Additional harness env vars:
  - `BODYCAM_REAL_HEYCYAN_ADB`;
  - `BODYCAM_REAL_HEYCYAN_ADB_SERIAL`;
  - `BODYCAM_REAL_HEYCYAN_ARTIFACT_DIR`;
  - `BODYCAM_REAL_HEYCYAN_VIDEO_SECONDS`;
  - `BODYCAM_REAL_HEYCYAN_SCAN_SECONDS`;
  - `BODYCAM_REAL_HEYCYAN_HOLD_ON_FAILURE_SECONDS`.
- [x] Test the full Android probe path:
  - BLE connect and transfer command path;
  - P2P discovery/connect path through the app probe logs;
  - HTTP `media.config` list through `IHeyCyanMediaTransfer`;
  - newest JPEG download and signature validation;
  - newest MP4 download and signature validation;
  - cleanup/stayon reset from the host harness.
- [ ] Add a dedicated warm-reuse assertion that counts reconnects across two
  consecutive probe transfers.
- [x] On failure, save raw diagnostic evidence:
  - logcat;
  - P2P state;
  - route table;
  - connectivity dump;
  - Android probe result and pulled media when available.

## Implementation

Test files:

- `src/BodyCam.Tests/Services/Glasses/HeyCyan/RealHardware/HeyCyanRealHardwareFactAttribute.cs`
- `src/BodyCam.Tests/Services/Glasses/HeyCyan/RealHardware/HeyCyanRealHardwareWifiTests.cs`

The test starts the installed Android BodyCam app with:

- action `com.companyname.bodycam.HEYCYAN_PROBE`;
- `capturePhotoBeforeTransfer=true`;
- `recordVideoBeforeTransfer=true`.

It waits for the probe completion log, pulls `probe-result.json` and downloaded
media, then asserts:

- the probe reported success;
- a device was selected;
- media entries were listed;
- the photo has a valid JPEG signature;
- the video has a valid MP4 `ftyp` signature.

Artifacts are written to:

```text
.my/plan/m46-heycyan-csharp-wifi-retry/captures/phase-5-real-hardware-test-harness/{timestamp}/
```

## Run

Deploy the current Android app to the phone first, then run:

```powershell
$env:BODYCAM_REAL_HEYCYAN_WIFI='1'
$env:BODYCAM_REAL_HEYCYAN_NAME='M01 Pro'
dotnet test src\BodyCam.Tests\BodyCam.Tests.csproj --filter "FullyQualifiedName~HeyCyanRealHardwareWifiTests"
```

Optional if more than one Android device is connected:

```powershell
$env:BODYCAM_REAL_HEYCYAN_ADB_SERIAL='<adb serial>'
```

Optional if you want to pin a specific BLE device:

```powershell
$env:BODYCAM_REAL_HEYCYAN_MAC='AA:BB:CC:DD:EE:FF'
```

## Acceptance

- [x] At least one full green real-hardware run is saved with artifacts.
- [x] Failures include enough context to write an RCA without rerunning blindly.

## First Green Run

Run folder:

```text
.my/plan/m46-heycyan-csharp-wifi-retry/captures/phase-5-real-hardware-test-harness/20260531-230532/
```

Result:

- Device: `M01 Pro_E6C9` / `D8:79:B8:7F:E6:C9`.
- Version: `AM01C_V2.0/AM01C_2.00.03_250718`.
- WiFi firmware: `WIFIAM01C_1.00.15_2507111740`.
- Media list: `6` entries.
- Photo: `20260531230515014.jpg`, `329,665` bytes, valid JPEG `FF D8`.
- Video: `20260531230520943.mp4`, `5,164,495` bytes, valid MP4 `ftypisom`.
