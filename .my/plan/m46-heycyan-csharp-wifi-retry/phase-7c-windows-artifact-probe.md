# Phase 7c - Windows Artifact Probe

## Goal

Add one focused Windows real-hardware probe that writes an artifact folder for a
single transfer attempt.

This phase does not claim Windows hardware is proven. It makes the next hardware
run useful by saving the evidence needed to decide what remains broken.

## Probe Shape

The probe should:

1. connect to the configured HeyCyan glasses through the existing Windows
   fixture;
2. optionally create fresh media;
3. trigger the existing `HeyCyanMediaTransfer` app path;
4. save a JSON report with:
   - run timing;
   - configured device MAC;
   - transfer success/failure;
   - last transfer candidate IPs;
   - validated transfer IP, if any;
   - WiFi Direct matched peer;
   - WiFi Direct endpoint pairs;
   - WiFi Direct discovery events;
   - media entries;
   - downloaded photo/video metadata;
   - exception details on failure;
5. save downloaded JPEG/MP4 bytes when available.

## Runbook

```powershell
$env:BODYCAM_REAL_HEYCYAN="1"
$env:BODYCAM_REAL_HEYCYAN_MAC="D8:79:B8:7F:E6:C9"
dotnet test src/BodyCam.RealTests/BodyCam.RealTests.csproj -f net10.0-windows10.0.19041.0 --filter "Category=RealWindowsRouteProbe" -v normal --logger "console;verbosity=detailed"
```

Optional:

```powershell
$env:BODYCAM_REAL_HEYCYAN_WINDOWS_CAPTURE_FRESH="1"
$env:BODYCAM_REAL_HEYCYAN_WINDOWS_VIDEO_SECONDS="4"
$env:BODYCAM_REAL_HEYCYAN_WINDOWS_ARTIFACT_DIR="e:\temp\bodycam-heycyan-windows"
```

## Acceptance

- Normal compile/test runs do not require hardware.
- The real-test assembly compiles.
- When hardware is available, the probe writes a timestamped folder even on
  failure.

## Implementation Notes And Findings

### 2026-06-01 - Started

- Phase 7b added WiFi Direct endpoint-pair diagnostics, but there is not yet a
  single hardware run that writes them to disk with media results.
- The probe will use `HeyCyanMediaTransfer` rather than a separate camera path,
  so it validates the same route the app uses after Phase 7a.

### 2026-06-01 - Implemented

- Added session diagnostics for the last transfer attempt:
  - ordered candidate IP list;
  - validated transfer IP.
- Exposed the Windows real-test fixture's `WindowsWiFiDirectManager` so the
  probe can serialize matched peer and endpoint-pair diagnostics.
- Added `WindowsRouteProbeTests.Windows_route_probe_writes_transfer_artifacts`.
- The probe is hardware-gated by `BODYCAM_REAL_HEYCYAN=1`.
- By default, the probe creates fresh media before transfer:
  - photo command;
  - short video start/stop using the Phase 7a fixed video-stop payload.
- The probe writes `windows-route-probe-result.json` plus downloaded JPEG/MP4
  files when available.
- Default artifact root:
  `captures/phase-7c-windows-route-probe/{timestamp}/`.

Verification without hardware:

```powershell
dotnet test src/BodyCam.RealTests/BodyCam.RealTests.csproj -f net10.0-windows10.0.19041.0 --no-restore --filter "Category=RealWindowsRouteProbe" -p:SkipBuildNumberIncrement=true
```

Result: compiled and skipped (`1` skipped) because `BODYCAM_REAL_HEYCYAN` is not
set.

Focused unit verification:

```powershell
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj --no-restore --filter "FullyQualifiedName~HeyCyan" -p:SkipBuildNumberIncrement=true
```

Result: `295` passed, `1` skipped.

## Hardware Results

### 2026-06-01 - First Windows Runs

Ran the gated probe with:

```powershell
$env:BODYCAM_REAL_HEYCYAN="1"
$env:BODYCAM_REAL_HEYCYAN_MAC="D8:79:B8:7F:E6:C9"
$env:BODYCAM_REAL_HEYCYAN_WINDOWS_CAPTURE_FRESH="1"
dotnet test src\BodyCam.RealTests\BodyCam.RealTests.csproj -f net10.0-windows10.0.19041.0 --no-restore --filter "Category=RealWindowsRouteProbe" -p:SkipBuildNumberIncrement=true -v normal --logger "console;verbosity=detailed"
```

Artifacts:

- `captures/phase-7c-windows-route-probe/20260601-103753/`
- `captures/phase-7c-windows-route-probe/20260601-104330/`
- `captures/phase-7c-windows-route-probe/20260601-104727/`
- `captures/phase-7c-windows-route-probe/20260601-105348/`

Findings:

- Fresh photo and video commands were accepted.
- BLE transfer mode returned SSID `M01 Pro_D879B87FE6C9`, password length `9`,
  and BLE IP `192.168.31.1`.
- WiFi Direct discovered the peer as
  `WiFiDirect#60:C2:2A:1A:B6:1B`.
- No endpoint pairs were created.
- No transfer candidate IP was validated.
- No JPEG/MP4 could be downloaded on Windows.

The final run preserved the most useful diagnostics:

- pair-first `PushButton` / `GroupOwnerNegotiation` / `GO=0`;
- Windows raised `ConfirmOnly`, which the code accepted;
- `PairAsync` returned `Failed`;
- `FromIdAsync` then failed with `0x80004005`;
- the peer did not reappear during rediscovery.

WLAN fallback diagnostics showed the glasses SSID is not available as a normal
infrastructure network on this adapter:

- `Failure Reason: The specific network is not available.`
- `RSSI: 255`

## Current Blocker

Windows can discover the HeyCyan WiFi Direct peer but cannot form a routed
connection on the current Intel BE200 adapter. The fallback WLAN profile also
fails because the returned SSID is not behaving like an infrastructure AP.

Phase 7d records the boundary and pivot options.
