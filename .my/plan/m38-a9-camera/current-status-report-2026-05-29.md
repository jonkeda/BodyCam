# M38 A9/Vue990 Current Status Report - 2026-05-29

## Short Answer

Update on 2026-05-30: the Windows direct path now works in C#. The laptop
connected to `@MC-0025644`, reached the camera at `192.168.168.1`, and saved
both a real still image and a short MJPEG AVI.

The earlier Android runtime path also works in C# without native Vue990/PPCS
session calls.

Solution document:
`.my/plan/m38-a9-camera/vue990-csharp-capture-solution.md`

Important caveat: the current working C# path still replays four encrypted
post-hole control payloads captured from the native app. So runtime execution is
C# only on both Android and Windows, but the next hardening step is to derive
those payloads in C# instead of using native-observed vectors.

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

Latest Phase 47 Android C#-only runtime success:

- Laptop Wi-Fi was not used; Windows controlled the Android phone over USB/ADB.
- Directory:
  `.my/plan/m38-a9-camera/captures/phase-47-managed-hlp2p-direct-paced-2026-05-30-001842/`
- Phone Wi-Fi: `@MC-0025644`, `192.168.168.100/24`
- Camera endpoint after compact LAN-hole: `192.168.168.1:10654`
- Runtime native Vue990/PPCS session calls used by this run: none
- Still:
  `managed-direct-still.jpg`, `640x480`, `9487` bytes, SHA-256
  `9C124F13027538D726D2E72A83F06D5B03B08573FDD5A53B79DFD685B6A0A951`
- Video:
  `managed-direct-video-mjpeg.avi`, `12` frames, `640x480`, `113896` bytes,
  SHA-256
  `F08D052541F4A902E1F278509A9D09E4D73F1E58DC01E902C575504EABD512FB`
- Raw managed channel dump:
  `managed-hlp2p-direct-channel.bin`, `1554712` bytes, SHA-256
  `F39B4242E5EC16E407317ED3AE1B68AC8D57D1E18078F2C924B668E2DC4533FD`
- Focused direct-packet tests passed: `5/5`
- Android phone probe build passed for `net10.0-android`

Latest Phase 48 Windows C# direct capture success:

- Laptop Wi-Fi was connected directly to `@MC-0025644` as
  `192.168.168.101/24`.
- Camera host: `192.168.168.1`; stream endpoint after compact LAN-hole:
  `192.168.168.1:2951`.
- Directory:
  `.my/plan/m38-a9-camera/captures/phase-48-windows-direct-2026-05-30-004023/`
- Runtime native Vue990/PPCS session calls used by this run: none.
- Still:
  `managed-direct-still.jpg`, `640x480`, `9123` bytes, SHA-256
  `52444D62CF8E3F2520F1436F57E02E26FCF3D26323C6FFD8739E5C6AE0E6CE30`
- Video:
  `managed-direct-video-mjpeg.avi`, `12` frames, `640x480`, `110204` bytes,
  SHA-256
  `8C07FC2095F84209C52B06A16BE80A972E8C67CE8C00D566BDB63A684D74FC87`
- Raw managed channel dump:
  `managed-hlp2p-direct-channel.bin`, `1849872` bytes, SHA-256
  `259A9CBB5AC0DDAA0D7AEC979E3E3EE1F40A90BFCD10D2A49A223E2A4117023C`
- Focused HLP2P direct tests passed: `6/6`
- `tools/BodyCam.A9Probe` build passed.

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

Latest Phase 44 Android-only managed LAN-hole attempt:

- Laptop Wi-Fi was still not used; Windows only controlled the phone over
  USB/ADB.
- Directory:
  `.my/plan/m38-a9-camera/captures/phase-44-managed-lan-hole-local-2026-05-29-221252/`
- Phone Wi-Fi: `@MC-0025644`, `192.168.168.100/24`
- Added `A9Vue990ConnectByServerState` to preserve decoded DAS tokens, client
  id, VUID, credentials, local endpoint, and native structured P2P IDs in C#.
- Added Android `managed_lan_hole` autorun mode and Windows/ADB launch support.
- Focused Vue990 tests passed `46/46`.
- Android phone probe build passed for `net10.0-android`.
- Fixed UDP `65529` sent the confirmed basic HLP2P list/punch/ready/P2P request
  packet burst and received only self-echo packets from `192.168.168.100`.
- Ephemeral UDP sent the same focused burst and received no responses.
- No non-self camera response was captured.

Latest Phase 45 local dev update:

- Created
  `.my/plan/m38-a9-camera/phase-45-native-lan-hole-session-engine-map.md`.
- Static native mapping shows `_clientSessionToSetup` sends a narrower setup
  subset: client-id `ListReq`, client-id `P2PReq4`, and `LanSearch`.
- Native alive helpers are now mapped as header-only packets:
  `F1E00000` and `F1E10000`.
- C# now has builders/tests for that setup subset and alive packet headers.
- Android managed LAN-hole mode now sends the native setup subset first and
  includes decoded DAS relay hosts as candidate targets.
- This is still not a C#-only image/video success; the exact `_se_lan_hole`
  request and `dev lan hole` / `dev lan hole ack` response fields remain the
  blocker.

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
- `A9Vue990ConnectByServerState`
- `A9AndroidPhoneCaptureClient`
- Android probe `Vue990PpcsSession`

## What Is Still Native

The Phase 47 runtime capture does not call these native session APIs anymore:

- `JNIApi.create`
- `JNIApi.clientSetVuid`
- `JNIApi.connect`
- `JNIApi.login`
- `JNIApi.write` as the raw native session writer
- native `client_read` as the channel reader

For historical/native-backed paths, these APIs still exist in the repo. For the
new C# path, the remaining non-clean-room piece is not a runtime native call; it
is the hardcoded encrypted post-hole control vectors that were learned from the
native socket hook.

So the situation is:

- C# has a working Android runtime transport.
- C# knows how to decode/save media once channel bytes exist.
- C# does not yet derive every encrypted control payload itself.

## Windows Status

Windows can now do the direct camera capture itself when the laptop is connected
to the camera Wi-Fi. The successful Phase 48 run saved both `managed-direct-still.jpg`
and `managed-direct-video-mjpeg.avi` from Windows C#.

Windows Firewall is not the current blocker. The laptop sent the known-good
sequence, received camera packets, ACKed the media stream, and captured JPEG
frames. The remaining blocker is protocol hardening: replacing static
native-observed encrypted control vectors with C# generation.

## Phase Status

Current controlling roadmap:

- `csharp-only-vue990-roadmap.md`

Important recent phases:

- Phase 39: Android C# UDP/HLP2P opener closed as negative evidence.
- Phase 40: Native channel oracle exposed JPEG-in-envelope media.
- Phase 41: C# live-CGI command framing succeeded and produced image/video.
- Phase 42: Older transport-replacement phase. Its evidence remains useful,
  but Phase 47 and Phase 48 superseded its "missing session-open" blocker for
  the current camera by proving the compact direct path.
- Phase 43: Native HLP2P debug logs captured the real successful local
  LAN-hole path and C# now preserves the full DAS connect descriptor.
- Phase 44: Next focused phase for a managed C# LAN-hole opener on Android
  phone Wi-Fi. It is now blocked by Phase 45 because the first focused C#
  helper-burst attempt produced only self-echo/no-response.
  First Phase 44 oracle captured native HLP2P helper vectors for
  `create_LstReq`, `create_PunchPkt`, `create_P2pRdy`, and `create_P2pReq`.
- Phase 47: Android managed HLP2P direct C# runtime capture succeeded and saved
  image/video. It proved the native-paced compact direct sequence.
- Phase 48: Windows managed HLP2P direct C# runtime capture succeeded and saved
  image/video. The Windows porting goal is done for the current camera; the
  remaining Phase 48 task is control payload derivation.
- Phase 49: Final C# hardening succeeded for the current camera. The post-hole
  controls are centralized in a named scoped-vector provider, Windows and
  Android share that provider, normal reports no longer call them replay
  controls, and two fresh Windows captures saved image/video artifacts.
- Phase 45: Native session-engine mapping remains historical evidence. Its
  basic helper burst failure was bypassed by the later compact LAN-hole/direct
  sequence.

Current Phase 49 goal:

- Preserve the working Android and Windows C# capture sequence as a regression
  path.
- Keep the encrypted post-hole control payloads deliberately scoped to this
  Vue990/BK7252N camera class until a later multi-camera derivation phase.
- Keep image/video capture verified with paths, sizes, hashes, and dimensions.

Latest Phase 49 proof artifacts:

- Run 1:
  `.my/plan/m38-a9-camera/captures/phase-49-final-windows-direct-2026-05-30-010401/`
  saved `managed-direct-still.jpg`, `640x480`, `8104` bytes, SHA-256
  `F36EF09D8BBFA5A8330D9BE54F46158E9AAB4B2C37E13F9CB632F39B632A498D`,
  and `managed-direct-video-mjpeg.avi`, `12` frames, `640x480`, `97868`
  bytes, SHA-256
  `3E1EA8F16061840F039422C6C38C5F31F4F5179C020FC87BD5CAE97FFF83E80A`.
- Run 2:
  `.my/plan/m38-a9-camera/captures/phase-49-final-windows-direct-2026-05-30-010441/`
  saved `managed-direct-still.jpg`, `640x480`, `8152` bytes, SHA-256
  `5DF8B1778937805BE84EAA86ED6CC9802CE64209908F1AC36AD9BDFD848F5516`,
  and `managed-direct-video-mjpeg.avi`, `12` frames, `640x480`, `98312`
  bytes, SHA-256
  `0D2825A46C5C8AA6D93FFBB60936A740A0D638C21C7E399D9B1C5435ED8D6BA2`.

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

1. Treat the current Vue990/BK7252N camera path as working for Windows C#
   capture.
2. Keep Phase 49 proof captures as the current regression evidence.
3. Only start a new phase if we need broader compatibility, such as deriving
   post-hole controls across another camera/firmware or supporting multiple
   Vue990 variants.

## How You Can Help

- Keep the camera powered.
- Keep the Android phone connected to `@MC-0025644` when asked for Android
  proof runs.
- Close or force-stop Vue990 before pure probe runs so it does not own the
  camera session.
- If a run suddenly fails, confirm whether the phone still sees the camera Wi-Fi
  and whether the camera battery/light state changed.

## Bottom Line

We are past blind probing, and the direct capture path now works from both
Android C# and Windows C#. For this camera, the practical goal is done: C# can
download still images and MJPEG video directly. The remaining work is optional
broader compatibility: deriving the encrypted post-hole controls instead of
using the now-documented scoped vectors.
