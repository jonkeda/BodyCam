# Pickup Report - Phase 42 - 2026-05-29

## Goal

Reach C#-only Vue990/A9 image and video capture. Android is the proving ground;
Windows should only be used for ADB orchestration until the managed session
carrier works.

## Current State

Phase 41 already proved:

- C# can generate the live-open command bytes.
- Native session transport accepts the C# command when sent as header/body on
  command channel `0`.
- Channel `1` media bytes contain JPEG frames in the `55 AA 15 A8` envelope.
- C# can extract a still and assemble MJPEG AVI from those bytes.

Successful artifact reference:

- Directory:
  `.my/plan/m38-a9-camera/captures/phase-41-managed-live-cgi-command-cgi-split-2026-05-29-172506/`
- Still: `native-channel-oracle-frames/channel-frame-000.jpg`
- Still SHA-256:
  `6CBF309650B4EAEC9B6712D8F679C7DA83CCDE398C5B711DC56AB757ACC90188`
- Video: `native-channel-oracle-mjpeg.avi`
- Video SHA-256:
  `64A5607A0FEDFD0FC3510D2CBFED255192CB8C89C1867182ADFE1F9A502D8257`

Still missing:

- Pure C# HLP2P/session carrier.
- Pure C# connection/login/raw read-write path.
- Pure C# image/video from Android or Windows without native `JNIApi`
  session calls.

## Work Completed In This Pickup

Native disassembly findings:

- `client_login` sends native login status CGI:
  `GET /get_status.cgi?name=admin&loginuse=admin&loginpas=888888&user=admin&pwd=888888&`.
- `client_write`, `client_read`, and `client_check_buffer` dispatch through
  the active interface at `client + 0x80` with session handle
  `client + 0x208`.
- `connectType = 0x3F` maps to V4/HLP2P subtype `1`.
- `ConnectByServer_V4` leads into `HLP2P_ConnectByServer` and then
  `_p2p_connect_check_svr`.
- Native DRW packets use mixed byte order: outer HLP2P headers are big-endian,
  but DRW command indexes and ACK index entries are little-endian.

Managed code changes:

- Added native login-status CGI body builder and native command-header parser.
- Corrected C# DRW/DRW ACK command-index byte order.
- Added Android probe incremental progress reporting.
- Bound Android ephemeral UDP sockets before receiving.
- Trimmed managed-direct HTTP probing to fast `get_status.cgi` checks only.
- Added Android `NEARBY_WIFI_DEVICES` and `ACCESS_FINE_LOCATION` manifest
  entries.
- Changed Android install path to `adb install -r -g` and attempted runtime
  grants for Wi-Fi permissions.

Verification:

- Focused Vue990 tests passed: `42/42`.
- Android probe build passed.

## Latest Android-Only Run

Constraint:

- Laptop Wi-Fi was not used.
- Windows used USB/ADB only.
- Android phone Wi-Fi was connected to the camera.

Run:

- Directory:
  `.my/plan/m38-a9-camera/captures/phase-42-android-wifi-permission-2026-05-29-210712/`
- Phone Wi-Fi: `@MC-0025644`, `192.168.168.100/24`
- Camera host: `192.168.168.1`
- Result: probe completed, no image/video.

Observed:

- TCP `81` status works.
- No direct HTTP JPEG/MJPEG/H264 media.
- `NEARBY_WIFI_DEVICES` was granted and the earlier UDP `Permission denied`
  failure is gone.
- UDP probes send successfully, but only self-echo packets are received from
  the phone on UDP `32108` and `65529`.
- Classic PPPP/HLP2P candidate bursts receive no remote camera
  `PunchPkt` / `P2pReady`.
- DAS relay fallback decodes relays and attempts `24` TCP `65527` candidates,
  but all time out with `0` response bytes.

Conclusion:

- Android Wi-Fi permission/routing is no longer the blocker.
- The current blocker is still the exact native HLP2P session-open handshake,
  especially the `_p2p_connect_check_svr` path and its dynamic request fields.

## Key Files Touched

- `src/BodyCam/Services/Camera/A9/Vue990/A9Vue990CgiCommandBuilder.cs`
- `src/BodyCam/Services/Camera/A9/Vue990/A9Vue990PpcsPacket.cs`
- `src/BodyCam/Services/Camera/A9/Vue990/A9AndroidManagedDirectClient.cs`
- `tools/BodyCam.A9PhoneProbe/ManagedDirectMediaProbe.cs`
- `tools/BodyCam.A9PhoneProbe/MainActivity.cs`
- `tools/BodyCam.A9PhoneProbe/AndroidManifest.xml`
- `.my/plan/m38-a9-camera/phase-42-managed-session-transport-replacement.md`
- `.my/plan/m38-a9-camera/current-status-report-2026-05-29.md`
- `.my/plan/m38-a9-camera/realtests-log.md`

## Do Not Repeat

- Broad HTTP media URL scans.
- RTSP probing.
- Generic UDP probe matrices without new native evidence.
- Fixed relay packet replay without dynamic native session context.
- Windows-only camera networking before Android has a managed session carrier.

## Next Best Step

Focus on native `_p2p_connect_check_svr` and the HLP2P connect-by-server
message sequence. The next useful artifact is a byte-accurate map of the first
native HLP2P request/response after DAS decode, including dynamic nonce/token
fields, before adding more managed probe variants.
