# VStarcam/VeePai PPCS Protocol Notes

**Status:** Evidence pack active - Android C# capture works, Windows status,
direct HTTP, DAS decode, and relay probes added

## Purpose

Track only evidence-backed facts needed to replace the Android Vue990/VStarcam
runtime with a Windows-native C# implementation.

This is not a general PPPP document. It is scoped to `@MC-0025644` and the
Vue990/VeePai path that has already produced image and video artifacts.

## Proven Device Facts

- Camera SSID/tag: `@MC-0025644`
- Camera/AP IP: `192.168.168.1`
- Direct TCP surface: port `81`
- Status URL:
  `http://192.168.168.1:81/get_status.cgi?loginuse=admin&loginpas=888888`
- VUID / real device id: `BK0025644WBPD`
- PPCS client id: `BKGD00000100FMQLN`
- Alias/chip hint: `BK7252N`
- Login: `admin` / `888888`
- Live stream resolution after vendor decode: `640x480`

## Proven Native Call Order

The Android oracle path works in this order:

1. Fetch status and read `deviceid`, `realdeviceid`, and `server`.
2. `JNIApi.init(...)`
3. `JNIApi.create(clientId, null)`
4. `JNIApi.clientSetVuid(clientPtr, vuid)`
5. `JNIApi.connect(clientPtr, 0x3F, serverParam, 1)`
6. `JNIApi.login(clientPtr, "admin", "888888")`
7. `JNIApi.checkMode(clientPtr)` returns `[1,1]`
8. `JNIApi.writeCgi(clientPtr, "livestream.cgi?streamid=10&substream=0&", 1)`
9. `JNIApi.checkBuffer(clientPtr, 1)` becomes non-zero before player
   consumption.
10. `AppPlayerApi.setPlayerSource(playerPtr, 1, null, null, clientPtr, null)`
11. `AppPlayerApi.start(playerPtr)`
12. Player callbacks report `app_player_draw_info width=640 height=480`.

The native lifecycle requires retain/release behavior around operations. The
first harness attempt crashed before this was mimicked.

## Callback Evidence

Command/control callbacks seen in the successful oracle path:

- `type=24577`, length around `788`
- `type=24631`, length around `33`

The `24631` callback appears after the live-open CGI and is likely the live-open
ack. Both callback payloads begin with JavaScript-like ASCII such as:

```text
var result=0;
```

## Capture Evidence

Still image:

- `AppPlayerApi.Screenshot(...)` produced real JPEG files.
- Verified output: `640x480`.

Video:

- `AppPlayerApi.StartDown(...)` returned `False` in the standalone harness.
- Phase 21 captured a bounded sequence of screenshot JPEGs and wrote a C#
  MJPEG AVI.
- Android control capture on 2026-05-29 still works while the phone is on
  `@MC-0025644`: a fresh `640x480` JPEG and six frame JPEGs were pulled to
  `.my/plan/m38-a9-camera/captures/phase-24-android-control-2026-05-29/`.
- The pulled frames were assembled on Windows with the C#
  `BodyCam.A9Probe mjpeg-avi` command into
  `a9-video-2026-05-29-120833-mjpeg.avi`.

## Unknowns Blocking Windows-Native Capture

- How to decode or connect using the `DAS-...` server parameter.
- Whether the managed client should connect directly to `192.168.168.1`, to a
  relay, or to both.
- Exact PPCS handshake bytes.
- Authentication/session-key derivation.
- Keepalive timing and disconnect behavior.
- Channel framing for command/control.
- Channel framing for live stream data.
- Stream codec before `libOKSMARTPLAY.so` decodes it.
- Whether channel `1` contains JPEG, H.264/H.265, or proprietary framed data.

## Managed C# Application Command Frame

The next C# implementation slice now has a managed builder for the known
application-level CGI request:

- class: `A9Vue990CgiCommandBuilder`
- live command: `livestream.cgi?streamid=10&substream=0&`
- generated payload text:
  `GET /livestream.cgi?streamid=10&substream=0& HTTP/1.1\r\n\r\n`
- frame prefix:
  `D1 00 <sequence:2> 01 0A <payloadLength:2> 00 00 00 00`

This matches public PPPP/VStarcam application-protocol examples and the
Android oracle's `JNIApi.writeCgi(..., channel=1)` behavior at the request
layer. It does not solve the lower PPCS transport handshake yet.

## Native String Evidence

`libOKSMARTPPCS.so` contains the public JNI entry points already wrapped by the
Android probe:

- `Java_com_vstarcam_JNIApi_connect`
- `Java_com_vstarcam_JNIApi_login`
- `Java_com_vstarcam_JNIApi_write`
- `Java_com_vstarcam_JNIApi_writeCgi`
- `Java_com_vstarcam_JNIApi_disconnect`

It also contains internal read/write/connect surfaces that are likely needed
for the managed Windows replacement:

- `client_read`
- `client_command_read`
- `client_command_read_v3`
- `client_write`
- `client_write_command`
- `client_write_cgi`
- `PPCS_Read`
- `XQP2P_Read`
- `vp_session_read`
- `HLP2P_Read`
- `PPCS_Write`
- `XQP2P_Write`
- `vp_session_write`
- `HLP2P_Write`
- `PPCS_Check_Buffer`
- `XQP2P_Check_Buffer`
- `vp_session_check`
- `HLP2P_Check_Buffer`
- `vp_channel_create`
- `vp_channel_send_packet`
- `vp_channel_read_packet`
- `vp_channel_read`
- `vp_channel_write`

The same library includes multiple protocol packet helpers:

- `cs2p2p_PPPP_Proto_Write_Header`
- `cs2p2p_PPPP_Proto_Read_Header`
- `cs2p2p_PPPP_Proto_Write_DevLgn`
- `cs2p2p_PPPP_Proto_Read_DevLgn`
- `cs2p2p_PPPP_Proto_Write_P2PReq`
- `cs2p2p_PPPP_Proto_Read_P2PReq`
- `cs2p2p_PPPP_Proto_Write_RSLgn`
- `cs2p2p_PPPP_Proto_Read_RSLgn`
- `cs2p2p_PPPP_Proto_Write_RlyReq`
- `cs2p2p_PPPP_Proto_Read_RlyReq`
- `cs2p2p_PPPP_Proto_Write_RlyPkt`
- `cs2p2p_PPPP_Proto_Read_RlyPkt`
- `cs2p2p_PPPP_Proto_Write_PunchPkt`
- `cs2p2p_PPPP_Proto_Read_PunchPkt`
- `cs2p2p_PPPP_Proto_Write_P2PRdy`
- `cs2p2p_PPPP_Proto_Read_P2PRdy`
- `cs2p2p_PPPP_DRW_Write_Header`
- `cs2p2p_PPPP_DRW_Read_Header`

Encryption-related strings in `libOKSMARTPPCS.so` and `libOKSMARTJIAMI.so`:

- `cs2p2p__P2P_Proprietary_Encrypt`
- `cs2p2p__P2P_Proprietary_Decrypt`
- `TCPRelay_Proprietary_Encrypt`
- `TCPRelay_Proprietary_Decrypt`
- `CRCSelect4Key`
- `md5Crypto`
- `cryptoKey`
- `AES_encrypt_eye`
- `AES_decrypt_eye`
- `vsMakeEncKey`

This strongly suggests the Windows client should start from the existing
`cs2p2p` packet family already partially implemented for older A9 variants, but
must account for VeePai/XQP2P/HLP2P session selection and encryption.

## Evidence Artifacts To Reuse

- `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-success-2026-05-28-185345.txt`
- `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-livecgi-fixed-2026-05-28-202924.txt`
- `.my/plan/m38-a9-camera/captures/a9-phone-capture-success-2026-05-28-222832.txt`
- `.my/plan/m38-a9-camera/captures/a9-phone-video-realtest-2026-05-28-230251.txt`
- `.my/plan/m38-a9-camera/captures/vue990-apk/`

## Next Questions

- Can the `DAS-...` server field be parsed into endpoints, keys, or mode flags?
- Do native strings/symbols expose enough packet names to map connect/login?
- Is there a callable native read method we can instrument once more as an
  oracle before replacing the protocol?
- Can channel bytes be captured safely in a bounded way from the Android oracle
  to identify codec/framing before writing the Windows decoder?

## Current Phase 22 Preflight

- Added a Windows C# `A9Vue990StatusClient`.
- Added `BodyCam.A9Probe vue990-status`.
- Added `BodyCam.A9Probe vue990-http-media`.
- Added `A9Vue990CgiCommandBuilder`.
- Added `A9WindowsNativeVue990RealTests`.
- Connected Windows live run reached `192.168.168.1:81`, parsed the Vue990
  status, and passed the gated Windows-native status RealTest.
- Direct Windows HTTP media probes returned no JPEG/video bytes. Snapshot,
  videostream, livestream, MJPEG, APK snapshot variants, and record/params CGI
  candidates returned `404`; only `get_status.cgi` returned `200`.

## `DAS-...` Observation

The observed `server` value has:

- prefix: `DAS-`
- hex payload length: `192`
- decoded payload length: `96` bytes
- first 16 decoded bytes:
  `8E D7 6A 33 80 D9 98 EC DA 94 D6 D8 05 A3 68 77`

Added managed C# analysis:

- class: `A9Vue990DasServerParameter`
- command: `BodyCam.A9Probe vue990-das`
- live artifact:
  `.my/plan/m38-a9-camera/captures/phase-23-das-analysis-2026-05-29.json`

The live 2026-05-29 DAS run showed:

- decoded payload length: `96` bytes
- known magic present: `8ED76A3380D998ECDA94D6D805A36877`
- entropy: about `6.29` bits/byte
- plaintext-looking: `false`
- common-port IPv4 endpoint heuristic: no candidates

Conclusion: the status `server` value is not a simple hidden IP/port list. Treat
it as opaque encrypted/encoded session or relay material until
`libOKSMARTPPCS.so` connect parsing is understood.

## Phase 24 DAS Decode Result

Native `ConnectByServer` parsing does decrypt the observed `DAS-...` payload.
The managed C# implementation now matches the native derivation:

- crypto: AES-CBC/no-padding
- key: first 16 ASCII chars of uppercase `MD5(61 zero bytes)`
- IV: first 16 ASCII chars of uppercase `MD5(78 zero bytes)`
- decoded relay hosts:
  `47.98.128.117`, `120.78.3.33`, `47.109.80.221`
- decoded payload text:
  `53BAH050-\x13\x11r/=K=00011,a+a+a,47.98.128.117-120.78.3.33-47.109.80.221,BKGD,9047F8F88`

This confirms the DAS value is relay/session material, not media payload.

## Phase 35 Android Managed Stream Result

The Android C# probe now contains a classic PPPP stream attempt using shared
managed control-channel builders:

- `ConnectUser`
- `VideoResolution`
- `StartVideo`
- `StopVideo`
- `DeviceStatus`

Live run on `2026-05-29 15:26 +02:00`:

- phone IP: `192.168.168.101/24`
- camera: `192.168.168.1`
- status port: TCP `81`
- sent local `LanSearch`: `F1300000`
- sent local XOR `LanSearch`: `2CBA5F5D`
- remote `PunchPkt` / `P2pReady`: none
- managed image/video frames: none

Conclusion: the MC/Vue990 camera does not expose the classic local PPPP stream
session even though the old generic A9 C# stream code supports that path. The
working Vue990 app must be using the Vue990/OKSMART relay/session path.

## Phase 36 Proprietary Codec Result

Native export/disassembly work identified the proprietary codec used under the
relay helpers:

- P2P encrypt/decrypt exports:
  `_Z31cs2p2p__P2P_Proprietary_EncryptPKcPKhPht` and
  `_Z31cs2p2p__P2P_Proprietary_DecryptPKcPKhPht`
- TCP relay encrypt/decrypt exports:
  `_Z29_TCPRelay_Proprietary_EncryptPKhS0_Pht` and
  `_Z29_TCPRelay_Proprietary_DecryptPKhS0_Pht`
- The TCP relay wrapper derives an ASCII key string from the first two seed
  bytes as `%02X%02X`, then calls the same P2P proprietary codec.
- The codec uses the same 256-byte table as the known `F1300000 -> 2CBA5F5D`
  discovery XOR path, with dynamic 4-byte keys.

Managed C# now implements this in `A9Vue990PpcsEncryptionCodec`:

- `DeriveProprietaryKeyBytes(...)`
- `ProprietaryEncode(...)`
- `ProprietaryDecode(...)`
- `TcpRelayEncode(...)`
- `TcpRelayDecode(...)`

This does not yet produce a complete relay request. The next missing piece is
the exact plain `TCPRlyReq` / `TCPRSLgn` structure plus the correct relay
encryption seed.

## Windows Transport Fingerprint

Added managed C# fingerprinting:

- class: `A9Vue990PpcsTransportProbeClient`
- command: `BodyCam.A9Probe vue990-ppcs-transport`
- live artifact:
  `.my/plan/m38-a9-camera/captures/phase-23/windows-ppcs-transport-2026-05-29.json`
- hardware-gated RealTest artifact:
  `.my/plan/m38-a9-camera/captures/phase-23/a9-windows-vue990-transport-realtest-2026-05-29-094500.json`

The probe performs status and DAS validation first, then a bounded non-CGI
fingerprint pass. It does not send the live-open command.

The 2026-05-29 run produced no direct local transport signal:

- TCP `65527`, `20190`, `32108`, `15203`, and `3478`: timed out
- UDP `65531`, `32108`, and `20190`: no target response to legacy LanSearch,
  SHIX seed, or JSON discover payloads

External PPPP research notes that HLP2P-family cameras may use encrypted
`DAS-...` server strings and HLP2P-specific transport ports. That is useful as
a hypothesis, but the current camera did not answer the local HLP2P port
fingerprint. Source:
https://palant.info/2025/11/05/an-overview-of-the-pppp-protocol-for-iot-cameras/

## Decoded Relay Probe

The relay probe added in Phase 24 uses the decoded DAS hosts after the status
fetch and DAS validation step.

Live artifact:

- `.my/plan/m38-a9-camera/captures/phase-24-ppcs-relay-probe-2026-05-29.json`

Result:

- Local camera PPCS/HLP2P candidate TCP ports still timed out.
- Local candidate UDP ports still returned no target response.
- TCP `65527` opened on all three decoded relay hosts.
- TCP `80` and `443` also opened on `47.98.128.117`.
- No relay returned banner bytes without the native hello.

Working conclusion: Windows can reach both the camera status service and the
decoded relay sockets. The blocker is now the managed HLP2P/PPCS hello and
session-open framing, not another HTTP path and probably not the Windows
firewall.

## Phase 25 Native-Header Relay Probe

Added managed C# packet builders for the first native-derived empty headers:

- `F1 00 00 00` - TCP hello
- `F1 70 00 00` - relay hello
- `F2 10 00 00` - server request

Live artifact:

- `.my/plan/m38-a9-camera/captures/phase-25-relay-hello-native-sequences-2026-05-29-123923.json`

Result:

- Windows fetched status and decoded DAS successfully.
- TCP `65527` opened on all decoded relays.
- The corrected native-derived headers and short same-socket sequences produced
  no response bytes.

Conclusion: the Windows relay path is not blocked by inability to connect. The
next missing item is exact second-stage session payload construction, likely
`TCPRlyReq` / `TCPRSLgn` with ids, endpoint data, flags, and crypto/checksum
fields.

## Phase 27 Windows C# Android Capture

Added a Windows C# command that uses the Android C# probe as the camera-network
host and packages the artifacts on Windows:

- command: `BodyCam.A9Probe vue990-android-capture`
- class: `A9AndroidPhoneCaptureClient`
- output directory:
  `.my/plan/m38-a9-camera/captures/phase-27-android-csharp-orchestrated-2026-05-29-131301/`

Result:

- still JPEG downloaded: `25062` bytes, `640x480`, SHA-256
  `E2073095B709B01ADF28230771ECFD33E26E4DC70C2FCAF88D04301EED92FB3F`
- six live frame JPEGs downloaded
- MJPEG AVI assembled on Windows with C#: `150612` bytes, `RIFF ... AVI`,
  SHA-256
  `D21CFBD55E001F6086D1C55498BDE66EFF9EED6E424DE9C1F80E7500E2680FC7`

This proves a C# command can retrieve picture and video artifacts through the
Android app path. It is not yet the pure Windows PPCS replacement because the
phone-side probe still depends on the Vue990 native PPCS/player libraries.

## Phase 28 Native Packet Oracle

Added an Android C# native-oracle mode that calls exported tiny packet creators
from `libOKSMARTPPCS.so`.

Live artifact:

- `.my/plan/m38-a9-camera/captures/phase-28-native-packet-oracle/a9-native-packet-oracle-2026-05-29-132028.txt`

Result:

- `create_Hello` returned `4` and wrote `F1000000`
- `create_RlyHello` returned `4` and wrote `F1700000`
- `create_SvrReq` returned `4` and wrote `F2100000`
- `TCPSend_Hello` loopback returned `0` and first emitted
  `000468007351673D7C5897F9`

These bytes are now represented in `A9Vue990P2pPacketBuilder`. Sending the
native `TCPSend_Hello` bytes to decoded TCP `65527` relays still produced no
response bytes. The remaining direct Windows blocker is the larger second-stage
payload, likely `TCPRlyReq` / `TCPRSLgn`.

## Phase 29-31 Plan Update

The next investigation path is no longer broad endpoint guessing:

- Phase 29: fake DAS/local relay oracle. Re-encode `DAS-...` with local relay
  hosts and let the Android native stack connect to a listener we control.
- Phase 30: native second-stage packet oracle. Use the working loopback socket
  pattern to call `TCPSend_TCPRlyReq` / `TCPSend_TCPRSLgn` after mapping their
  argument layouts.
- Phase 31: cross-platform C# PPCS library. Promote recovered packet builders,
  relay response parsing, login/control channel, live-open, and stream decoding
  into platform-neutral C#.

Managed DAS re-encoding now round-trips the current live `DAS-...` value, so
fake DAS generation is feasible enough to test.

## Phase 29/30 Outcomes

Phase 29 fake DAS/local relay attempt:

- Android accepted the `server_override` extra and started a phone-local
  listener on TCP `65527`.
- Rewritten DAS variants for short loopback, same-length loopback, and phone
  Wi-Fi IP were tested.
- All runs produced `JNIApi.Connect=4`, `JNIApi.login=False`, and
  `fake relay: connections=0`.

Conclusion: the decoded DAS relay-host token is not independently mutable. The
native code likely validates the trailing DAS token/checksum before it opens a
relay socket.

Phase 30 native second-stage oracle:

- `Write_TCPRlyReq` writes the plain/native struct shape.
- `Write_TCPRSLgn` writes the plain/native struct shape.
- `TCPSend_TCPRlyReq` emits a 64-byte framed TCP packet:
  `00386800FF4A81098AFBD7FC10B9AC58F88BFB0E502C933ACCA49F4373E35E905B7F5F9FAE89E783C6D3C825A82544AD780282477960DD03ED1BADDC35D6B2B3`
- `TCPSend_TCPRSLgn` emits a 68-byte framed TCP packet:
  `003C6800EC294C34003E3364A0270856599C9319361CA5159328E0CE2F1BC4DBE223080EA7179378EAFEFE31E23F984E12B291B1BE4BA15DCB7C604C98DEA2405298FE7D`
- These second-stage frames are now represented in
  `A9Vue990P2pPacketBuilder`.

Sending native-generated hello, `TCPRlyReq`, `TCPRSLgn`, and short sequences to
the decoded TCP `65527` relays still returned no bytes. The next unknown is no
longer the packet length/header shape; it is the correct dynamic argument
material used by the native helper calls.

Phase 32 tracks that dynamic-field mapping work.

First Phase 32 write-variant result:

- `Write_TCPRlyReq` and `Write_TCPRSLgn` both store the first seven bytes of
  client id at offset `0`, client-id length at offset `11`, and the first seven
  bytes of VUID at offset `12`.
- `sockaddr_cs2` occupies the final eight bytes in the baseline write structs.
- Non-zero `mode`, key, and flag values in `Write_TCPRlyReq` extend the
  non-zero tail beyond the 28-byte baseline.
- The tested relay-token argument did not affect the plain write struct,
  suggesting our guessed argument role is incomplete or only relevant in the
  `TCPSend_*` wrapper.

## Phase 33/34 Managed Android Findings

Shared C# protocol code now exists for the PPPP/PPCS pieces that are common
between Android and Windows:

- packet envelope: `F1 <type> <len:BE16> <payload>`
- packet types: LAN search, extended LAN search, DRW, DRW ACK, keepalive, close
- XOR1 discovery encoding/decoding
- DRW channel/index parsing
- DRW ACK generation
- video chunk reassembly around marker `55 AA 15 A8`

The Android managed-direct probe links these shared files into the APK and runs
without calling `JNIApi` or `AppPlayerApi`.

Live Android result:

- phone: `192.168.168.101/24`
- camera: `192.168.168.1`
- open local TCP ports found: `81`
- local HTTP media: no JPEG/MJPEG/H264; only `get_status.cgi` returned status
- UDP: plain PPPP, XOR1 PPPP, extended LAN search, SHIX/A9, and JSON discovery
  produced only self-echo from the phone
- managed image/video artifacts: none

The Android relay fallback decoded the same DAS relay hosts:

- `47.98.128.117`
- `120.78.3.33`
- `47.109.80.221`

But from the phone on camera Wi-Fi, all bounded TCP `65527` relay attempts timed
out before opening. This differs from Windows, where TCP `65527` had opened
previously. Current inference: Android on the camera AP is useful for local
camera tests, but relay testing should run from Windows unless the phone has an
internet route while still associated with the camera AP.

Next protocol target:

- Use Windows to run the bounded C# relay/session-open path with raw `.bin`
  saving.
- Replace fixed oracle frames with parameterized `TCPRlyReq` / `TCPRSLgn`
  builders.
- If any relay returns bytes, parse the PPCS envelope and then send the
  existing `A9Vue990CgiCommandBuilder` live request over the recovered channel.

## Phase 37 Android Direct Matrix Update

The Android direct C# probe now binds the process to the camera Wi-Fi network
and holds multicast/Wi-Fi locks while probing. It also tests a wider local UDP
matrix, including `32108`, `20190`, `65529`, and `65531`.

Live observation:

- Vue990 vendor app can show live video from the same phone/camera setup.
- While live, the vendor app owns UDP `0.0.0.0:65529`.
- After force-stopping the vendor app, the managed C# probe can own sockets but
  still receives only self-echo packets.
- No non-self UDP packet, `PunchPkt`, `P2pReady`, JPEG, or video payload was
  captured by managed direct probing.

Current inference: Android is suitable for running the shared C# client, but
the camera is not answering classic local PPPP discovery/session packets. The
next C#-only stream attempt should port the native Vue990/OKSMART session-open
packet path rather than add more guessed HTTP or UDP local endpoints.
