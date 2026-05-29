# Phase 24 - DAS And ConnectByServer Reverse Path

**Status:** Completed - DAS decode recovered; relay sockets identified; managed
HLP2P/PPCS hello remains the next blocker

## Goal

Recover enough of Vue990/VStarcam's `ConnectByServer` path to implement the
first managed C# connect/login step from Windows.

Phase 24 exists because Phase 23 proved that the camera status API and DAS
shape are reachable from Windows, but the likely local PPCS/HLP2P ports did not
answer bounded probes. The next useful work is parser/crypto evidence, not more
blind port scanning.

## Inputs

- Status/DAS artifact:
  `.my/plan/m38-a9-camera/captures/phase-23-das-analysis-2026-05-29.json`
- Transport artifact:
  `.my/plan/m38-a9-camera/captures/phase-23/windows-ppcs-transport-2026-05-29.json`
- Native libraries:
  `.my/plan/m38-a9-camera/captures/vue990-apk/`
- Proven Android call:
  `connect(clientPtr, 0x3F, serverParam, 1)`
- Public hypothesis source:
  https://palant.info/2025/11/05/an-overview-of-the-pppp-protocol-for-iot-cameras/

## Work Plan

1. Locate `ConnectByServer`, `hl_p2p_connect_by_server`, and `DAS` parsing
   paths in `libOKSMARTPPCS.so` strings and disassembly.
2. Determine whether `connectType=0x3F` and `p2pType=1` select PPCS, XQP2P,
   HLP2P, VEEPAI, or a fallback chain.
3. Reconstruct the DAS decode/key derivation path in managed C#.
4. Add unit tests for DAS decode attempts, including failure cases that keep
   opaque values explicit.
5. If DAS decodes to relay/server parameters, add a bounded server hello probe
   that records bytes but does not login or open video.
6. Only after connect/session bytes are understood, return to Phase 23 login
   and channel `1` raw byte capture.

## Checklist

- [x] Extract focused symbol/string windows around `ConnectByServer` and DAS
      parser code.
- [x] Identify DAS key/IV derivation or prove it is not enough without native
      code execution.
- [x] Implement a managed DAS decode candidate with explicit success/failure
      reporting.
- [x] Map selected transport family for `connectType=0x3F`, `p2pType=1`.
- [x] Add a bounded C# relay/server probe if decoded endpoints exist.
- [x] Update the next-phase stop condition: relay sockets open, but no stream
      bytes appear without the native hello.

## Stop Conditions

Stop and document if:

- DAS decode requires secrets not present in status, APK, or known constants;
- the native code delegates server selection entirely to cloud APIs that are
  unavailable from the camera AP path;
- the parser is identifiable but too costly to port safely without using the
  vendor library.

## Current Evidence

- DAS is a 96-byte opaque payload with known magic and no direct IP/port
  candidates.
- Naive local transport probing found no direct TCP/UDP signal on likely
  PPCS/HLP2P ports.
- Android vendor code can connect using the same DAS string, so the missing
  information is in the native connect/parser/crypto path rather than camera
  availability.

## Results

- `connectType=0x3F` maps into the HLP2P `ConnectByServer` path in the native
  library.
- The `DAS-...` payload decrypts with AES-CBC/no-padding. The native key is
  the first 16 ASCII chars of uppercase `MD5(61 zero bytes)`, and the native IV
  is the first 16 ASCII chars of uppercase `MD5(78 zero bytes)`.
- Managed C# DAS decode now extracts this plaintext:
  `53BAH050-\x13\x11r/=K=00011,a+a+a,47.98.128.117-120.78.3.33-47.109.80.221,BKGD,9047F8F88`
- Decoded relay hosts:
  `47.98.128.117`, `120.78.3.33`, `47.109.80.221`.
- Live Windows status remained stable on the camera AP:
  `192.168.168.100/24`, camera `192.168.168.1:81`, battery `100`.
- Direct HTTP media probing from Windows still returned `404` for all
  snapshot/video/livestream candidates; only `get_status.cgi` returned `200`.
- Local camera transport probing still found no direct socket response on the
  tested PPCS/HLP2P TCP/UDP ports.
- Relay probing found TCP `65527` open on all three decoded relay hosts. One
  relay also accepted TCP `80` and `443`. No banner or media bytes were
  returned because the managed HLP2P/PPCS hello is not implemented yet.
- Windows Firewall is not the main explanation for the current failure:
  Windows can reach camera HTTP status, gets real `404` media responses, and
  can open decoded relay TCP sockets. The camera Wi-Fi profile is still
  `Public`, so UDP/listener behavior remains worth noting, but the primary
  blocker is protocol handshake/framing.

## Artifacts

- Offline DAS decode:
  `.my/plan/m38-a9-camera/captures/phase-24-das-decode-offline-2026-05-29.json`
- Live DAS decode:
  `.my/plan/m38-a9-camera/captures/phase-24-das-decode-live-2026-05-29.json`
- HTTP media retry:
  `.my/plan/m38-a9-camera/captures/phase-24-http-media-retry2-2026-05-29.json`
- Relay probe:
  `.my/plan/m38-a9-camera/captures/phase-24-ppcs-relay-probe-2026-05-29.json`
- Android control still/video frame pull:
  `.my/plan/m38-a9-camera/captures/phase-24-android-control-2026-05-29/`

## Next Phase

Phase 25 should implement the managed HLP2P/PPCS relay hello and session-open
packet sequence against TCP `65527`, then return to the known
`livestream.cgi?streamid=10&substream=0&` channel `1` request once a session is
established.
