# Phase 14 - V720/Naxclow A9 Variant

**Status:** Planned

## Goal

Add support for A9 cameras that use the Naxclow/V720 app protocol described by
`intx82/a9-v720`.

This is a separate protocol variant from the completed cam-reverse UDP/MJPEG
A9/X5 path and from the planned TCP PPPP/iLnk H.264 path. It should be detected
and implemented behind its own protocol variant so BodyCam can connect without
locking all A9 devices into one assumption.

## Source Findings

Reference: https://github.com/intx82/a9-v720

The repository points to a V720/Naxclow A9 family with these traits:

- AP mode SSID commonly starts with `Nax`.
- AP mode camera IP is usually `192.168.169.1`.
- AP mode uses TCP port `6123`.
- Packets use a Naxclow frame:
  - payload length: 4 bytes little-endian
  - command: 2 bytes little-endian
  - message flag: 1 byte
  - deal flag: 1 byte
  - forward id: 8 bytes
  - package id: 4 bytes little-endian
  - payload bytes
- JSON payloads are used for connect/control commands.
- AP commands wrap a `content` object with command code `502`.
- Live setup starts with command `115` (`P2P_UDP_CMD_LIVE_MOTION`), then an AP
  connect command `501`, then base-info command `4`.
- Live video opens with command `3` (`CODE_FORWARD_OPEN_A_OPEN_V`).
- Video packets are command `1` and carry JPEG data.
- Audio packets are command `4` and carry G.711 A-law.
- SD-card browsing/downloading uses date-list, config, media-info, and
  start-stream commands.
- STA mode can be captured through a fake local server, DNS redirection for
  Naxclow domains, MQTT on port `1883`, and TCP/UDP relay channels.

## Scope

First implementation should target the local AP-mode path:

1. Connect to `192.168.169.1:6123` or a configured host/port.
2. Parse and build the Naxclow frame.
3. Send live-motion init.
4. Send AP connect.
5. Read base info and firmware version.
6. Open live video.
7. Reassemble JPEG frames from command `1` payloads.
8. Optionally surface G.711 A-law audio packets as a later phase-8 input.

STA/fake-server mode is useful but should be a second step because it requires
DNS redirection, a local HTTP server, MQTT behavior, and TCP/UDP relay handling.

## Detection

Add a `V720NaxclowAp` protocol probe to the phase 5 discovery matrix:

1. Check whether the host is `192.168.169.1` or the current Wi-Fi SSID starts
   with `Nax`.
2. Try TCP connect to port `6123`.
3. Send a minimal Naxclow live-motion frame with command `115`.
4. Treat a parseable Naxclow frame response as `A9ProtocolVariant.V720NaxclowAp`.

Do not try V720 AP commands against arbitrary LAN hosts unless the user has
enabled broad probing in RealTests.

## Proposed Types

- `A9ProtocolVariant.V720NaxclowAp`
- `V720NaxclowProtocol`
- `V720NaxclowFrame`
- `V720NaxclowSession`
- `V720NaxclowCameraProvider` or a variant-aware branch inside
  `A9CameraProvider`
- `V720NaxclowRealTests`

## Files

- `src/BodyCam/Services/Camera/A9/V720/V720NaxclowProtocol.cs`
- `src/BodyCam/Services/Camera/A9/V720/V720NaxclowFrame.cs`
- `src/BodyCam/Services/Camera/A9/V720/V720NaxclowSession.cs`
- `src/BodyCam.Tests/Services/Camera/A9/V720/V720NaxclowProtocolTests.cs`
- `src/BodyCam.Tests/Services/Camera/A9/V720/V720NaxclowSessionTests.cs`
- `src/BodyCam.RealTests/A9/V720NaxclowRealTests.cs`

## RealTests

Add hardware-gated tests:

- `V720Naxclow_Probe_DetectsApCamera`
- `V720Naxclow_ConnectsAndReadsBaseInfo`
- `V720Naxclow_ReceivesJpegFrame`

Environment variables:

```powershell
$env:A9_E2E = "1"
$env:A9_V720_E2E = "1"
$env:A9_V720_HOST = "192.168.169.1"
$env:A9_V720_PORT = "6123"
```

These tests should ask the user to switch on exactly one V720/Naxclow A9 camera
and connect the computer to the camera AP before running.

## Acceptance Criteria

- BodyCam can detect a V720/Naxclow AP-mode camera separately from RTSP, HTTP
  MJPEG, cam-reverse UDP/MJPEG, and TCP PPPP/iLnk variants.
- Unit tests cover Naxclow frame encode/decode, JSON command wrapping, and JPEG
  frame extraction.
- RealTests skip unless `A9_E2E=1` and `A9_V720_E2E=1`.
- With hardware enabled, RealTests can read base info and receive one JPEG
  frame from the camera.
- Existing A9/X5 UDP/MJPEG behavior remains unchanged.

## Out Of Scope

- DNS redirection setup for `*.naxclow.com`.
- Running a local MQTT broker.
- Full STA-mode fake-server support.
- SD-card file browsing and AVI download.
- Camera settings mutations such as Wi-Fi setup, AP password changes, reboot,
  IR LED, or flip controls.
