# Phase 7b - Windows Route Diagnostics And Candidate Selection

## Goal

Make Windows transfer failures diagnosable and make media-host selection follow
the Android proof more closely.

Phase 7a made Windows use the real transfer path. Phase 7b makes the route
selection less guessy:

- capture WiFi Direct endpoint-pair diagnostics from the Windows stack;
- include all plausible endpoint-pair remote addresses in HTTP probing;
- include known Android-observed P2P media-host candidates only after a Windows
  P2P route exists;
- do not silently pick an unvalidated route candidate when
  `/files/media.config` cannot be fetched.

## Why

The Android proof showed the phone as group owner at `192.168.49.1` and the
glasses media host at `192.168.49.183`. Windows may expose a different remote
endpoint through `WiFiDirectDevice.GetConnectionEndpointPairs()`, and earlier
code only kept the first remote IP.

If the wrong candidate is returned, `HeyCyanMediaTransfer` fails later with a
generic HTTP error. The probe should instead tell us:

- which peer Windows matched;
- which endpoint pairs Windows created;
- which candidate IPs were probed;
- which candidate, if any, served `/files/media.config`.

## Implementation Plan

1. Add lightweight diagnostics to `IWindowsWiFiDirectConnector` and
   `WindowsWiFiDirectManager`:
   - matched peer name;
   - matched peer ID;
   - endpoint pairs from `GetConnectionEndpointPairs()`;
   - bounded discovery-event log.
2. Update `WindowsHeyCyanGlassesSession` to build route candidates from:
   - BLE-reported IP;
   - WiFi Direct returned IP;
   - WiFi Direct endpoint-pair remote hosts;
   - known P2P media-host candidates `192.168.49.183`, `192.168.49.200`, and
     `192.168.49.1` only when a P2P route exists.
3. Validate every candidate by fetching `/files/media.config`.
4. Stop returning the first unvalidated route candidate after probe failure.
5. Add unit tests for candidate ordering, de-duping, and known P2P fallback
   injection.

## Acceptance

- Focused HeyCyan unit tests pass.
- Windows app build passes.
- Real hardware remains gated, but a future Windows run prints enough
  diagnostics to see whether the failure is discovery, pairing, route
  formation, candidate choice, or HTTP serving.

## Implementation Notes And Findings

### 2026-06-01 - Started

- `WindowsWiFiDirectManager` already calls
  `WiFiDirectDevice.GetConnectionEndpointPairs()`, but it only stores the first
  remote IP as `RemoteIp`.
- `WindowsHeyCyanGlassesSession` already probes route candidates with
  `/files/media.config`, but after probe failure it could still return the
  first unvalidated candidate.
- Fixed fallback candidates did not include the Android-proven media host
  `192.168.49.183`.

### 2026-06-01 - Implemented

- Added WiFi Direct diagnostics to the Windows connector:
  - matched peer name;
  - matched peer ID;
  - bounded discovery-event log;
  - all endpoint pairs from `GetConnectionEndpointPairs()`.
- `WindowsWiFiDirectManager` now logs every endpoint pair with local and remote
  host/service values.
- `WindowsHeyCyanGlassesSession` now builds candidate media hosts from:
  - BLE-reported IP;
  - route IP returned by the Windows connector;
  - all WiFi Direct endpoint-pair remote hosts;
  - Android-proven P2P candidates `192.168.49.183`, `192.168.49.200`, and
    `192.168.49.1` when a P2P route exists.
- The session no longer returns the first unvalidated candidate after
  `/files/media.config` probing fails.
- Added candidate-selection tests for ordering, duplicate removal, invalid
  endpoint-pair hosts, and conditional known-P2P injection.

Verification:

```powershell
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj --no-restore --filter "FullyQualifiedName~HeyCyan" -p:SkipBuildNumberIncrement=true
```

Result: `295` passed, `1` skipped.

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 --no-restore -p:SkipBuildNumberIncrement=true
```

Result: `0` errors, existing warnings only.

## Next Ideas

- Add a single Windows probe real-test that writes one timestamped artifact
  folder with:
  - transfer-mode result;
  - matched peer;
  - endpoint pairs;
  - discovery events;
  - media.config HTTP status/body preview;
  - downloaded JPEG/MP4 signatures if available.
- Keep that test hardware-gated so normal CI/developer runs only compile it.
