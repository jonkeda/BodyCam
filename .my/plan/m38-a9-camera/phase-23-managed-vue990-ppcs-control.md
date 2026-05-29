# Phase 23 - Managed Vue990 PPCS Control And Raw Channel

**Status:** In Progress - DAS field analyzed; managed transport endpoint still
unknown

## Goal

Implement the smallest Windows-native C# PPCS control path for `@MC-0025644`:
connect, login, send the known live-open CGI on channel `1`, and save a bounded
raw channel-byte dump.

This phase does not promise image/video decoding yet. Its output is the missing
protocol boundary needed before Phase 22 can honestly save Windows-native media.

## Inputs

- Camera/AP IP: `192.168.168.1`
- Status endpoint:
  `http://192.168.168.1:81/get_status.cgi?loginuse=admin&loginpas=888888`
- Client id: `BKGD00000100FMQLN`
- VUID / real device id: `BK0025644WBPD`
- Login: `admin` / `888888`
- `connectType=0x3F`
- `p2pType=1`
- `server` parameter: opaque `DAS-...` value from status
- Live CGI: `livestream.cgi?streamid=10&substream=0&`
- Live channel: `1`
- Known live-open ack from Android oracle: `type=24631`, `len=33`

## Work Plan

1. Add `Vue990DasServerParameter` to decode and describe the `DAS-...` field
   without assuming plaintext endpoints.
2. Run a bounded transport fingerprint against likely PPCS/HLP2P local ports
   without sending CGI or stream commands.
3. Add managed PPCS transport scaffolding with explicit variant names:
   PPCS, XQP2P, HLP2P.
4. Reuse the older `A9Protocol` only as a packet-shape reference; do not assume
   the cam-reverse UDP/MJPEG handshake works for Vue990.
5. Implement a bounded connect/login spike against the live camera.
6. Send the C# built CGI request for
   `livestream.cgi?streamid=10&substream=0&`.
7. Save the first raw channel `1` byte sample under
   `.my/plan/m38-a9-camera/captures/phase-23/`.
8. Update `ppcs-protocol-notes.md` with actual bytes before attempting decode.

## Checklist

- [x] Add managed C# CGI-over-PPCS frame builder.
- [x] Add unit tests for the CGI request frame.
- [x] Decode and document the `DAS-...` status field shape.
- [x] Run bounded Windows transport fingerprint probes on candidate PPCS/HLP2P
      ports.
- [x] Add hardware-gated RealTest for the transport fingerprint pass.
- [ ] Identify actual transport endpoint or relay negotiation.
- [ ] Implement managed connect handshake.
- [ ] Implement managed login.
- [ ] Send live-open CGI on channel `1`.
- [ ] Save bounded raw channel `1` bytes from Windows.
- [ ] Add hardware-gated RealTest for raw channel bytes.
- [ ] Hand the raw byte artifact back to Phase 22 for image/video extraction.

## Stop Conditions

Stop and document if:

- the `DAS-...` field requires server-side secrets not present in status/APK
  evidence;
- the native library uses undiscovered encryption or relay negotiation that
  cannot be reproduced safely;
- Windows can connect but never receives channel bytes after live-open.

## Current Evidence

- Windows can join `@MC-0025644` and fetch status through managed C#.
- On 2026-05-29 Windows joined `@MC-0025644` as `192.168.168.100/24`
  while wired Ethernet remained available; the status endpoint still returned
  `HTTP 200`.
- Added `A9Vue990DasServerParameter` and `BodyCam.A9Probe vue990-das`.
- Live DAS analysis artifact:
  `.my/plan/m38-a9-camera/captures/phase-23-das-analysis-2026-05-29.json`.
- The current camera `server` field decodes to a 96-byte payload with the
  known 16-byte magic `8ED76A3380D998ECDA94D6D805A36877`.
- The decoded DAS payload is not plaintext-looking, has entropy around
  `6.29` bits/byte, and the common-port heuristic found no plausible embedded
  IPv4 endpoint.
- The status response reported `isCharge=1`; battery rose from the earlier low
  reading to `25` during the live DAS run after USB power was connected.
- Direct HTTP media is ruled out for the tested endpoints: status returns
  `HTTP 200`, while snapshot/video/livestream candidates return `404`.
- Added `A9Vue990PpcsTransportProbeClient` and
  `BodyCam.A9Probe vue990-ppcs-transport`.
- Live transport fingerprint artifact:
  `.my/plan/m38-a9-camera/captures/phase-23/windows-ppcs-transport-2026-05-29.json`.
- Hardware-gated transport RealTest artifact:
  `.my/plan/m38-a9-camera/captures/phase-23/a9-windows-vue990-transport-realtest-2026-05-29-094500.json`.
- The 2026-05-29 transport fingerprint produced no direct local transport
  signal: TCP `65527`, `20190`, `32108`, `15203`, and `3478` timed out; UDP
  `65531`, `32108`, and `20190` returned no target response for the bounded
  legacy LanSearch, SHIX seed, or JSON discover payloads.
- The Android oracle proves that the live stream exists after PPCS login and
  `writeCgi` on channel `1`.
- The managed C# application-level CGI frame builder now produces:
  `D1 00 <seq> 01 0A <len> 00 00 00 00 GET /livestream.cgi?... HTTP/1.1`.
