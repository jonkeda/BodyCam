# C#-Only Vue990 Stream Roadmap

**Status:** Active controlling roadmap

## Goal

Retrieve a real still image and short video from `@MC-0025644` using C# protocol
code that can run on Android first and Windows later.

Android is the current proving ground because the phone is already on the
camera Wi-Fi and Vue990 proves the camera can stream there. Android is not the
target architecture and not a relay. It is where we remove uncertainty about
the protocol before moving the same C# code to Windows.

## Current Truth

- The vendor-backed Android path works and has produced real JPEG and MJPEG AVI
  artifacts.
- That path still depends on `libOKSMARTPPCS.so` and `libOKSMARTPLAY.so`.
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
  and Phase 40 produced a raw channel oracle plus extracted image/video
  artifacts.

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
- Native transport/session carrying still remains.

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

Do not proceed to Windows until at least the second gate is true on Android.

Immediate next step:

- Keep the confirmed live-open command fixed and replace the native session
  carrier through the Phase 43/44 LAN-hole path: connect/login/read/write
  transport.

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

### 5. Move The Working C# Path To Windows

Purpose:

- Reuse the Android-proven C# protocol code from Windows.

Allowed experiments:

- Windows firewall changes only after Android proves the C# protocol.
- Prefer same packet sequence and credentials.
- Only add Windows-specific socket binding/firewall work if packet evidence
  shows Windows is failing after sending the known-good sequence.

Exit criteria:

- Windows C# saves a real JPEG and video from `@MC-0025644`.
- The Android proving app is no longer required for normal capture.

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
- Future: Windows C# port of the proven Android path.

If a task does not move one of those gates, record it in the realtests log
instead of creating another phase.

## How The User Can Help

- Keep the camera powered and the phone connected to `@MC-0025644`.
- When asked, open Vue990 and confirm live view is visible.
- When asked for a pure C# run, close or force-stop Vue990 so it does not own
  the camera session.
- If comfortable, temporarily disable mobile data or VPN during one vendor
  live-view sample so the native path is easier to interpret.
