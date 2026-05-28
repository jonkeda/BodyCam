# Phase 5 - A9 Discovery

**Status:** Planned

## Goal

Add a **Discover** button to the A9 setup screen so users can find A9/X5 cameras
on the local network instead of typing an IP address manually.

Discovery should not assume PPPP/iLnk is the only valid A9 path. Many A9
cameras use PPPP/iLnk, some variants expose direct RTSP or HTTP MJPEG streams,
and V720/Naxclow variants use a custom AP-mode TCP protocol. Best practice is:

1. Probe direct RTSP/MJPEG stream endpoints first.
2. Probe V720/Naxclow AP mode when the network looks like a Naxclow camera AP.
3. Fall back to PPPP/iLnk discovery and session setup when direct streams are
   not available.

The PPPP/iLnk fallback should use the existing A9 `LanSearch` packet and the
additional probe shapes from `pmpt.md`:

- UDP `32108`: current `LanSearch`/`PunchPkt` flow, plus prompt-listed JSON
  discovery if a camera variant answers that shape.
- UDP `20190`: prompt-listed binary discovery for PPPP/iLnk variants.

The UI should fit the current Settings flow:

Settings > Devices -> **+ Connect Device** -> **Add A9 Camera** -> A9 setup page.

## UX

```markui
# A9 Camera

v------------------------------------------------------v
| IP Address                                           |
| < 192.168.1.1                                    >   |
|                                                      |
| [ Discover ]                  [ Test Connection ]    |
|                                                      |
| Discovered Cameras                                  |
| v--------------------------------------------------v |
| | #camera A9 RTSP                                  | |
| | rtsp://192.168.1.31:554/live/ch00_0             | |
| | Direct stream                              RTSP  | |
| v--------------------------------------------------v |
| v--------------------------------------------------v |
| | #camera A9 HTTP                                  | |
| | http://192.168.1.32:8080/video                  | |
| | Direct stream                              MJPEG | |
| v--------------------------------------------------v |
| v--------------------------------------------------v |
| | #camera A9 V720                                  | |
| | 192.168.169.1:6123                               | |
| | Naxclow AP                         V720/Naxclow | |
| v--------------------------------------------------v |
| v--------------------------------------------------v |
| | #camera A9X5 42                                   | |
| | UID: A9X542                         192.168.1.1   | |
| | Port: 32108                  LAN / iLnkP2P        | |
| v--------------------------------------------------v |
| v--------------------------------------------------v |
| | #camera A9 Mini                                  | |
| | UID: DGK12345678                    192.168.1.42  | |
| | Port: 20190                  PPPP binary          | |
| v--------------------------------------------------v |
|                                                      |
| [ Save ]                                             |
| Ready                                                |
v------------------------------------------------------v
```

Selecting a discovered camera fills the IP address, port, UID/device-id, and
protocol variant fields when available.

## Protocol Probe Order

Run stream probes with short timeouts and stop as soon as a confident direct
stream match is found:

1. **RTSP probe**
   - Try TCP `554`.
   - Send `OPTIONS` or `DESCRIBE` against a small set of known A9 paths.
   - Treat a valid RTSP response as the preferred connection path.
2. **HTTP MJPEG probe**
   - Try HTTP `80` and `8080`.
   - Check common MJPEG endpoints and content types.
   - Accept responses that produce JPEG multipart data or JPEG frames.
3. **V720/Naxclow AP probe**
   - Try TCP `6123` when the candidate host is `192.168.169.1`, the Wi-Fi SSID
     starts with `Nax`, or the user manually entered a V720 host.
   - Send a minimal Naxclow live-motion frame.
   - Treat a parseable Naxclow frame response as `V720NaxclowAp`.
4. **PPPP/iLnk fallback**
   - Broadcast/search on UDP `32108` and `20190`.
   - Use the matching PPPP/iLnk session implementation for the detected
     variant.

The probes may run concurrently internally, but selection should prefer direct
RTSP/MJPEG results over custom protocol probes when both are available.

## Implementation

1. Add an `A9EndpointProbeService` that can test RTSP, HTTP MJPEG,
   V720/Naxclow, and PPPP/iLnk candidates.
2. Add direct stream probes:
   - RTSP probe on TCP `554`.
   - HTTP MJPEG probe on `80` and `8080`.
3. Add a V720/Naxclow AP probe for TCP `6123`.
4. Add an `A9DiscoveryService` with strategy-based PPPP/iLnk fallback probes:
   - `LanSearch` on UDP `32108` using `A9Protocol.BuildLanSearch()`.
   - JSON discovery on UDP `32108` for variants described in `pmpt.md`.
   - Binary discovery on UDP `20190` for variants described in `pmpt.md`.
5. Listen for responses for a short timeout window.
6. Parse device identity, IP address, response port, stream URL, and protocol
   variant.
7. Capture sender endpoint data as the candidate camera address.
8. Normalize all responses into `A9DiscoveredCamera`.
9. Suppress duplicates by UID first, then by IP/port/protocol/stream URL.
10. Prefer direct RTSP/MJPEG results over V720/Naxclow or PPPP/iLnk when the
    same camera answers multiple probes.
11. Add `DiscoverCommand` to `A9CameraSettingsViewModel`.
12. Add `DiscoveredCameras` collection and `SelectedDiscoveredCamera`.
13. Add a **Discover** button to `A9CameraSettingsPage`.
14. Render discovered cameras as MarkUI 3-style list-container cards using `v`
    corners.
15. Selecting a card populates:
   - `A9CameraIp`
   - `A9CameraUid`
   - `A9CameraPort`
   - `A9CameraProtocolVariant`
   - `A9CameraStreamUrl`
16. Keep manual entry available when discovery fails or the camera is in AP mode.

## Files

- `src/BodyCam/Services/Camera/A9/A9DiscoveryService.cs`
- `src/BodyCam/Services/Camera/A9/A9DiscoveredCamera.cs`
- `src/BodyCam/Services/Camera/A9/A9DiscoveryProtocol.cs`
- `src/BodyCam/Services/Camera/A9/A9EndpointProbeService.cs`
- `src/BodyCam/Services/Camera/A9/A9StreamProtocol.cs`
- `src/BodyCam/Services/Camera/A9/V720/V720NaxclowProtocol.cs`
- `src/BodyCam/ViewModels/Settings/A9CameraSettingsViewModel.cs`
- `src/BodyCam/Pages/Settings/A9CameraSettingsPage.xaml`
- `src/BodyCam/ServiceExtensions.cs`
- `src/BodyCam.Tests/Services/Camera/A9/A9DiscoveryServiceTests.cs`
- `src/BodyCam.Tests/Services/Camera/A9/A9EndpointProbeServiceTests.cs`
- `src/BodyCam.Tests/ViewModels/Settings/A9CameraSettingsViewModelTests.cs`
- `src/BodyCam.UITests/Pages/A9CameraSettingsPage.cs`
- `src/BodyCam.UITests/Tests/SettingsPage/A9CameraSettingsTests.cs`

## Automation IDs

- `A9CameraDiscoverButton`
- `A9CameraDiscoveryStatusLabel`
- `A9DiscoveredCamerasList`
- `A9DiscoveredCameraCard`
- `A9DiscoveredCameraSelectButton`
- `A9DiscoveredCameraProtocolLabel`

## Acceptance Criteria

- A9 setup page has a visible **Discover** button.
- Discover probes RTSP and HTTP MJPEG before selecting PPPP/iLnk.
- Discover probes V720/Naxclow AP mode on TCP `6123` when applicable.
- Discover sends `LanSearch` and gathers `PunchPkt` replies on UDP `32108`.
- Discover also attempts prompt-listed UDP `32108` JSON and UDP `20190` binary
  discovery without breaking the existing A9/X5 path.
- Discovered cameras show as selectable list-container cards with device id,
  IP address, stream URL where available, port, and protocol variant.
- Selecting a discovered camera fills IP, UID, port, protocol, and stream URL
  fields.
- Discovery failure shows an in-page status and preserves manual entry.
- Unit tests cover RTSP probe success/failure, HTTP MJPEG probe success/failure,
  V720/Naxclow probe success/failure, PPPP parsing, timeout, duplicate
  suppression, protocol preference, and selection.
- UI tests cover the Discover button and discovered-camera list automation IDs.
