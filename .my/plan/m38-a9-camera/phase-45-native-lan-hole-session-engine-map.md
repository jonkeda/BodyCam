# Phase 45 - Native LAN-Hole Session Engine Map

**Status:** Superseded by Phase 47 runtime proof

## Goal

Map the native Vue990/HLP2P session-engine LAN-hole flow tightly enough to
replace the remaining native `connect` / raw `write` / raw `client_read` path
with C#.

Android remains the proof runtime because the phone can stay on the camera Wi-Fi
while Windows controls builds and logs over USB/ADB. This phase is local-only;
no commit or push is part of the work.

## Starting Point

The image/video goal is partially solved:

- Phase 40 proved the channel media bytes contain JPEG frames in the
  `55 AA 15 A8` Vue990 envelope.
- Phase 41 proved the C# live-open command bytes and saved real still/video
  artifacts through the native session carrier.
- Phase 43 proved native `ConnectByServer` succeeds through local UDP LAN-hole:
  `_se_lan_hole`, `dev lan hole`, `dev lan hole ack`, then alive packets to
  `192.168.168.1:53674`.
- Phase 44 proved the basic helper packets are byte-accurate in C#, but a
  helper-packet burst only produced self-echo/no camera response.

The active blocker is the native session-engine opener and derived session
state, not live-CGI framing, JPEG extraction, Android Wi-Fi permission, or broad
network scanning.

## Static Map - 2026-05-29

Native library:

```text
tools/BodyCam.A9PhoneProbe/NativeLibs/arm64-v8a/libOKSMARTPPCS.so
```

Important exported/native symbols:

- `ConnectByServer_V4 0x4597c`
- `HLP2P_ConnectByServer 0x9aa98`
- `hl_p2p_connect_by_server 0x98b58`
- `_p2p_connect_check_svr 0x9525c`
- `_p2p_connect_set_session 0x94248`
- `_clientSessionToSetup 0x839a0`
- `_sessionSetup 0x857fc`
- `_sessionAliveKeep 0x85984`
- `pSessionStart 0x85fe4`
- `Send_Pkt_ListReq 0x8a570`
- `Send_Pkt_P2PReq 0x8a1c0`
- `Send_Pkt_LanSearch 0x89f58`
- `Send_Pkt_Alive 0x8a3f0`
- `Send_MagicPkt 0x8be84`

Mapped native client-session setup subset:

- `pSessionStart` initializes two UDP sockets at session offsets `0xc04` and
  `0xc08`, copies remote server address lists into offsets `0x5f0` and
  `0x8f0`, and writes the compact native DID/P2P id at `0xbf0`.
- `_clientSessionToSetup` sends:
  - `Send_Pkt_ListReq` using the client DID/P2P id and primary server list.
  - `Send_Pkt_ListReq` using the same DID/P2P id and secondary server list
    when present.
  - `Send_Pkt_P2PReq` using the client DID/P2P id plus the local reverse IPv4
    endpoint.
  - `Send_Pkt_LanSearch` and `Send_MagicPkt` on a local-search branch.
- `Send_Pkt_PunchPkt` and `Send_Pkt_P2PRdy` are native helpers, but they are
  not the primary `_clientSessionToSetup` sends. Replaying them as an opener is
  negative evidence from Phase 44 unless new native bytes require them.
- `create_P2pAlive` writes `F1 E0 00 00`; `create_P2pAliveAck` writes
  `F1 E1 00 00`.

New C# baseline from this phase:

- Added native header-only alive packet builders.
- Added a narrower `BuildNativeClientSessionSetupPackets()` packet set:
  client-id `ListReq`, client-id `P2PReq4`, and `LanSearch`.
- Updated the Android managed LAN-hole mode to send this native-session setup
  subset first and to include decoded DAS relay hosts in the target list.

## What Is Still Unknown

- Exact `_se_lan_hole` application packet bytes and whether it is separate from
  HLP2P helper packets.
- Exact `dev lan hole` and `dev lan hole ack` response formats.
- How the native session random values map to C# state:
  - observed `uid=436848649` (`0x1A09C809`)
  - observed `aid=261839029` (`0x0F9B58B5`)
  - observed app version `20260310`
  - observed device version `20260203`
- Which native address list entry and port produce the final camera endpoint
  `192.168.168.1:53674`.
- The encrypted channel carrier state needed for DRW `F1 D0` read/write after
  the LAN-hole ack.

## Phase 47 Resolution - 2026-05-30

The broad helper-packet path remained negative evidence, but the native socket
hook exposed a different compact direct transport shape. Managed C# now succeeds
on Android by using:

- compact LAN-hole probe type `0x02`,
- compact LAN-hole response type `0x11`,
- compact ready type `0x15`,
- compact alive `0B0000` / `0C`,
- direct `0D` command/media packets and C# ACKs,
- native-paced post-hole control ordering.

The exact successful artifact run is documented in
`phase-47-managed-hlp2p-direct-csharp-capture.md`.

The remaining unknown from this phase has narrowed: C# no longer needs the old
`F1 D0`/`F1 D1` DRW carrier for this camera's local stream path, but it still
needs to derive the encrypted post-hole `0D` control payloads instead of
replaying native-observed vectors.

## Work Plan

1. Finish native LAN-hole function map.
   - Find the exact code that logs `_se_lan_hole`, `dev lan hole`, and
     `dev lan hole ack`.
   - Record the send buffer, length, destination, and field offsets.
   - Identify random/session fields and whether they are generated or parsed.

2. Build C# packet vectors only from mapped bytes.
   - Add golden tests for `_se_lan_hole` request bytes once known.
   - Add parsers for `dev lan hole` and `dev lan hole ack` once known.
   - Keep basic helper packets as supporting primitives, not the active opener.

3. Run one focused Android proof.
   - Phone on `@MC-0025644`.
   - Windows over USB/ADB only.
   - Save every non-self response packet from `192.168.168.1` or relay hosts.
   - Treat self-echo-only as a byte/state mismatch, not a firewall issue.

4. Add managed carrier after the ack.
   - Send native alive packets to the negotiated endpoint.
   - Send the already-proved C# live CGI command over command channel `0`.
   - Read media channel `1`.
   - Save one JPEG and one MJPEG AVI without native session calls.

## Acceptance Criteria

- Exact native LAN-hole request/ack packet fields are documented.
- C# tests cover the mapped packet bytes and response parsers.
- Android C# receives a non-self LAN-hole/session response.
- Android C# reaches managed DRW channel read/write.
- Android C# saves a still image and MJPEG AVI without native `connect`,
  native raw `write`, native `client_read`, or `AppPlayerApi`.

## Checklist

- [x] Map `pSessionStart` socket/list/DID setup enough to choose client-id
      session packets.
- [x] Map `_clientSessionToSetup` basic send order.
- [x] Add C# native client-session setup packet set.
- [x] Add C# `F1E0` / `F1E1` alive builders.
- [x] Find working compact LAN-hole send buffer.
- [x] Find working compact LAN-hole response parser.
- [x] Find working compact LAN-hole ready/ack parser.
- [x] Reuse observed session `uid` / token shape in C# for the current camera.
- [x] Receive first non-self response in managed Android probe.
- [x] Implement managed direct `0D` carrier and ACKs.
- [x] Save C#-only still image.
- [x] Save C#-only MJPEG AVI.
- [ ] Derive encrypted post-hole control payloads in C#.
