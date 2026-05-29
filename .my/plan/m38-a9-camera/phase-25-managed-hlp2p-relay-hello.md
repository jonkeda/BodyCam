# Phase 25 - Managed HLP2P Relay Hello

**Status:** Active - bounded C# relay probes implemented; no response bytes yet

## Goal

Implement the first Windows-native C# HLP2P/PPCS relay handshake step far
enough to receive a real server response from a decoded relay host.

The final capture goal is still direct Windows C# image/video retrieval. This
phase is narrower: replace the opaque vendor `ConnectByServer` opening packet
sequence with managed C# and prove the relay accepts it.

## Evidence From Phase 24

- Live camera status works from Windows on `192.168.168.1:81`.
- Direct HTTP media paths still return real `404` responses, so a stream URL is
  not hiding behind the HTTP status service.
- The live `DAS-...` value decrypts in C# with the recovered native AES-CBC
  key/IV derivation.
- Decoded relay hosts are:
  `47.98.128.117`, `120.78.3.33`, `47.109.80.221`.
- TCP `65527` opens on all three decoded relay hosts but returns no banner
  without the native hello.
- Android control capture still works and produced a fresh still plus six live
  frames on 2026-05-29.
- Phase 27 provides a repeatable Windows C# command that drives the Android C#
  probe and downloads a still image plus MJPEG AVI while the pure Windows
  session replacement is still pending.

## Work Plan

1. Reverse the native `create_Hello`, `cs2p2p_PPPP_Proto_TCPSend_Hello`,
   server-request, and relay-login packet helpers enough to identify packet
   headers, lengths, constants, device id fields, and checksums.
2. Add a C# relay hello builder with byte-level tests against recovered native
   constants.
3. Add a bounded `BodyCam.A9Probe` command that connects to TCP `65527`, sends
   only the hello/opening packet, and records the first response bytes.
4. If the relay answers, implement the next request packet that binds
   `BKGD00000100FMQLN` / `BK0025644WBPD` to the session.
5. Only after session open is proven, send the existing managed CGI frame for
   `livestream.cgi?streamid=10&substream=0&` on channel `1`.
6. Save any response bytes under captures and update the protocol notes before
   attempting video decoding.

## Checklist

- [x] Locate native hello/server-request packet construction evidence.
- [ ] Identify device-id, VUID, relay token, nonce, checksum, and crypto fields.
- [x] Implement first managed packet builders in C# for recovered empty headers.
- [x] Add unit tests for recovered empty-header byte layout.
- [x] Add a bounded relay-hello probe command.
- [x] Run the relay-hello probe against decoded TCP `65527` hosts.
- [ ] If a session opens, attempt channel `1` live CGI and save a bounded raw
      byte dump.
- [ ] If channel bytes arrive, identify codec/framing and create the image/video
      capture phase.

## 2026-05-29 Relay Probe Result

Implemented `BodyCam.A9Probe vue990-relay-hello` and
`A9Vue990P2pPacketBuilder`.

Native-derived empty-header candidates:

- `F1 00 00 00` - TCP hello
- `F1 70 00 00` - relay hello
- `F2 10 00 00` - server request
- `00 04 68 00 73 51 67 3D 7C 58 97 F9` - native `TCPSend_Hello`
  loopback output from Phase 28
- short same-socket sequences combining those headers

Live artifact:

- `.my/plan/m38-a9-camera/captures/phase-25-relay-hello-native-sequences-2026-05-29-123923.json`
- `.my/plan/m38-a9-camera/captures/phase-25-relay-native-tcpsend-hello-2026-05-29-132343.json`

Outcome:

- status fetch and DAS decode succeeded;
- decoded relays remained `47.98.128.117`, `120.78.3.33`, and
  `47.109.80.221`;
- TCP `65527` opened on all decoded relays;
- all 36 candidate attempts sent bytes successfully;
- no candidate produced response bytes within the read window;
- the later native `TCPSend_Hello` loopback payload also produced no relay
  response bytes.

Conclusion: the relay is reachable from Windows, but the empty hello/server
headers and native TCP-send hello are not enough. The next packet to recover is
likely the larger native `TCPRlyReq` / `TCPRSLgn` second-stage payload, not
another broad port probe.

## Stop Conditions

Stop and document if:

- the relay hello requires a per-device secret that is not present in status,
  DAS, APK constants, or native-derived values;
- the relay requires cloud account authentication outside the camera-local
  Vue990 path;
- the native packet sequence is too large to port safely without a more direct
  Android oracle byte capture.

## Firewall Note

The Windows firewall remains enabled and the camera AP is categorized as
`Public`, but current evidence does not point to firewall as the primary
blocker. Windows can reach camera status, receives real HTTP `404` media
responses, and opens decoded relay TCP sockets. The missing piece is the
managed relay/session hello.
