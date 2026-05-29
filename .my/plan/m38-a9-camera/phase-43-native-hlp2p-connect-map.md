# Phase 43 - Native HLP2P Connect-By-Server Map

**Status:** In progress - native LAN-hole path identified, descriptor parser added

## Goal

Recover the exact native HLP2P session-open sequence needed for the pure C#
Vue990/A9 transport.

This phase keeps the current network constraint: laptop Wi-Fi is not used.
Windows may build, install, and drive the Android probe over USB/ADB, but the
Android phone is the only device connected to the camera Wi-Fi.

## Why This Phase Exists

Phase 41 proved the live-open command and media extraction. Phase 42 proved
that Android Wi-Fi permissions and routing are not the current blocker. The
remaining problem is the native HLP2P session carrier that creates the session
handle used by native `client_write`, `client_read`, and `client_check_buffer`.

Repeating broad HTTP, RTSP, UDP, or fixed relay packet probes would mostly
repeat known negative evidence. The next useful work is a byte-accurate map of
native `ConnectByServer_V4` / `hl_p2p_connect_by_server` /
`_p2p_connect_check_svr`.

## Current Native Map

Library:

- `tools/BodyCam.A9PhoneProbe/NativeLibs/arm64-v8a/libOKSMARTPPCS.so`

Selected symbols:

- `ConnectByServer_V4` at `0x4597c`
- `HLP2P_ConnectByServer` at `0x9aa98`
- `hl_p2p_connect_by_server` at `0x98b58`
- `_p2p_connect_check_svr` at `0x9525c`
- `_p2p_connect_check` at `0x9447c`
- `_p2p_connect_set_session` at `0x94248`
- `_p2p_connect_timeout` at `0x958d4`
- `hl_p2p_read_udp` at `0x9921c`
- `hl_p2p_read_tcp` at `0x99530`
- `hl_p2p_write` at `0x99b40`
- `hl_p2p_check_buffer` at `0x9a1c0`

Call path for our known connect shape:

```text
JNIApi.connect(client, 0x3F, serverParam, 1)
client_connect
ConnectByServer_V4
HLP2P_ConnectByServer
hl_p2p_connect_by_server
_p2p_connect_check_svr
_p2p_connect_timeout
```

`ConnectByServer_V4` maps `connectType=0x3F` to HLP2P subtype `1`.

`hl_p2p_connect_by_server`:

- checks HLP2P global initialization flags;
- checks the global session count limit;
- requires a non-empty DID and a server parameter length of at least `0x3c`;
- rejects subtype values above `7`;
- allocates a `0x2f0`-byte session structure;
- writes subtype/connect flags into `session + 0x2de`;
- copies optional caller data into `session + 0x30` with length at
  `session + 0x98`;
- calls `_p2p_connect_check_svr(session, did, serverParam)`;
- on success passes the session into `_p2p_connect_timeout(session, 10)`.

`_p2p_connect_check_svr`:

- requires the server parameter to start with `DAS-`;
- strips the `DAS-` prefix and decrypts the hex payload using the already
  recovered native AES-CBC/no-padding key and IV;
- tokenizes the decrypted payload by comma;
- validates several tokens rather than accepting a simple relay host rewrite;
- builds an internal string with native format:
  `das,%d,%s,%s,%s`;
- hashes/derives fields using `m_init`, `m_u`, `m_f`, and `_b_t_h`;
- validates the trailing/select token unless it is the special token `all`;
- initializes the session identity and network state;
- records session id material at offsets including `session + 0x88`,
  `session + 0xed`, `session + 0xf2`, `session + 0x10c`,
  `session + 0x1b4`, and `session + 0x26d`;
- calls `_p2p_connect_set_session`, then `_p2p_connect_timeout`.

Important implication: replacing relay hosts in the decoded DAS plaintext failed
in Phase 29 because native code validates more than the host list. The trailing
token `9047F8F88`, the `a+a+a` mode token, and the first opaque token are part
of the session-open state.

## Confirmed DAS Plaintext

Current camera DAS decrypts to:

```text
53BAH050-\x13\x11r/=K=00011,a+a+a,47.98.128.117-120.78.3.33-47.109.80.221,BKGD,9047F8F88
```

Known tokens:

- token 0: opaque server/auth material,
  `53BAH050-\x13\x11r/=K=00011`
- token 1: mode/routing material, `a+a+a`
- token 2: relay hosts,
  `47.98.128.117-120.78.3.33-47.109.80.221`
- token 3: relay/name token, `BKGD`
- token 4: selector/check token, `9047F8F88`

## Working Hypothesis

The managed C# relay and UDP packet builders are not enough by themselves
because native HLP2P first derives per-session values from the whole DAS
payload. Those derived values then seed the session state, endpoint list, and
the first session-open packets.

The next C# implementation target is therefore not another fixed packet. It is
a managed `ConnectByServer` state builder that mirrors the native DAS token
validation and session-field derivation.

## Android-Only Native Log Outcome - 2026-05-29 21:20

Run:

- Directory:
  `.my/plan/m38-a9-camera/captures/phase-43-native-hlp2p-log-2026-05-29-211949/`
- Windows role: USB/ADB build and orchestration only.
- Camera-network role: Android phone Wi-Fi only.
- Native HLP2P logging enabled:
  `HLP2P_SetLogLevel flags=0x1f level=0xff enabled=1 result=0`

The successful native-backed run did not use the decoded TCP relay candidates.
It opened a local UDP LAN-hole session:

```text
_se_lan_hole() ... app send lan hole broadcast
_se_recv_work_lan_app() ... recv dev lan hole
_se_recv_work() ... recv dev lan hole ack
_se_conn_succ() ... is_tcp=0 ... conn succ
_p2p_connect_timeout() ... succ ... time=260ms
```

After connection, native keepalive logged the camera endpoint:

```text
_se_snd_alive() ... tcp_fd=-1 send alive to rmt cnt=1 ip=192.168.168.1:53674
```

This changes the next managed target. The priority is now the native LAN-hole
opener and session carrier, not more relay retries.

Fresh native-backed media artifacts from the same run:

- Still image:
  `native-channel-oracle-frames/channel-frame-000.jpg`
- Still size: `14043` bytes
- Still SHA-256:
  `BD89669D1244913B888E5AF2EF5CC376CEF9EC30C10A8D9D4D9814D2950E4369`
- Extracted frames: `49`
- Video:
  `native-channel-oracle-mjpeg.avi`
- Video size: `685544` bytes
- Video SHA-256:
  `5B0D8D1550D332D8126EC21144BF84D554EA851F4DFB6CEE808EBB8379A84FBF`

Native channel evidence after the LAN-hole connect:

- `hl_p2p_write(... cnl=0 data_len=8 enc=1)`
- `hl_p2p_write(... cnl=0 data_len=84 enc=1)` for login status.
- `hl_p2p_write(... cnl=0 data_len=8 enc=1)`
- `hl_p2p_write(... cnl=0 data_len=97 enc=1)` for the live CGI command.
- `_cgt_rcv_data(... cnl=0 len=796)` for status.
- `_cgt_rcv_data(... cnl=1 len=14048)` for first media.
- `_cgt_rcv_data(... cnl=0 len=41)` for live-open response.

## Managed C# Added - 2026-05-29 21:26

- Added `A9Vue990DasConnectDescriptor`.
- Preserved each decoded DAS token as raw bytes plus escaped ASCII and hex.
- Verified the current camera descriptor keeps token `0` binary bytes
  `0x13 0x11`, mode parts `a+a+a`, relay hosts, relay name `BKGD`, and
  selector `9047F8F88`.
- Focused Vue990 tests passed: `42/42`.
- Android phone probe build passed for `net10.0-android`.

## Work Plan

1. Freeze the Android-only native log outcome.
   - Keep Windows on USB/ADB only.
   - Keep the phone on `@MC-0025644`.
   - Treat the LAN-hole path as the next active managed target.

2. Promote the disassembly map into managed code.
   - Add a C# value object for decoded DAS connect-by-server tokens.
   - Add tests using the current camera DAS payload.
   - Preserve binary tokens as bytes, not lossy strings.

3. Map native LAN-hole derivations and packet builders.
   - Port or emulate the `das,%d,%s,%s,%s` derived string step.
   - Identify the global integer used in the format string.
   - Map which token goes into each `%s`.
   - Map the five-byte selector check generated from `m_init` / `_b_t_h`.
   - Map `_se_lan_hole`, `_se_recv_work_lan_app`, `_se_snd_alive`, and
     `_cgt_snd_se_web` packet formats.

4. Add an Android-only managed opener attempt only after the derived fields are
   known.
   - Do not rerun broad port scans.
   - First success signal is a non-self remote HLP2P response.
   - Final success signal remains a C#-only JPEG and MJPEG AVI.

## Acceptance Criteria

- Native log/oracle run recorded without laptop Wi-Fi.
- C# parses and preserves all five DAS tokens with tests.
- The native `das,%d,%s,%s,%s` derivation is mapped or explicitly blocked.
- Managed C# receives a non-self session response before attempting live CGI.
- Managed C# saves image and video without native session read/write.

## Checklist

- [x] Locate native connect-by-server path.
- [x] Record key symbols and offsets.
- [x] Confirm native validates full DAS token set.
- [x] Enable and capture native HLP2P debug logs from Android-only run.
- [x] Add managed DAS connect descriptor parser.
- [ ] Map native derived `das,%d,%s,%s,%s` fields.
- [ ] Map native LAN-hole opener packets.
- [ ] Implement managed session-open packet/state builder.
- [ ] Receive non-self HLP2P response in C#.
- [ ] Save C#-only image and video.
