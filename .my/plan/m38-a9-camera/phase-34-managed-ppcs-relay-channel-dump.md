# Phase 34 - Managed PPCS Relay and Channel Dump

**Status:** Completed for candidate replay - no stream bytes; moved to Phase 36 encryption work

## Goal

Move from local URL probing to the actual Vue990/PPCS transport in managed C#:
open enough of the session to receive bytes, send the known live-stream request
over the command/video channel path, and save a bounded raw `.bin` sample before
attempting image/video decoding.

This is still Android-first because the phone is already connected to
`@MC-0025644`, but all new protocol primitives live in shared C# so the same
code can run from Windows later.

## Why This Phase Exists

Phase 33 proved that the camera does not expose media through normal local HTTP,
MJPEG, RTSP, or LAN discovery responses. The Vue990 native path still streams,
so the missing piece is the proprietary PPCS/HLP2P channel layer, not image
decoding.

## Implemented So Far

- Added shared C# PPCS packet primitives:
  - `A9Vue990PpcsPacket`
  - `A9Vue990PpcsEncryptionCodec`
  - `A9Vue990VideoFrameAssembler`
- Linked shared C# Vue990 files into the Android probe APK:
  - `A9Vue990PpcsPacket`
  - `A9Vue990P2pPacketBuilder`
  - `A9Vue990StatusClient`
  - `A9Vue990DasServerParameter`
  - `A9Vue990RelayHelloProbeClient`
- Added bounded relay/session-open fallback to `ManagedDirectMediaProbe`.
- Added response `.bin` saving in `A9Vue990RelayHelloProbeClient` if any relay
  candidate returns bytes.

## Live Run

Artifact:

- `.my/plan/m38-a9-camera/captures/phase-34-android-relay-fallback-2026-05-29-151145.json`
- report:
  `.my/plan/m38-a9-camera/captures/phase-33-android-managed-direct-2026-05-29-151153/a9-android-managed-direct-2026-05-29-151153.txt`

Observed:

- Phone remained on `@MC-0025644` as `192.168.168.101/24`.
- Local camera status on `192.168.168.1:81` still worked.
- Direct media stayed empty: no JPEG frames and no video-like HTTP payloads.
- UDP discovery still produced only phone self-echoes.
- DAS decoded to relays:
  - `47.98.128.117`
  - `120.78.3.33`
  - `47.109.80.221`
- Android tried the first eight native-derived relay candidates against each
  relay on TCP `65527`.
- All 24 Android relay attempts timed out before opening.
- No raw `.bin`, image, or video artifact was produced.

## Interpretation

The Android managed C# path can parse the camera status and DAS relay list, but
while the phone is connected to the camera AP it does not currently open TCP
connections to the external relays. That means Android is a good host for local
camera probing, but not yet a useful host for relay testing unless its network
routing permits mobile data or another internet path while Wi-Fi stays on the
camera.

Windows previously could open the decoded relay TCP sockets, so the next
relay/session work should run on Windows unless the phone is explicitly given
internet routing while still connected to the camera.

## Next Work

1. [x] Run the bounded C# relay/session-open probe from Windows again with raw
   `.bin` saving enabled.
2. [ ] Add parameterized C# builders for `TCPRlyReq` and `TCPRSLgn` instead of
   replaying only fixed native-oracle frames.
3. [ ] Use Phase 32 native oracle output to map dynamic fields:
   client id, VUID, relay token, session key, flags, endpoint bytes, and
   numeric fields.
4. [ ] If a relay returns bytes, save the full raw response and parse its PPCS
   envelope.
5. [ ] Send `A9Vue990CgiCommandBuilder.BuildLiveStreamRequest()` over the
   recovered command/channel path.
6. [ ] Feed incoming video-channel chunks through `A9Vue990VideoFrameAssembler`.
7. [ ] If chunks decode to JPEG frames, save still image and MJPEG AVI.

## Windows Cached Relay Follow-Up

Artifact:

- `.my/plan/m38-a9-camera/captures/phase-34-windows-relay-cached-2026-05-29-1520.json`

Observed:

- Windows had Ethernet plus camera Wi-Fi active.
- Decoded relay TCP `65527` sockets opened for most tested candidates.
- No candidate returned response bytes.
- No raw `.bin`, image, or video artifact was produced.

Interpretation:

The relays are reachable from Windows, but they are not responding to fixed
oracle replay frames. Phase 36 now owns the next step: build the encrypted
second-stage request in C# instead of replaying stale native-output bytes.

## How The User Can Help

- Keep the camera powered and available as `@MC-0025644`.
- For Android relay testing, enable a network mode where the phone can use
  mobile data while staying connected to the camera Wi-Fi.
- For Windows relay testing, keep wired Ethernet active while Windows joins the
  camera Wi-Fi only when local status/camera access is required.
