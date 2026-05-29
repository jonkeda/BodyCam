# Phase 47 - Managed HLP2P Direct C# Capture

**Status:** Succeeded on Android - C# transport saved image and video

## Goal

Use only managed C# code at runtime to connect from the Android phone to
`@MC-0025644`, receive the Vue990/HLP2P media channel, and save a still image
plus a short video. Laptop Wi-Fi is not used; Windows only builds, installs,
launches, and pulls artifacts over USB/ADB.

## Result - 2026-05-30

Successful run:

- Directory:
  `.my/plan/m38-a9-camera/captures/phase-47-managed-hlp2p-direct-paced-2026-05-30-001842/`
- Phone Wi-Fi: `@MC-0025644`, `192.168.168.100/24`
- Camera endpoint after LAN-hole: `192.168.168.1:10654`
- Android app: `com.bodycam.a9phoneprobe`
- Native Vue990/PPCS session calls used by this run: none
- Captured image: true
- Captured video: true

Artifacts pulled back to Windows:

- Still:
  `managed-direct-still.jpg`
- Still dimensions: `640x480`
- Still size: `9487` bytes
- Still SHA-256:
  `9C124F13027538D726D2E72A83F06D5B03B08573FDD5A53B79DFD685B6A0A951`
- Video:
  `managed-direct-video-mjpeg.avi`
- Video frames: `12`
- Video dimensions: `640x480`
- Video size: `113896` bytes
- Video SHA-256:
  `F08D052541F4A902E1F278509A9D09E4D73F1E58DC01E902C575504EABD512FB`
- Raw managed channel dump:
  `managed-hlp2p-direct-channel.bin`, `1554712` bytes, SHA-256
  `F39B4242E5EC16E407317ED3AE1B68AC8D57D1E18078F2C924B668E2DC4533FD`

## Working C# Sequence

The breakthrough was not a new endpoint or firewall change. The camera required
the native-paced post-LAN-hole control order:

1. Send compact LAN-hole opener from the observed run-2 seed.
2. Parse camera response:
   `11 B8 36 B4 25 2E EA 4A 01 13 00 6B 25 35 01`.
3. Send LAN-hole ACK:
   `11 13 B8 36 B4 25 2E EA 4A 01 D6 25 35 01 CB 69 E6 29`.
4. Parse ready:
   `15 B8 36 B4 25 6B 25 35 01`.
5. Send compact alive probe `0B0000` and accept `0C`.
6. Send replay control packets in the native-paced order:
   - control `0`
   - control `1`
   - short wait
   - control `2`
   - control `3`
   - short wait
   - control `1` again
7. Receive the 830-byte command response and ACK it with:
   `0D00010800000000000331`.
8. Resend control `3`.
9. Receive ACKs for controls `2` and `3`.
10. Receive the 62-byte command response and ACK it with:
    `0D00020800000100010031`.
11. Receive the `55 AA 15 A8` video envelope header.
12. ACK each direct media packet as `0D [rxSeq+1] 08 00 [messageId] [rxSeq] [tailLength-8]`.
13. Reassemble payloads with `A9Vue990VideoFrameAssembler`.
14. Extract JPEGs and write `managed-direct-still.jpg` plus
    `managed-direct-video-mjpeg.avi`.

## Code Added Or Changed

- Added `A9Vue990Hlp2pDirectPacket` for compact LAN-hole, ready, direct media,
  and ACK packet parsing/building.
- Added focused tests in `A9Vue990Hlp2pDirectPacketTests`.
- Added the shared packet helper to the Android phone probe project.
- Added managed HLP2P direct replay to `ManagedDirectMediaProbe`.
- Changed compact alive parsing so later `0B0002` probes are acknowledged.
- Changed replay timing to match the native trace rather than blasting all
  controls at once.

## What This Proves

- Android Wi-Fi routing and Windows Firewall are not the blocker for this path.
- The camera accepts the compact C# LAN-hole opener and C# ACKs.
- The camera sends the same post-hole command and media sequence to managed C#.
- C# can receive, ACK, reassemble, extract, and persist the live stream without
  calling the vendor native session API.

## Remaining Caveat

This is C#-only runtime code, but it is not yet a general clean-room protocol
implementation. The four post-hole control packets are still encrypted/native
observed byte vectors from the Phase 46 socket-hook sample. They work for the
current camera/session shape, but the next phase should replace those static
vectors with C# generation or documented derivation.

## Next Phase

Phase 48 should harden the C# implementation:

- Derive or regenerate the encrypted post-hole control payloads.
- Remove the misleading "replay" wording from the production path once the
  payloads are generated.
- Add a small public API around still/video capture.
- Port the same working sequence to Windows and only then handle Windows socket
  binding/firewall differences. This Windows port succeeded in Phase 48.

## Checklist

- [x] C# receives compact LAN-hole response from the camera.
- [x] C# receives compact ready from the camera.
- [x] C# reproduces direct ACKs for command and media packets.
- [x] C# reaches the 62-byte post-control response.
- [x] C# receives `55 AA 15 A8` media header.
- [x] C# saves a still image.
- [x] C# saves an MJPEG AVI.
- [ ] C# derives the encrypted post-hole control packets instead of replaying
      captured vectors.
- [x] Windows C# repeats the same capture without Android.
