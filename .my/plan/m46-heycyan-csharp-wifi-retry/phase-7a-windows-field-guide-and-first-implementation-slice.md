# Phase 7a - Windows Field Guide And First Implementation Slice

## Goal

Create a session-stable Windows field guide, then make the first small
production slice toward the Windows HeyCyan media route.

The implementation target stays the existing path:

```text
HeyCyanCameraProvider
  -> IHeyCyanMediaTransfer / HeyCyanMediaTransfer
  -> IHeyCyanGlassesSession / WindowsHeyCyanGlassesSession
  -> WindowsWiFiDirectManager or WindowsGlassesWiFiManager
  -> WindowsHeyCyanHttpClientFactory
```

Do not add a second camera path for Windows. If the Look/camera route needs a
larger refactor later, keep that as a later milestone.

## Known Good Android Sequence

The no-AAR Android C# hardware run proves this sequence:

1. Connect BLE over direct C# GATT.
2. Capture fresh photo with `02 01 01`.
3. Start video with `02 01 02`.
4. Stop video with `02 01 03`.
5. Enter transfer mode with `02 01 04`.
6. Poll `GetWifiIP` with `02 03`.
7. Send device-config activation/keepalive:
   - action `0x47`;
   - payload `01 00`;
   - full frame observed as `BC47020000200100`.
8. Form WiFi Direct/P2P.
9. Fetch `http://{glasses-ip}/files/media.config`.
10. Download `/files/{name}` for JPEG and MP4.

Latest proven Android hardware result:

- peer: `M01 Pro_D879B87FE6C9/60:c2:2a:1a:b6:1b`;
- media host: `192.168.49.183`;
- group-owner/phone side: `192.168.49.1`;
- photo: `20260531234943012.jpg`, valid JPEG;
- video: `20260531234947896.mp4`, valid MP4;
- artifact folder:
  `captures/phase-5-real-hardware-test-harness/20260531-234952/`.

## Windows Code Map

- `WindowsHeyCyanGlassesSession`
  - owns Windows BLE connect, command send, transfer-mode state machine, route
    candidate probing, and cleanup.
- `WindowsWiFiDirectManager`
  - owns Windows WiFi Direct discovery and `WiFiDirectDevice.FromIdAsync`.
- `WindowsGlassesWiFiManager`
  - owns fallback regular WiFi/hidden-SSID joining through WinRT and `netsh`.
- `WindowsHeyCyanHttpClientFactory`
  - creates the cleartext HTTP client once Windows has a route.
- `HeyCyanMediaTransfer`
  - lists `/files/media.config`, downloads files, and keeps transfer mode warm.
- `HeyCyanCameraProvider`
  - camera-facing entry point used by the app and Look path.

## First Implementation Slice

Make the Windows production path align with the Android proof before deeper
hardware probing:

- use `StopVideoRecording()` (`02 01 03`) for
  `WindowsHeyCyanGlassesSession.StopVideoAsync()`;
- route Windows `IHeyCyanMediaTransfer` to `HeyCyanMediaTransfer`, not the
  stored-image fallback;
- leave `StoredImageHeyCyanMediaTransfer` available for tests/dev injection,
  but do not make it the Windows production default;
- add a registration guard test so Windows does not silently regress back to the
  fallback path;
- run focused HeyCyan tests and a Windows build.

## Current Uncertainties

- Windows may connect to the WiFi Direct peer but not install a route that
  `HttpClient` can use.
- Windows pairing state can be stale and may require unpair/retry.
- The correct HTTP target may be the BLE-reported IP, the WiFi Direct remote
  endpoint, or another address in the P2P subnet.
- The fallback hidden-SSID path from older M36 work may still be useful, but it
  should be treated as secondary unless hardware proves it.
- Hardware validation has to wait for a Windows real-device run.

## Runbook

Focused unit tests:

```powershell
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj --no-restore --filter "FullyQualifiedName~HeyCyan"
```

Windows app build:

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 --no-restore -p:SkipBuildNumberIncrement=true
```

Future hardware-gated Windows run:

```powershell
$env:BODYCAM_REAL_HEYCYAN="1"
$env:BODYCAM_REAL_HEYCYAN_MAC="D8:79:B8:7F:E6:C9"
dotnet test src/BodyCam.RealTests/BodyCam.RealTests.csproj -f net10.0-windows10.0.19041.0 --filter "Category=RealWiFiDirect"
```

## Implementation Notes And Findings

### 2026-06-01 - Phase 7a Start

- Windows already has the Android-style transfer entry sequence in
  `WindowsHeyCyanGlassesSession.EnterTransferModeAsync`: transfer command,
  `GetWifiIP`, `GetDeviceConfig`, WiFi Direct route attempt, route candidate
  probing, and fallback WiFi attempts.
- Two first-slice mismatches remain:
  - `StopVideoAsync()` still uses broad `StopMode()` (`02 01 0b`) instead of
    the proven video stop (`02 01 03`);
  - Windows DI still resolves `IHeyCyanMediaTransfer` to
    `StoredImageHeyCyanMediaTransfer`, which bypasses the real HTTP transfer
    route even though `WindowsHeyCyanHttpClientFactory` exists.
- The real-hardware Windows fixture already constructs `HeyCyanMediaTransfer`
  manually, so the production DI change brings the app path closer to the
  existing real-test path.

### 2026-06-01 - First Slice Implemented

- `WindowsHeyCyanGlassesSession.StopVideoAsync()` now sends
  `HeyCyanCommands.StopVideoRecording()` (`02 01 03`) instead of
  `StopMode()` (`02 01 0b`).
- Windows production DI now resolves `IHeyCyanMediaTransfer` to
  `HeyCyanMediaTransfer`, matching Android/iOS and the Windows real-test
  fixture.
- `StoredImageHeyCyanMediaTransfer` remains registered for explicit tests or
  development injection, but it is no longer the default Windows app transfer
  path.
- Added `HeyCyanServiceRegistrationTests` to guard against Windows silently
  returning to the stored-image fallback.
- Focused tests passed:

```powershell
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj --no-restore --filter "FullyQualifiedName~HeyCyan" -p:SkipBuildNumberIncrement=true
```

Result: `292` passed, `1` skipped.

- Windows build passed:

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 --no-restore -p:SkipBuildNumberIncrement=true
```

Result: `0` errors, existing warnings only.

## Next Ideas

- Add a Windows real-hardware probe entry point that records BLE notifications,
  WiFi Direct watcher events, endpoint pairs, candidate IP probes, and HTTP
  responses into a timestamped artifact folder.
- Teach `WindowsWiFiDirectManager` to expose endpoint-pair diagnostics instead
  of only the first remote IP.
- Keep the hidden-SSID route as a fallback, but make the WiFi Direct route the
  primary path because it matches the Android proof.
