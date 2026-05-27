# Phase 1 - Protocol & Provider

**Status:** In Progress

## Goal

Add the A9/X5 camera as an `ICameraProvider` backed by the iLnkP2P/PPPP UDP
protocol. The provider should expose JPEG frames through the existing camera
abstraction without introducing a video decoder dependency.

## Scope

- Parse and build the A9 protocol packets used for discovery, login, video start,
  keepalive, ACKs, and stream control.
- Implement the XOR-flip + rotate control-payload cipher.
- Manage a UDP session with discovery, handshake, keepalive, disconnect detection,
  and JPEG frame reassembly.
- Wrap the session in `A9CameraProvider` with `ProviderId = "a9-camera"` and
  `DisplayName = "A9 Camera"`.
- Add settings for IP, optional UID, username, and password.
- Register the provider with camera DI so it appears in `CameraManager.Providers`.

## Implementation

1. Implement `A9Protocol` helpers for:
   - Command ID read/write.
   - Big-endian packet framing.
   - `LanSearch`, `P2pRdy`, `P2PAlive`, `P2PAliveAck`, `DrwAck`.
   - `ConnectUser`, `VideoParamSet`, and `StartVideo` Drw control payloads.
   - `XqBytesEnc` and `XqBytesDec`.
2. Implement `A9Session`:
   - Open UDP socket to port `32108`.
   - Perform LanSearch -> PunchPkt -> P2pRdy -> ConnectUser -> StartVideo.
   - Reply to keepalives.
   - Reassemble JPEG frames from Drw data packets.
   - Drop corrupt/out-of-order frames.
   - Raise disconnect when packets stop arriving.
3. Implement `A9CameraProvider`:
   - Read settings from `ISettingsService`.
   - Start/stop `A9Session`.
   - Return latest JPEG from `CaptureFrameAsync`.
   - Expose `StreamFramesAsync`.
   - Retry failed connections up to the configured retry limit.
4. Register `A9CameraProvider` as an `ICameraProvider`.

## Files

- `src/BodyCam/Services/Camera/A9/A9Protocol.cs`
- `src/BodyCam/Services/Camera/A9/A9Session.cs`
- `src/BodyCam/Services/Camera/A9/A9CameraProvider.cs`
- `src/BodyCam/Services/ISettingsService.cs`
- `src/BodyCam/Services/SettingsService.cs`
- `src/BodyCam/ServiceExtensions.cs`

## Acceptance Criteria

- `A9CameraProvider` is registered and visible through `CameraManager.Providers`.
- `A9CameraProvider.ProviderId` is exactly `a9-camera`.
- `StartAsync` no-ops cleanly when no A9 IP is configured.
- With valid settings and a reachable camera, `StartAsync` starts the JPEG stream.
- `CaptureFrameAsync` returns a JPEG byte array when frames are available.
- Provider disconnects fall back through the existing camera manager behavior.
- The app builds for `net10.0-windows10.0.19041.0`.
- Protocol helper tests cover packet framing and cipher round trips.
