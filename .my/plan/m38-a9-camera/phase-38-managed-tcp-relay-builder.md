# Phase 38 - Managed TCP Relay Builder

**Status:** Completed - native TCP relay framing ported, live relay still silent

## Goal

Replace fixed native-oracle relay packet replay with managed C# packet
construction for the Vue990/OKSMART TCP relay session-open path.

This phase exists because Phase 37 ruled out another local Android HTTP/UDP
guessing pass. The C# client now needs to reproduce the native `TCPSend_MSG`
wrapper and generated `TCPRlyReq` / `TCPRSLgn` packets.

## Implemented

- Added `A9Vue990TcpRelayPacketBuilder`.
- Ported the native TCP relay wire shape:
  `len:BE16 68 00 seed0 seed1 crc0 crc1 ciphertext`.
- Ported the native TCP relay CRC.
- Built managed plain structs for:
  - `TCPRlyReq`, internal length `0x38`
  - `TCPRSLgn`, internal length `0x3c`
- Confirmed the serialized body starts with the VUID fields, not the client id.
- Applied the two-layer encryption used by native `TCPSend_MSG`:
  - first layer: client id as the proprietary key
  - second layer: two-byte random seed rendered as `%02X%02X`
- Added deterministic unit tests using native Phase 32 seed `67 C6`.
- Wired generated managed relay candidates into `A9Vue990RelayHelloProbeClient`
  before the older fixed native-oracle constants.

## Verification

Focused Vue990 tests:

- `dotnet test .\src\BodyCam.Tests\BodyCam.Tests.csproj --filter "FullyQualifiedName~A9Vue990" --no-restore -m:1`
- Result: passed `29/29`.

Android probe build:

- `dotnet build .\tools\BodyCam.A9PhoneProbe\BodyCam.A9PhoneProbe.csproj -f net10.0-android --no-restore -m:1`
- Result: passed with `0` warnings and `0` errors.

## Live Windows Relay Run

Artifact:

- `.my/plan/m38-a9-camera/captures/phase-38-managed-relay-builder-2026-05-29-160830.json`

Observed:

- Windows had Ethernet internet and camera Wi-Fi active.
- Decoded DAS relay hosts were:
  - `47.98.128.117`
  - `120.78.3.33`
  - `47.109.80.221`
- Managed C# generated `TCPRlyReq` / `TCPRSLgn` packets were sent to decoded
  TCP `65527` relays.
- Most relay sockets opened and accepted bytes.
- No relay returned response bytes.
- No image or video artifact was produced.

## Interpretation

The packet builder is no longer the weak spot for the tested native vectors:
managed C# can reproduce native `TCPSend_TCPRlyReq` and `TCPSend_TCPRSLgn`
byte-for-byte when given the same seed.

The live relay still does not answer. That suggests the relay path needs more
native session context than the current standalone request provides, or that
the working phone app is using a different local transport while associated
with the camera AP.

## Next Work

- Treat the standalone TCP relay builder as verified for known native vectors,
  but not sufficient for live media.
- Continue in Phase 39 with the Android C# direct stream target.
- Focus Phase 39 on the native UDP session opener observed during successful
  native-backed streaming: UDP `65529` plus dynamic UDP sockets, no observed TCP
  media/session socket.

## Checklist

- [x] Create managed TCP relay frame builder.
- [x] Reproduce native `TCPRlyReq` vector.
- [x] Reproduce native `TCPRSLgn` vector.
- [x] Add focused unit tests.
- [x] Link the builder into the Android C# probe.
- [x] Send generated packets to decoded Windows-accessible relays.
- [x] Document that no live relay response bytes were returned.
- [ ] Retrieve image/video using pure C#.
