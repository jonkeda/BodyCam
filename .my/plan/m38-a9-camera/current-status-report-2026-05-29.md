# M38 A9/Vue990 Current Status Report - 2026-05-29

## Short Answer

We made real progress. We can now start the camera stream with C#-generated
Vue990 command bytes and download a real still image plus a short MJPEG AVI
from the live stream.

This is not fully pure C# yet. The live-open command, JPEG extraction, and video
assembly are C#, but the remaining session carrier still uses native Vue990/PPCS
calls for connect/login/raw read-write.

## Current Result

Working camera:

- SSID / tag: `@MC-0025644`
- Camera host: `192.168.168.1`
- Phone Wi-Fi during successful run: `192.168.168.101/24`
- Camera identity from status: `BKGD00000100FMQLN`
- VUID: `BK0025644WBPD`
- Credentials used: `admin` / `888888`

Successful artifact run:

- Directory:
  `.my/plan/m38-a9-camera/captures/phase-41-managed-live-cgi-command-cgi-split-2026-05-29-172506/`
- Still image:
  `native-channel-oracle-frames/channel-frame-000.jpg`
- Still dimensions: `640x480`
- Still size: `9247` bytes
- Still SHA-256:
  `6CBF309650B4EAEC9B6712D8F679C7DA83CCDE398C5B711DC56AB757ACC90188`
- Extracted frames: `73`
- Video:
  `native-channel-oracle-mjpeg.avi`
- Video size: `677120` bytes
- Video SHA-256:
  `64A5607A0FEDFD0FC3510D2CBFED255192CB8C89C1867182ADFE1F9A502D8257`

Verification:

- Focused Vue990 tests passed: `40/40`
- Android probe build passed
- Live camera run succeeded and pulled artifacts back to Windows

Latest Phase 42 Android-only attempt:

- Laptop Wi-Fi was not used; Windows only controlled the Android phone over
  USB/ADB.
- Directory:
  `.my/plan/m38-a9-camera/captures/phase-42-android-wifi-permission-2026-05-29-210712/`
- Phone Wi-Fi: `@MC-0025644`, `192.168.168.100/24`
- Android probe result: completed, but captured image/video were both false.
- The Android `NEARBY_WIFI_DEVICES` permission fix removed the earlier UDP
  `Permission denied` failures.
- Managed UDP session probes now send on the camera Wi-Fi, but receive only
  self-echo packets from the phone; no remote camera `PunchPkt` / `P2pReady`
  response was observed.
- Managed relay fallback decoded DAS and attempted `24` TCP `65527`
  candidates; all timed out with no response bytes.

Latest Phase 43 Android-only native-log oracle:

- Laptop Wi-Fi was still not used; Windows only controlled the phone over
  USB/ADB.
- Directory:
  `.my/plan/m38-a9-camera/captures/phase-43-native-hlp2p-log-2026-05-29-211949/`
- HLP2P native debug logging was enabled successfully.
- Native `ConnectByServer` succeeded through a local UDP LAN-hole path, not a
  TCP relay.
- The camera endpoint after native connect was logged as
  `192.168.168.1:53674`.
- Fresh native-backed still:
  `native-channel-oracle-frames/channel-frame-000.jpg`, `640x480`,
  `14043` bytes, SHA-256
  `BD89669D1244913B888E5AF2EF5CC376CEF9EC30C10A8D9D4D9814D2950E4369`.
- Fresh native-backed video:
  `native-channel-oracle-mjpeg.avi`, `685544` bytes, SHA-256
  `5B0D8D1550D332D8126EC21144BF84D554EA851F4DFB6CEE808EBB8379A84FBF`.
- Added a managed C# DAS connect descriptor that preserves binary token bytes;
  focused Vue990 tests now pass `42/42`.
- Android phone probe build passes for `net10.0-android`.

## What We Learned

The earlier broad probes are no longer the best path. Direct HTTP/RTSP/MJPEG,
classic PPPP, generic UDP matrices, and fixed relay replays did not produce
media. The current blocker is the Vue990/OKSMART session carrier.

Phase 43 clarified the carrier target:

- The successful native path used `_se_lan_hole`, received `dev lan hole`, then
  `dev lan hole ack`, and connected in about `260ms`.
- Native keepalive then talked to `192.168.168.1:53674` over UDP.
- Managed work should now target the native LAN-hole opener and carrier packet
  format, not another broad relay or port matrix.

Phase 40 proved the stream payload:

- Channel media is JPEG frames.
- Frames arrive inside a Vue990 envelope starting with `55 AA 15 A8`.
- C# can extract those JPEGs.
- C# can assemble those JPEGs into MJPEG AVI.

Phase 41 proved the live-open command:

- Native `writeCgi` does not send raw HTTP or raw CGI to channel `1`.
- It builds a credentialed command body and writes it to command channel `0`.
- The command is sent as an 8-byte little-endian header followed by ASCII CGI
  text.

Confirmed C# live-open command:

```text
Header:
01 0A 00 00 61 00 00 00

Body:
GET /livestream.cgi?streamid=10&substream=0&loginuse=admin&loginpas=888888&user=admin&pwd=888888&
```

Confirmed send order:

```text
JNIApi.write(clientPtr, 0, header, 5000)
JNIApi.write(clientPtr, 0, body, 5000)
```

That command produced the expected stream-start callback and channel `1` media
bytes.

## What Is C# Now

The following pieces are now C# owned:

- Windows orchestration of the Android probe.
- Live CGI command construction.
- Native-style command header construction.
- Pulling raw channel dumps from the phone.
- Vue990 `55 AA 15 A8` channel/JPEG extraction.
- Still-image artifact verification.
- MJPEG AVI assembly.
- Reports/logs around the capture.

Important code areas:

- `A9Vue990CgiCommandBuilder`
- `A9Vue990ChannelMediaExtractor`
- `A9MjpegAviWriter`
- `A9AndroidPhoneCaptureClient`
- Android probe `Vue990PpcsSession`

## What Is Still Native

The following pieces are still native and are the reason this is not yet a pure
C# library:

- `JNIApi.create`
- `JNIApi.clientSetVuid`
- `JNIApi.connect`
- `JNIApi.login`
- `JNIApi.write` as the raw native session writer
- native `client_read` as the channel reader

So the situation is:

- C# knows the live-open command.
- C# knows how to decode/save media once channel bytes exist.
- C# does not yet fully own the session transport that creates and carries
  those channel bytes.

## Windows Status

Windows can orchestrate the successful Android run and download the image/video
artifacts.

Windows-only direct camera capture is not working yet. That is expected right
now because the protocol is not fully ported. Windows Firewall is not the
current primary suspect; the same live-CGI failures happened on Android until
we matched the native command framing.

Windows should come after Android proves the managed session carrier. Moving to
Windows before that would probably repeat old failed probes.

## Phase Status

Current controlling roadmap:

- `csharp-only-vue990-roadmap.md`

Important recent phases:

- Phase 39: Android C# UDP/HLP2P opener closed as negative evidence.
- Phase 40: Native channel oracle exposed JPEG-in-envelope media.
- Phase 41: C# live-CGI command framing succeeded and produced image/video.
- Phase 42: Planned next step, replace the remaining native session transport.
  Current Phase 42 evidence shows Android Wi-Fi permission/routing is no
  longer the managed-direct blocker; the exact native HLP2P session-open
  handshake is still missing.
- Phase 43: Native HLP2P debug logs captured the real successful local
  LAN-hole path and C# now preserves the full DAS connect descriptor.
- Phase 44: Next focused phase for a managed C# LAN-hole opener on Android
  phone Wi-Fi.
  First Phase 44 oracle captured native HLP2P helper vectors for
  `create_LstReq`, `create_PunchPkt`, `create_P2pRdy`, and `create_P2pReq`.
  Static follow-up mapped `pack_ClntPkt` and confirmed the final send shape is
  the existing managed no-padding shape: `header + P2P id`, or
  `header + P2P id + reverse address` for `P2pReq4`.

Phase 42 goal:

- Receive the same media bytes from managed C# transport on Android.
- Save a still and MJPEG AVI without `JNIApi.writeCgi`, `JNIApi.write`, native
  `client_read`, or `AppPlayerApi`.

## Do Not Repeat Yet

Do not spend time on these again unless new evidence changes the camera state:

- Broad HTTP media URL scans.
- RTSP probing.
- Generic UDP port matrices.
- Fixed native relay packet replay without dynamic session context.
- Decoded TCP relay retries as the primary path.
- Phone socket snapshots as the main strategy.
- FFmpeg/LibVLC/H.264 work before H.264 bytes are proven.

## Recommended Next Work

1. Keep the Phase 41 live-open command fixed.
2. Map the native LAN-hole connect/login/session carrier.
3. Replace native raw read/write with managed channel transport on Android.
4. Save raw managed channel bytes.
5. Feed those bytes into the existing C# extractor and MJPEG writer.
6. Only after Android works without native session calls, port the same path to
   Windows.

## How You Can Help

- Keep the camera powered.
- Keep the Android phone connected to `@MC-0025644` when asked for Android
  proof runs.
- Close or force-stop Vue990 before pure probe runs so it does not own the
  camera session.
- If a run suddenly fails, confirm whether the phone still sees the camera Wi-Fi
  and whether the camera battery/light state changed.

## Bottom Line

We are past blind probing. The live media format and live-open command are now
known and partially ported to C#. The remaining hard problem is the native
Vue990/PPCS session transport. Phase 42 is the right next place to work.
