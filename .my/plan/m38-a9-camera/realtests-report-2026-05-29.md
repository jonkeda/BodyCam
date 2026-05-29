# Realtests Report - 2026-05-29

## Scope

Continue the Windows-native C# capture path for `@MC-0025644` after direct HTTP
media probing was ruled out.

## Outcomes

- Windows was connected to the camera AP as `192.168.168.100/24`.
- The camera status endpoint at `192.168.168.1:81` responded through managed
  C#.
- The camera reported `isCharge=1`; battery rose to `25` during the live run
  after USB power was connected.
- Added managed C# DAS/server-parameter analysis:
  `A9Vue990DasServerParameter`.
- Added the probe command:
  `BodyCam.A9Probe vue990-das`.
- Saved the live analysis artifact:
  `.my/plan/m38-a9-camera/captures/phase-23-das-analysis-2026-05-29.json`.
- Added bounded PPCS/HLP2P transport fingerprinting:
  `A9Vue990PpcsTransportProbeClient`.
- Saved the transport artifact:
  `.my/plan/m38-a9-camera/captures/phase-23/windows-ppcs-transport-2026-05-29.json`.
- Added and ran the hardware-gated Windows transport RealTest.

## DAS Result

- Prefix: `DAS-`
- Hex payload length: `192`
- Decoded payload length: `96` bytes
- Known magic:
  `8ED76A3380D998ECDA94D6D805A36877`
- Entropy: about `6.29` bits/byte
- Plaintext-looking: `false`
- Common-port IPv4 endpoint candidates: none

## Verification

- `A9Vue990DasServerParameterTests`: passed, 5/5.
- Focused DAS and CGI tests: passed, 7/7.
- `tools/BodyCam.A9Probe` build: passed after rerunning sequentially to avoid
  the known MAUI intermediate-XAML file lock.
- `BodyCam.A9Probe vue990-das`: passed and wrote JSON.
- `BodyCam.A9Probe vue990-ppcs-transport`: passed and wrote JSON.
- `A9WindowsNativeVue990RealTests`: passed, 2/2, with
  `A9_E2E=1`, `A9_WINDOWS_PPCS_E2E=1`, and
  `A9_CAMERA_IP=192.168.168.1`.

## Transport Fingerprint Result

- TCP `65527`, `20190`, `32108`, `15203`, `3478`: no open socket signal.
- UDP `65531`, `32108`, `20190`: no target response to bounded legacy
  LanSearch, SHIX seed, or JSON discover payloads.
- Battery rose to `49` by the transport RealTest while USB power was connected.

## Current Blocker

Windows-native image/video capture is not finished yet. The remaining blocker is
managed PPCS/XQP2P/HLP2P transport negotiation and login; the DAS value is not a
plain endpoint list, and direct HTTP media paths remain ruled out.

## Android C# Capture Stabilization

- The Android C# probe was stabilized so it completes after still/frame
  download and no longer depends on Android-side AVI assembly.
- Phone state during the successful run: `wlan0=192.168.168.101/24` on
  `@MC-0025644`.
- The run downloaded `a9-capture-2026-05-29-122420.jpg`, verified at
  `25605` bytes, `640x480`, SHA-256
  `1B41A829588F085F984DE3CEDADFDBD91F184EBA5259B92E298392B90C6B52B7`.
- The run downloaded six verified `640x480` frame JPEGs and wrote a frame
  manifest.
- Windows C# assembled those frames into
  `a9-video-2026-05-29-122420-mjpeg.avi`, verified at `153334` bytes with a
  `RIFF ... AVI` header and SHA-256
  `00777EBE8E2CE141ECF6D59DBCA3A328B382D68C4E67363E17979DC828DAF64C`.
- Hardware-gated
  `A9PhonePpcsPlayer_CapturesShortVideoArtifact` passed with
  `A9_E2E=1`, `A9_PHONE_VIDEO_E2E=1`, and
  `A9_CAMERA_IP=192.168.168.1`.
- Phase 26 documents this working Android C# picture/frame download plus
  Windows C# video packaging path.

## Windows C# Android Capture Command

- Added `BodyCam.A9Probe vue990-android-capture`.
- The command succeeded at `2026-05-29 13:13:01 +02:00`, driving the Android C#
  probe over ADB and pulling artifacts to
  `.my/plan/m38-a9-camera/captures/phase-27-android-csharp-orchestrated-2026-05-29-131301/`.
- Downloaded still JPEG: `25062` bytes, `640x480`, SHA-256
  `E2073095B709B01ADF28230771ECFD33E26E4DC70C2FCAF88D04301EED92FB3F`.
- Downloaded six frame JPEGs and assembled
  `a9-video-2026-05-29-131301-mjpeg.avi` on Windows with C#; verified
  `150612` bytes, `RIFF ... AVI`, SHA-256
  `D21CFBD55E001F6086D1C55498BDE66EFF9EED6E424DE9C1F80E7500E2680FC7`.
- Corrected Phase 25 native-header relay probes reached all decoded relay TCP
  `65527` hosts but still received no response bytes, so direct Windows capture
  remains blocked on exact session-open payload construction.

## Native Packet Oracle

- Added Android C# native-oracle mode.
- Native `create_Hello`, `create_RlyHello`, and `create_SvrReq` returned `4`
  and wrote `F1000000`, `F1700000`, and `F2100000`.
- Native `TCPSend_Hello` was captured through a phone-local loopback socket.
  The first observed value was `000468007351673D7C5897F9`; Phase 30 later
  observed `0004680067C6FE158F32C284` with the same `00046800` prefix.
- Artifact:
  `.my/plan/m38-a9-camera/captures/phase-28-native-packet-oracle/a9-native-packet-oracle-2026-05-29-132028.txt`.
- Sending the 12-byte native hello to decoded TCP `65527` relays still produced
  no response bytes. Direct Windows capture needs the larger `TCPRlyReq` /
  `TCPRSLgn` payload mapping.

## Fake DAS And Second-Stage Oracle

- Added managed DAS re-encoding and verified round-trip encryption for the
  current live `DAS-...` value.
- Added Android `server_override` and `fake_relay` probe modes.
- Tried fake-DAS relay rewrites for loopback, same-length loopback, and phone
  Wi-Fi IP. All runs started the local listener but recorded
  `fake relay: connections=0`.
- Phase 29 conclusion: decoded DAS relay hosts are probably covered by another
  validation token/checksum, so host replacement alone is blocked.
- Added native second-stage oracle calls for `Write_TCPRlyReq`,
  `Write_TCPRSLgn`, `TCPSend_TCPRlyReq`, and `TCPSend_TCPRSLgn`.
- Captured native-generated `TCPRlyReq` and `TCPRSLgn` framed packets and
  promoted them into managed C# packet builders.
- Retested decoded relay TCP `65527` hosts with those native-generated frames
  and hello+second-stage sequences; sockets opened but returned no bytes.
- Created Phase 32 to map the dynamic second-stage fields that the fixed
  oracle frames are missing.
- Ran the first Phase 32 write-variant oracle. It partially mapped byte offsets
  for client id, VUID, `sockaddr_cs2`, and numeric fields in native
  `Write_TCPRlyReq` / `Write_TCPRSLgn`.
- Focused A9/Vue990 tests passed: 13/13.
