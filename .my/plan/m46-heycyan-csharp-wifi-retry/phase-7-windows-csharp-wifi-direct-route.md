# Phase 7 - Windows C# Wi-Fi Direct Route

## Goal

Implement the same proven HeyCyan media-transfer behavior on Windows, if the
Windows Wi-Fi Direct stack can form a usable route to the glasses.

The target is not a new camera abstraction. The target is to make the existing
HeyCyan provider/session path work on Windows with the same behavior proven on
Android:

1. capture or select fresh media;
2. stop recording cleanly;
3. enter transfer mode with `02 01 04`;
4. form a Wi-Fi Direct route to the glasses;
5. fetch `/files/media.config`;
6. download image/video files through `HeyCyanMediaTransfer`;
7. let the existing `HeyCyanCameraProvider` consume the result.

## Current Status

As of 2026-06-01, Phase 7 is blocked on the current Windows adapter.

Windows can run the HeyCyan BLE/control sequence and discover the WiFi Direct
peer, but the Intel BE200/Windows stack does not create endpoint pairs or a
routed HTTP path. Phase 7d records the evidence and recommends trying a second
WiFi Direct/Miracast-capable adapter, an unmanaged Windows laptop, or an Android
bridge fallback before spending more time on minor native Windows variants.

## Starting Point

Windows already has partial support:

- `WindowsWiFiDirectManager` discovers and connects through
  `Windows.Devices.WiFiDirect.WiFiDirectDevice`;
- `WindowsHeyCyanGlassesSession.EnterTransferModeAsync` starts Wi-Fi Direct
  discovery before the BLE transfer command;
- real tests exist under `WindowsWiFiDirectTests`;
- fallback regular Wi-Fi/hotspot code exists in `WindowsGlassesWiFiManager`.

The Android proof gives us the exact behavior to preserve:

- start photo: `02 01 01`;
- start video: `02 01 02`;
- stop video: `02 01 03`;
- media transfer: `02 01 04`;
- optional IP poll: `02 03`;
- exit transfer: `02 01 09`;
- reset P2P: `02 01 0f`;
- media endpoint: `http://{glasses-ip}/files/media.config`.

## Main Unknowns

Windows may not behave like Android even with the same BLE sequence:

- Windows may expose the remote endpoint as the glasses IP, the group-owner IP,
  or a different peer-local address.
- Pairing may require stale Wi-Fi Direct device cleanup or a user-visible
  pairing consent path.
- `WiFiDirectDevice.FromIdAsync` may connect but still not create a route that
  `HttpClient` can use.
- The glasses may expose P2P as an association endpoint only while transfer mode
  is active, so discovery timing is critical.
- Some Windows Wi-Fi adapters may not support the needed P2P role reliably.

## Proposed Implementation

### 1. Align Windows Commands

Update Windows session behavior to match the Android proof:

- `StartVideoAsync` sends `StartVideoRecording()` / `02 01 02`;
- `StopVideoAsync` sends `StopVideoRecording()` / `02 01 03`;
- broad `StopMode()` / `02 01 0b` remains available, but is not used as the
  normal video stop path.

This prevents the glasses from staying in a mode where the media HTTP server
accepts TCP but does not serve `media.config`.

### 2. Add A Windows Probe Mode

Create a Windows real-hardware probe equivalent to the Android probe:

- connect to the configured HeyCyan BLE address;
- optionally capture a fresh photo;
- optionally record a short video and stop it correctly;
- enter transfer mode;
- collect Wi-Fi Direct watcher events;
- record endpoint pairs from `WiFiDirectDevice.GetConnectionEndpointPairs()`;
- probe all plausible IP candidates sequentially;
- download newest photo/video if `media.config` succeeds;
- write a timestamped result folder.

Do not depend on the Look command for this phase. Keep it at the
session/transfer/provider layer.

### 3. Rework Candidate IP Resolution

Use a validation-first approach instead of trusting a single source:

- BLE-reported transfer IP;
- Wi-Fi Direct remote endpoint;
- Wi-Fi Direct local endpoint's peer subnet;
- known Android-observed candidates like `192.168.49.183`;
- hotspot fallbacks only if Wi-Fi Direct cannot form a route.

The selected IP must be the first candidate that returns valid
`/files/media.config` content.

### 4. Harden Windows Wi-Fi Direct Lifecycle

Add cleanup and retry logic around:

- stale paired Wi-Fi Direct devices;
- watcher start/stop races;
- peer names that do not include the BLE MAC;
- first connect timeout followed by a discovery restart;
- disconnect/exit transfer cleanup.

The Windows route should connect only to likely glasses peers, not arbitrary
Wi-Fi Direct devices.

### 5. Stream Downloads

Use the same transfer API as Android, but make sure Windows media downloads are
streamed to the target file/store for MP4s instead of buffering large videos in
memory.

### 6. Wire Existing Provider

Once the probe passes:

- keep `HeyCyanCameraProvider` as the camera-facing entry point;
- keep `HeyCyanMediaTransfer` as the transfer abstraction;
- use the Windows session/transport underneath;
- defer any Look/camera command refactor to a later milestone.

## Test Plan

Add or update real-hardware tests behind the existing Windows gate:

```powershell
$env:BODYCAM_REAL_HEYCYAN="1"
$env:BODYCAM_REAL_HEYCYAN_MAC="D8:79:B8:7F:E6:C9"
dotnet test src/BodyCam.RealTests -f net10.0-windows10.0.19041.0 --filter "Category=RealWiFiDirect"
```

Required tests:

- fresh photo capture then `HeyCyanCameraProvider.CaptureFrameAsync`;
- fresh short video then media transfer lists the MP4;
- `EnterTransferModeAsync` resolves a validated media IP;
- `/files/media.config` returns non-empty parseable content;
- reconnect after `ExitTransferModeAsync`;
- failure log includes watcher events, endpoint pairs, BLE IP, candidate IPs,
  and HTTP errors.

## Success Criteria

- Windows forms a Wi-Fi Direct or fallback Wi-Fi route without the official app.
- Windows fetches a valid `/files/media.config`.
- Windows downloads at least one valid JPEG.
- Windows can download a valid MP4 when a fresh video was recorded.
- The existing `HeyCyanCameraProvider` can return a JPEG through the Windows
  transfer route.
- Failures are diagnosable from saved probe/test artifacts.

## Stop Or Pivot Criteria

Pivot away from Windows Wi-Fi Direct if:

- Windows cannot form a P2P route on the available adapter after repeated clean
  attempts;
- the route forms but Windows cannot send HTTP to the glasses endpoint;
- pairing requires user interaction that cannot be made reliable enough for the
  BodyCam flow;
- firmware only exposes the HTTP media server for Android-style P2P roles.

If that happens, keep Android as the primary supported HeyCyan media path and
document Windows as BLE/control/audio-only until a reliable transport appears.

## Probability

Moderate, not guaranteed.

The chance is better now because Android proved the BLE and HTTP behavior, and
Windows already has Wi-Fi Direct code. The risk is mostly Windows networking:
adapter support, pairing state, endpoint addressing, and whether `HttpClient`
can use the formed P2P route.
