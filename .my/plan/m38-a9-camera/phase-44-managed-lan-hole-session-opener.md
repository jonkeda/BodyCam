# Phase 44 - Managed LAN-Hole Session Opener

**Status:** Superseded by Phase 47 compact direct opener

## Goal

Implement the first C#-only session-open attempt that follows the native
HLP2P LAN-hole path seen in Phase 43, running on Android phone Wi-Fi only.

Windows must not use laptop Wi-Fi for this phase. Windows may build, install,
and collect reports over USB/ADB. The Android phone remains the network device
connected to `@MC-0025644`.

## Starting Evidence

Phase 43 native logs show the working session does not primarily use decoded
TCP relays while the phone is on the camera AP. It succeeds through a local UDP
LAN-hole path:

```text
_se_lan_hole() ... app send lan hole broadcast
_se_recv_work_lan_app() ... recv dev lan hole
_se_recv_work() ... recv dev lan hole ack
_se_conn_succ() ... is_tcp=0 ... conn succ
_se_snd_alive() ... ip=192.168.168.1:53674
```

After that, native writes encrypted command-channel data and receives:

- command channel `0` status response;
- media channel `1` JPEG payloads;
- live-open response on command channel `0`.

The live CGI bytes and media extractor are already C# owned. The missing part
is the carrier that gets from LAN-hole open to encrypted channel read/write.

## Scope

In scope:

- Map the native LAN-hole packet format and session state fields.
- Use the Phase 43 `A9Vue990DasConnectDescriptor` instead of lossy DAS token
  strings.
- Build deterministic C# packets for the native session-open path.
- Run proof attempts only from Android Wi-Fi through the phone probe.
- Treat the first non-self response from `192.168.168.1` as the first gate.

Out of scope:

- Laptop Wi-Fi.
- Broad HTTP/RTSP/media scans.
- Broad UDP port matrices without newly mapped packet bytes.
- More decoded TCP relay retries as the primary path.
- H.264/FFmpeg/LibVLC work before raw media bytes prove a new codec need.

## Native Packet Creator Oracle - 2026-05-29 21:36

Run:

- Directory:
  `.my/plan/m38-a9-camera/captures/phase-44-native-hlp2p-packet-oracle-2026-05-29-213653/`
- Windows role: USB/ADB install and report collection only.
- Phone network state in report: `wlan0: 192.168.168.100/24`.
- The run did not use laptop Wi-Fi.
- Android phone probe build passed after adding this oracle.

Added Android oracle calls for native HLP2P helper exports:

- `create_LstReq`
- `create_PunchPkt`
- `create_P2pRdy`
- `create_P2pReq`

Key returned vectors:

```text
create_LstReq[vuid]:
F167001400000000424B0000000000000000642C57425044

create_PunchPkt[vuid]:
F141001400000000424B0000000000000000642C57425044

create_P2pRdy[vuid]:
F142001400000000424B0000000000000000642C57425044

create_P2pReq[vuid,phone-192.168.168.101:65529]:
F120002400000000424B0000000000000000642C57425044000000000002F9FF65A8A8C000000000
```

Important interpretation:

- The native helper scratch buffer contains four zero bytes between the
  4-byte HLP2P header and the 20-byte P2P id.
- `create_LstReq` / `create_PunchPkt` / `create_P2pRdy` return `24`, even
  though the scratch preview contains additional trailing P2P-id bytes.

Static follow-up resolved the scratch-buffer ambiguity:

- `Send_Pkt_ListReq` calls `create_LstReq`, then `pack_ClntPkt`, then
  `XQ_UdpPktSend`.
- `Send_Pkt_P2PReq` calls `create_P2pReq`, then `pack_ClntPkt`, then
  `XQ_UdpPktSend`.
- `pack_ClntPkt` first packs the 4-byte HLP2P header, then reads packet bodies
  from the helper scratch buffer at `scratch + 8`.
- For `F141`, `F142`, and `F167`, `pack_ClntPkt` writes exactly
  `header + 20-byte P2P id`.
- For `F120`, `pack_ClntPkt` writes exactly
  `header + 20-byte P2P id + 16-byte reverse address`.

Conclusion: the existing managed `A9Vue990Hlp2pPacketBuilder` no-padding packet
shape matches the final send shape for these basic packets. The remaining
missing piece is the higher-level LAN-hole/session-engine state, not the
basic HLP2P packet helpers.

## Managed Focused Opener Attempt - 2026-05-29 22:12

Run:

- Directory:
  `.my/plan/m38-a9-camera/captures/phase-44-managed-lan-hole-local-2026-05-29-221252/`
- Windows role: USB/ADB install and report collection only.
- Phone network state: `wlan0: 192.168.168.100/24`.
- The run did not use laptop Wi-Fi.
- Android phone probe build passed before the run.
- Focused Vue990 tests passed `46/46`.

Managed C# added:

- Added `A9Vue990ConnectByServerState`.
- Preserves decoded DAS tokens, including binary opaque token bytes and selector
  `9047F8F88`.
- Builds native structured P2P IDs for both `BK0025644WBPD` and
  `BKGD00000100FMQLN`.
- Produces the confirmed basic HLP2P opener packet bytes for list, punch,
  ready, and P2P request.
- Added Android `managed_lan_hole` autorun mode and Windows/ADB orchestration
  support.

Live outcome:

- The probe fetched status over camera Wi-Fi and built state from the live
  `DAS-...` server value.
- Fixed UDP `65529` sent the focused packet burst to camera unicast and
  broadcast targets.
- Fixed UDP `65529` received only self-echo packets from `192.168.168.100`.
- Ephemeral UDP `58034` sent the same focused packet burst and received no
  responses.
- No non-self response from `192.168.168.1` was captured.

Interpretation:

- The basic HLP2P helper packet bytes are now a tested C# baseline.
- The successful native `_se_lan_hole` path is not just the basic helper packet
  burst. It likely uses a separate session-engine packet built from derived DAS
  state and/or native session fields.
- Do not repeat this exact focused basic-packet burst unless new packet bytes
  or endpoint evidence are added.

## Phase 45 Handoff - 2026-05-29

This phase should not keep retrying the basic helper burst. The next active work
has moved to
[Phase 45 - Native LAN-Hole Session Engine Map](./phase-45-native-lan-hole-session-engine-map.md).

Static follow-up added one useful refinement:

- `_clientSessionToSetup` sends a narrower native setup subset:
  client-id `ListReq`, client-id `P2PReq4`, and `LanSearch`.
- `create_P2pAlive` / `create_P2pAliveAck` are header-only packets:
  `F1E00000` and `F1E10000`.
- The Android managed LAN-hole mode now sends that native setup subset first and
  includes decoded DAS relay hosts as candidate targets.

The remaining Phase 44 blocker is unchanged: until Phase 45 maps the exact
`_se_lan_hole` request/ack bytes, this phase cannot honestly claim a C# session
opener.

## Phase 47 Update - 2026-05-30

The first C# session opener did not come from the basic `F1xx` helper-packet
burst in this phase. It came from the compact direct HLP2P path documented in
Phase 47:

- compact LAN-hole request `0x02`,
- LAN-hole response `0x11`,
- ready `0x15`,
- direct transport `0x0D`,
- C# ACK generation,
- native-paced post-hole control ordering.

Phase 47 succeeded on Android phone Wi-Fi and saved both
`managed-direct-still.jpg` and `managed-direct-video-mjpeg.avi`. The remaining
work is no longer "find any C# response"; it is "derive the encrypted post-hole
controls and port the proven sequence to Windows."

## Work Plan

1. Map native packet sources.
   - Inspect `_se_lan_hole`, `_se_recv_work_lan_app`, `_se_recv_work`,
     `_se_snd_alive`, and `_cgt_snd_se_web`.
   - Record field offsets for `did`, session id, app id, user id, version, and
     remote endpoint.
   - Identify whether LAN-hole packets reuse `create_p2pHdr` or a separate
     session-engine packet header.
   - Resolve the helper-buffer versus send-slice ambiguity for
     `create_LstReq` and `create_P2pReq`.

2. Promote native session state to C#.
   - Build `A9Vue990ConnectByServerState` from
     `A9Vue990DasConnectDescriptor`, client id, VUID, local endpoint, and
     credentials.
   - Preserve binary tokens and selector material.
   - Add unit tests against current camera values.

3. Add a focused Android managed opener.
   - Add a `managed_lan_hole` mode to the phone probe or managed-direct probe.
   - Bind to Android Wi-Fi and use only `wlan0` addresses.
   - Send the mapped LAN-hole packet sequence.
   - Save every non-self response packet as `.bin`.

4. Add the managed carrier after the opener responds.
   - Send alive/ack packets as native does.
   - Implement encrypted channel `0` write and channel `1` read.
   - Send the already-confirmed live CGI header/body.

5. Save C#-only media artifacts.
   - Save raw managed channel dumps.
   - Extract at least one JPEG.
   - Assemble MJPEG AVI from multiple frames.

## Acceptance Criteria

- Android C# receives a non-self LAN-hole/session response from the camera.
- Android C# reaches an encrypted channel read/write state without native
  `connect`, `write`, or `client_read`.
- Android C# sends the confirmed live CGI command.
- Android C# saves a still image and MJPEG AVI.
- Report records endpoint, packet prefixes, frame count, hashes, and artifact
  paths.

## Checklist

- [x] Map working compact direct LAN-hole packet format in Phase 47.
- [x] Capture native `create_LstReq` / `create_P2pReq` helper vectors.
- [x] Map `Send_Pkt_ListReq` / `Send_Pkt_P2PReq` send pointer and length.
- [x] Map working compact LAN-hole response and ready formats in Phase 47.
- [x] Map alive packet headers `F1E00000` / `F1E10000`.
- [x] Add `A9Vue990ConnectByServerState`.
- [x] Add focused Android `managed_lan_hole` probe mode.
- [x] Receive first non-self camera response in C#.
- [x] Implement managed direct `0D` carrier and ACKs.
- [x] Save C#-only still image.
- [x] Save C#-only MJPEG AVI.
- [ ] Derive encrypted post-hole controls instead of replaying observed vectors.
