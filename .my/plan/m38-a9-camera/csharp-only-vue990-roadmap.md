# C#-Only Vue990 Stream Roadmap

**Status:** Current camera complete - Windows C# capture repeatability proved

Solution document:
`.my/plan/m38-a9-camera/vue990-csharp-capture-solution.md`

## Goal

Retrieve a real still image and short video from `@MC-0025644` using C# protocol
code that can run on Android and Windows.

Android was the proving ground because the phone was already on the camera
Wi-Fi and Vue990 proved the camera could stream there. Windows is now also
proven for direct capture when the laptop is connected to the camera Wi-Fi.

## Current Truth

- The vendor-backed Android path works and has produced real JPEG and MJPEG AVI
  artifacts.
- The first Android C#-only runtime path now saves a real still image and short
  MJPEG AVI without calling the native Vue990/PPCS session APIs.
- The first Windows C# direct path now saves a real still image and short MJPEG
  AVI from the laptop while connected directly to `@MC-0025644`.
- Windows and Android both reach camera HTTP status on `192.168.168.1:81`.
- Direct HTTP/RTSP/MJPEG endpoints have not produced media.
- Classic PPPP/HLP2P UDP discovery and broad local UDP matrices have not made
  the camera answer managed C#.
- Managed C# now reproduces known native TCP relay packet vectors, but decoded
  relays still do not return bytes.
- Socket sampling during a working native-backed stream did not reveal a simple
  standalone media socket that can be copied directly.
- Phase 40 exposed the native channel payload: live video is JPEG frames in a
  `55 AA 15 A8` / 32-byte Vue990 channel envelope. C# can now extract still
  frames and assemble MJPEG AVI from those channel bytes.
- Phase 41 replaced native `writeCgi` framing with C#-generated protocol bytes:
  an 8-byte command-channel header plus credentialed live-stream CGI body on
  channel `0`. That C# command started the stream and produced a real still plus
  MJPEG AVI while native connect/login/read still carried the session.
- Phase 47 replaced the Android runtime session carrier with managed C# for the
  local HLP2P direct path. It uses the compact LAN-hole handshake, C# direct
  ACKs, and native-paced post-hole control ordering to receive `55 AA 15 A8`
  media bytes and save image/video artifacts.
- Phase 48 moved the same path to Windows C# and saved
  `managed-direct-still.jpg` plus `managed-direct-video-mjpeg.avi`.
- Phase 49 splits out the final hardening work: control derivation or scoped
  vector documentation, shared capture API cleanup, and repeatable final proof
  captures.
- Phase 49 succeeded for the current camera: the post-hole controls are named
  scoped vectors, Windows and Android share that provider, and two fresh Windows
  proof runs saved still/video artifacts.

## Roadmap

### 1. Freeze Baseline Evidence

Purpose:

- Keep the working native-backed capture as the oracle.
- Keep the failed direct HTTP, classic PPPP, fixed UDP, and fixed relay attempts
  as negative evidence.

Exit criteria:

- A fresh native-backed image/video artifact exists.
- The latest managed C# attempt is documented with exact result paths.
- The artifact list distinguishes native-backed, C#-orchestrated, and C#-only.

Status:

- Done for the current evidence set. Phase 39 is closed as a negative result,
  Phase 40 produced a raw channel oracle plus extracted image/video artifacts,
  Phase 47 produced a C#-only Android runtime image/video capture, and Phase 48
  produced a Windows C# direct image/video capture.

### 2. Build A Native Behavior Oracle

Purpose:

- Use the vendor native libraries only as measuring tools.
- Extract the real connection, channel, and media message sequence instead of
  guessing more packet shapes.

Allowed experiments:

- Call exported native helpers in-process from the Android C# probe.
- Log native-created packets and native channel states.
- Dump bounded channel payload prefixes after `connect`, `login`, and
  `livestream.cgi?streamid=10&substream=0&`.
- Compare native success against managed failure in one report.

Exit criteria:

- We know which API or packet opens the working local session.
- We have message lengths, types, and prefixes for the first successful control
  and media payloads.
- We can describe what the managed C# opener is missing.

Result:

- Phase 40 succeeded for channel bytes and media shape.
- Phase 41 identified native `writeCgi` framing and replaced it with C#
  command bytes.
- Phase 47 replaced native runtime carrying on Android, with the caveat that the
  encrypted post-hole controls are still native-observed vectors.

### 3. Port The Session Opener To C#

Purpose:

- Replace the native session-open portion with managed C# while still running
  on Android.

Allowed experiments:

- Send only packet shapes learned from the oracle.
- Use Android Wi-Fi binding and multicast locks already implemented.
- Use existing C# packet parsers, ACK builders, control command builders, and
  frame assembler.

Exit criteria:

- First gate: managed C# receives a non-self packet from the camera or relay.
- Second gate: managed C# receives a valid control/channel payload after login.
- Final gate: managed C# receives media bytes after the live CGI command.

Android status:

- Done for the current camera. Phase 47 passed the final Android gate and saved
  still/video artifacts from managed C# transport.

Immediate next step:

- Current-camera hardening is done in Phase 49. A future phase should only be
  created if broader control derivation or multi-camera compatibility is needed.

### 4. Decode And Save Media In C#

Purpose:

- Turn received channel/media bytes into a durable image and video artifact.

Allowed experiments:

- Reuse the existing `55 AA 15 A8` chunk reassembly work if the payload matches.
- Save raw payload dumps before attempting decode.
- Write MJPEG AVI when frames are JPEG.
- Add H.264 decode only after H.264 NAL units are proven in raw bytes.

Exit criteria:

- A C#-only JPEG is saved on Android.
- A C#-only video artifact is saved on Android.
- Hash, dimensions, byte count, and artifact path are written to the report.

Status:

- Done on Android in Phase 47 and on Windows in Phase 48.

### 5. Move The Working C# Path To Windows

Purpose:

- Reuse the Android-proven C# protocol code from Windows.

Allowed experiments:

- Prefer same packet sequence and credentials.
- Only add Windows-specific socket binding/firewall work if packet evidence
  shows Windows is failing after sending the known-good sequence.

Exit criteria:

- Windows C# saves a real JPEG and video from `@MC-0025644`.
- The Android proving app is no longer required for normal capture.

Status:

- Done for the current camera in Phase 48. Windows saved
  `managed-direct-still.jpg`, `640x480`, `9123` bytes, SHA-256
  `52444D62CF8E3F2520F1436F57E02E26FCF3D26323C6FFD8739E5C6AE0E6CE30`.
- Windows saved `managed-direct-video-mjpeg.avi`, `12` frames, `640x480`,
  `110204` bytes, SHA-256
  `8C07FC2095F84209C52B06A16BE80A972E8C67CE8C00D566BDB63A684D74FC87`.

## Do Not Repeat Without New Evidence

- Do not rerun broad HTTP media URL scans unless firmware, port list, or status
  metadata changes.
- Do not rerun RTSP probing unless a new RTSP-like port opens.
- Do not rerun generic UDP port matrices unless the packet payloads changed.
- Do not replay fixed native TCP relay packets without adding the missing
  dynamic session context.
- Do not rely on phone socket snapshots alone; they already failed to expose
  the working media path clearly.
- Do not add FFmpeg, LibVLC, or H.264 decode work until real stream bytes prove
  the codec.

## When To Create A Phase Doc

Create a new phase only when it moves one roadmap gate:

- Phase 40: native channel/session oracle.
- Phase 41: managed C# session opener from oracle output.
- Phase 42: managed C# transport/read replacement on Android.
- Phase 43: native HLP2P `ConnectByServer` / LAN-hole map.
- Phase 44: managed C# LAN-hole opener on Android.
- Phase 47: Android managed HLP2P direct capture saved image/video.
- Phase 48: Windows managed HLP2P direct capture saved image/video and now
  tracks encrypted control derivation.
- Phase 49: final C# hardening after the stream goal succeeded. This phase is
  for control derivation/scoping, API cleanup, and repeatability proof.
- Future: create another phase only for a new gate, such as generated
  post-hole controls proven across more than one camera/firmware or
  multi-camera compatibility.

If a task does not move one of those gates, record it in the realtests log
instead of creating another phase.

## How The User Can Help

- Keep the camera powered and the phone connected to `@MC-0025644`.
- When asked, open Vue990 and confirm live view is visible.
- When asked for a pure C# run, close or force-stop Vue990 so it does not own
  the camera session.
- If comfortable, temporarily disable mobile data or VPN during one vendor
  live-view sample so the native path is easier to interpret.
