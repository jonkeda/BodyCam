# Phase 42 - Managed Session Transport Replacement

**Status:** Superseded by Phase 47 Android success and Phase 48 Windows success

## Roadmap Gate

This phase continues gate 3 from
[C#-Only Vue990 Stream Roadmap](./csharp-only-vue990-roadmap.md): remove the
remaining native session carrier after Phase 41 proved the live-open protocol
bytes.

## Starting Point

Phase 41 proved the live stream can be opened with C#-generated protocol data:

- Command channel: `0`
- Header: `01 0A 00 00 61 00 00 00`
- Body:
  `GET /livestream.cgi?streamid=10&substream=0&loginuse=admin&loginpas=888888&user=admin&pwd=888888&`
- Result: stream-start callback, channel `1` bytes, `73` extracted JPEG frames,
  and a valid MJPEG AVI.

This means the live CGI command is no longer a hypothesis. Do not revisit CGI
payload variants unless a new camera firmware changes the command response.

## Remaining Native Pieces

- `JNIApi.create`
- `JNIApi.clientSetVuid`
- `JNIApi.connect`
- `JNIApi.login`
- `JNIApi.write` as the raw native session writer
- Native `client_read` as the channel reader

## Native Evidence - 2026-05-29

The Phase 42 disassembly pass moved the blocker down one layer:

- `client_login` at `0x449dc` is not a separate password handshake. It checks
  the connected flag at `client + 0x204`, stores the login user at
  `client + 0x98`, stores the password at `client + 0x199`, then sends a CGI
  command through the same command-channel path:
  `get_status.cgi?name=admin&`.
- That login status CGI is wrapped by `client_write_cgi`, so the actual command
  body is:
  `GET /get_status.cgi?name=admin&loginuse=admin&loginpas=888888&user=admin&pwd=888888&`.
- `client_write` at `0x43ab4` does not own packet framing itself. It calls the
  active session interface stored at `client + 0x80`, using session handle
  `client + 0x208` and interface offset `+0x88`.
- `client_read` at `0x44ba4` also delegates to the active session interface,
  using offset `+0x90`. It reads in bounded chunks, writes the number of bytes
  read to the caller's out pointer, and returns native session status.
- `client_check_buffer` at `0x439a8` delegates through interface offset
  `+0x98`.
- `client_connect` at `0x42974` is the point that installs the active session:
  on success it sets `client + 0x204 = 1`, `client + 0x80 = iface`, and
  `client + 0x208 = session`.
- For our successful call shape, `connectType = 0x3F` maps to the V4/HLP2P
  interface with subtype `1`:
  - `Connect_V4` at `0x45650` maps `0x3F` to subtype `1` and calls
    `HLP2P_Connect`.
- `ConnectByServer_V4` at `0x4597c` maps `0x3F` to subtype `1` and calls
    `HLP2P_ConnectByServer`.
  - `Read_V4` at `0x45cac` jumps to `HLP2P_Read`.
  - `Write_V4` at `0x45c38` jumps to `HLP2P_Write`.
- Native HLP2P packet helpers confirm a mixed byte-order packet shape:
  - `create_p2pHdr` writes the outer packet type and payload length in
    big-endian order.
  - `create_Drw` writes outer packet type `F1 D0`, payload marker `D1`, the
    channel byte, then the command index with a raw native `strh`. On ARM64
    that command index is little-endian.
  - `CSession_Drw_Deal` reads the DRW command index with `ldrh`, confirming the
    little-endian interpretation.
  - `create_DrwAck` writes outer packet type `F1 D1`; its ACK count is
    big-endian, but the individual ACKed command-index values are copied in
    native little-endian order.

Implication: the remaining rewrite is no longer the Java wrapper API. It is the
HLP2P session carrier that creates the session handle, carries channel `0` and
channel `1`, and exposes read/write/check through the interface table.

## Managed Code Added

- Added a C# builder for the native login-status CGI command body.
- Added a C# parser for the native 8-byte CGI command header.
- Corrected C# DRW and DRW ACK packet helpers to use the native mixed byte
  order: big-endian outer packet headers, little-endian DRW command indexes.
- Added incremental Android managed-direct reporting so partial probe progress
  is saved before completion.
- Added Android `NEARBY_WIFI_DEVICES` permission and ADB `install -g` / `pm
  grant` handling for Wi-Fi-local UDP tests.
- Fixed Android managed UDP sockets to bind ephemeral sockets before receiving.
- Narrowed managed-direct HTTP probing to fast `get_status.cgi` checks only;
  broad HTTP media scans are now skipped in this path because Phase 41 already
  proved live media is not exposed as plain HTTP.
- Added focused unit tests for these items.

## Android-Only Live Outcome - 2026-05-29 21:07

Laptop Wi-Fi was not used. Windows only controlled the phone over USB/ADB while
the Android phone used the camera Wi-Fi.

Run:

- Directory:
  `.my/plan/m38-a9-camera/captures/phase-42-android-wifi-permission-2026-05-29-210712/`
- Phone Wi-Fi: `@MC-0025644`, `192.168.168.100/24`
- Camera host: `192.168.168.1`
- Result: probe completed, no C#-only image/video artifacts.

What changed in this run:

- Before adding `NEARBY_WIFI_DEVICES`, Android UDP sends failed with
  `Permission denied`.
- After adding/granting the permission, UDP sends worked. The probe saw
  self-echoes from `192.168.168.100` on UDP `32108` and `65529`.
- No remote `PunchPkt` / `P2pReady` response arrived from the camera on the
  tested classic PPPP/HLP2P UDP variants.
- Relay fallback decoded DAS relays and attempted `24` TCP `65527` candidates,
  but all timed out with `0` response bytes.

Conclusion: Android Wi-Fi permission/routing is no longer the blocker for this
managed-direct path. The blocker remains the exact HLP2P/session-engine
LAN-hole handshake used by native `ConnectByServer_V4` /
`_p2p_connect_check_svr`.

## Phase 45 Refinement - 2026-05-29

Phase 44 proved that the basic helper packets are byte-accurate in C# but not
sufficient as an opener. Phase 45 now owns the sharper blocker:

- Map the native `_se_lan_hole` session-engine packet and response parsers.
- Keep `_clientSessionToSetup` evidence as a narrow setup subset:
  client-id `ListReq`, client-id `P2PReq4`, and `LanSearch`.
- Do not repeat the broad helper-packet burst unless new native packet bytes
  require it.

## Phase 47 Outcome - 2026-05-30

Phase 47 reached this phase's Android acceptance target. The Android probe used
managed C# runtime code to complete the compact HLP2P LAN-hole path, receive the
direct `0D` media transport, extract JPEGs, and save a still plus MJPEG AVI.

Successful run:

- Directory:
  `.my/plan/m38-a9-camera/captures/phase-47-managed-hlp2p-direct-paced-2026-05-30-001842/`
- Camera endpoint: `192.168.168.1:10654`
- Phone Wi-Fi: `@MC-0025644`, `192.168.168.100/24`
- Still: `managed-direct-still.jpg`, `640x480`, `9487` bytes,
  SHA-256 `9C124F13027538D726D2E72A83F06D5B03B08573FDD5A53B79DFD685B6A0A951`
- Video: `managed-direct-video-mjpeg.avi`, `12` frames, `640x480`,
  `113896` bytes, SHA-256
  `F08D052541F4A902E1F278509A9D09E4D73F1E58DC01E902C575504EABD512FB`

Important caveat: the runtime path is C# only, but the four post-hole control
payloads are still native-observed encrypted vectors. The next phase should
derive those controls in C#. Phase 48 has now ported the proven sequence to
Windows and saved image/video artifacts.

## Phase 48 Outcome - 2026-05-30

Phase 48 reached the Windows acceptance target. The laptop connected directly
to `@MC-0025644`, ran the shared managed direct capture sequence, received
`55 AA 15 A8` media bytes, extracted JPEGs, and saved a still plus MJPEG AVI.

Successful run:

- Directory:
  `.my/plan/m38-a9-camera/captures/phase-48-windows-direct-2026-05-30-004023/`
- Laptop Wi-Fi: `@MC-0025644`, `192.168.168.101/24`
- Camera endpoint: `192.168.168.1:2951`
- Still: `managed-direct-still.jpg`, `640x480`, `9123` bytes,
  SHA-256 `52444D62CF8E3F2520F1436F57E02E26FCF3D26323C6FFD8739E5C6AE0E6CE30`
- Video: `managed-direct-video-mjpeg.avi`, `12` frames, `640x480`,
  `110204` bytes, SHA-256
  `8C07FC2095F84209C52B06A16BE80A972E8C67CE8C00D566BDB63A684D74FC87`

## Goal

Receive the same `55 AA 15 A8` channel media bytes from managed C# transport on
Android, then save the JPEG and MJPEG AVI without `JNIApi` or direct native
P/Invoke session calls.

## Plan

1. Freeze Phase 41 command bytes as the live-open payload.
   - Promote only the confirmed command header/body into managed transport
     attempts.
   - Remove old `D1` live-CGI assumptions from active opener paths.

2. Map native login/session messages.
   - Use native disassembly and focused oracle logging around `client_login`,
     `client_connect`, `client_write`, and `client_command_read`.
   - Record command headers, channels, response types, and timing.

3. Implement the managed Android session carrier.
   - Open the same local or relay transport the native session uses.
   - Authenticate with the mapped login sequence.
   - Send the Phase 41 command-frame live CGI on command channel `0`.
   - Read media channel `1` and pass bytes to `A9Vue990ChannelMediaExtractor`.

4. Save artifacts from managed bytes.
   - Save raw channel dumps first.
   - Extract at least one JPEG.
   - Assemble MJPEG AVI when multiple frames arrive.

## Acceptance Criteria

- Android C# receives non-native channel bytes containing `55 AA 15 A8` or raw
  JPEG frames.
- Android C# saves a still image and video without `JNIApi.writeCgi`,
  `JNIApi.write`, native `client_read`, or `AppPlayerApi`.
- The report records transport endpoint, command bytes, frame count, still path,
  video path, dimensions, byte counts, and SHA-256 hashes.

## Checklist

- [x] Phase 41 command-frame live CGI proved.
- [x] C# media extractor and MJPEG writer proved on live camera bytes.
- [x] Native login/read/write wrapper map recorded.
- [x] Native DRW packet byte order recorded and corrected in C#.
- [x] Android Wi-Fi-local UDP permission issue fixed.
- [x] Managed Android probe reaches UDP and relay session attempts without
  laptop Wi-Fi.
- [x] Basic HLP2P helper packet bytes mapped and tested.
- [x] Native `_clientSessionToSetup` setup subset recorded.
- [x] HLP2P connect/session handshake map recorded enough for Android direct
      transport.
- [x] Managed transport receives first non-self session response.
- [x] Managed transport reaches the media channel without native session calls.
- [x] Managed transport receives media bytes.
- [x] C# saves image and video without native session calls.
- [ ] C# derives encrypted post-hole controls instead of replaying observed
      vectors.
