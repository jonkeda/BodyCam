# Phase 39 - Android C# UDP Session Opener

**Status:** Completed as negative result - continue Phase 40

## Goal

Build the first C#-only Vue990/A9 media path on Android.

Android is the first target because the phone is already connected to the
camera Wi-Fi and can prove the camera is reachable. This phase is not an
Android relay. The goal is managed C# code that opens the camera session and
retrieves image/video bytes on Android, with the same protocol code later
portable to Windows.

## Current Evidence

- Native-backed Android capture works and has downloaded real still images and
  MJPEG AVI video artifacts.
- That working path still depends on `libOKSMARTPPCS.so` and
  `libOKSMARTPLAY.so`, so it is C#-orchestrated but not C#-only.
- Managed C# direct HTTP media probing finds only `get_status.cgi`; media URLs
  return no JPEG/MJPEG/H264 bytes.
- Managed C# local UDP probing with Wi-Fi bind, multicast lock, and UDP ports
  `32108`, `20190`, `65529`, and `65531` has only produced self-echoes.
- Managed C# TCP relay packet construction now reproduces native
  `TCPRlyReq` / `TCPRSLgn` vectors byte-for-byte, but live decoded relays still
  return no bytes.
- ADB socket sampling during a successful native-backed stream showed UDP
  sockets under the BodyCam probe UID:
  - `0.0.0.0:65529`
  - dynamic UDP ports, including observed `37623` and `39235`
- The same sampling did not show a TCP media/session socket to the camera.

## Interpretation

The next blocker is likely the Vue990/OKSMART UDP session opener, not Android
routing, Windows Firewall, HTTP endpoints, or the already-ported TCP relay
message wrapper.

The native library appears to create a stable UDP listener on `65529` and then
use one or more dynamic UDP sockets for live session traffic. The managed C#
probe can bind those kinds of sockets, but it does not yet send the native
session-open packets that cause the camera to answer.

## 2026-05-29 Outcome

- Added a shared C# `A9Vue990Hlp2pPacketBuilder`.
- Ported native-observed HLP2P packet headers, compact P2P ID layout, and IPv4
  reverse-address layout into managed C#.
- Added focused tests for LAN search, P2P ID construction, reverse address
  construction, and full `P2pRequest4` bytes.
- Linked the builder into the Android managed-direct probe.
- Cleaned local endpoint selection so managed C# does not advertise
  `0.0.0.0:<port>` as a usable callback address.
- Focused Vue990 tests passed `34/34`.
- Android probe build passed for `net10.0-android`.
- A live managed Android run completed, but still saved no C#-only image or
  video and still did not receive the required remote session response.
- A native-backed comparison run still saved a real image and MJPEG AVI,
  confirming the camera and phone path work.

Conclusion: Phase 39 produced useful C# packet builders and negative evidence,
but another generic managed UDP run would repeat the same hypothesis. Continue
with [Phase 40](./phase-40-native-channel-session-oracle.md), the native
channel/session oracle.

## Plan

1. Reverse the native UDP session opener.
   - Search `libOKSMARTPPCS.so` for `sendto`, `recvfrom`, UDP bind/create
     paths, and `PPCS_PktSend` / `PPCS_PktRecv` / `HLP2P_PktSend` /
     `HLP2P_PktRecv`.
   - Locate references to UDP bind logs and the `UDP_Port` connect argument.
   - Confirm whether `65529` is a fixed API argument, a default, or a derived
     port.

2. Capture the native UDP shape.
   - Prefer in-process observations from our own Android probe where possible.
   - Record local ports, remote endpoints, packet lengths, and packet prefixes.
   - Save bounded raw packet samples if a safe app-local capture route is
     available.

3. Port the smallest opener to C#.
   - Add a managed Android probe mode or extend managed-direct with a
     `phase-39` UDP opener attempt.
   - Reuse existing C# PPCS packet parsing, proprietary codec, CGI live-open,
     video frame assembler, and MJPEG AVI writer.
   - First success criterion: receive any non-self camera packet.
   - Second success criterion: receive channel bytes after login/live-open.
   - Final success criterion: save a JPEG still and MJPEG AVI from C# only.

4. Promote to Windows.
   - Once Android C# receives media bytes, move the same protocol code into the
     shared library and rerun from Windows.

## Checklist

- [x] Confirm native-backed Android still downloads real image/video.
- [x] Confirm current managed C# TCP relay builder matches native vectors.
- [x] Confirm current managed C# direct HTTP/UDP attempts still do not download
  image/video.
- [x] Observe native-backed stream socket shape through ADB sampling.
- [ ] Reverse native UDP session-open functions and packet constructors.
- [x] Implement the next managed C# UDP opener attempt on Android.
- [ ] Retrieve a C#-only image on Android.
- [ ] Retrieve a C#-only video artifact on Android.
- [ ] Port the working C# path to Windows.
