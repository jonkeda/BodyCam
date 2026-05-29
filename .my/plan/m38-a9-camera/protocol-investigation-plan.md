# M38 A9 Protocol Investigation Plan

## Goal

Find the local video/control path for the currently connected `@MC-0025644`
camera without guessing a protocol implementation too early.

Known current hardware evidence:

- Camera AP SSID: `@MC-0025644`
- Camera/AP IP: `192.168.168.1`
- PC Wi-Fi IP when connected: `192.168.168.100/24`
- Confirmed open TCP port: `81`
- Confirmed status endpoint: `http://192.168.168.1:81/get_status.cgi`
- Device identity: `BK0025644WBPD`
- Device alias/chip hint: `BK7252N`
- Firmware/app version: `21.120.101.34`

## Checklist

- [x] Read existing phase documents and extract the protocol decision tree.
- [x] Create an ordered list of possible protocol/stream paths.
- [x] Confirm the camera is currently connected and `get_status.cgi` is live.
- [x] Repeat a broad local TCP port scan while connected to the camera AP.
- [x] Probe HTTP port `81` with a broad non-mutating endpoint matrix.
- [x] Probe direct RTSP options only if an RTSP-like port is open.
- [x] Probe direct HTTP MJPEG/JPEG options only on open HTTP-like ports.
- [x] Rerun cam-reverse PPPP UDP `32108` unicast and broadcast discovery.
- [x] Probe prompt-listed PPPP/iLnk UDP discovery variants on `32108` and `20190`.
- [x] Probe V720/Naxclow only if TCP `6123`, `192.168.169.1`, or Naxclow SSID evidence appears.
- [x] Research BK7252N / CY365 / `get_status.cgi` local protocol options on the internet.
- [x] Update `protocol-variant-investigation.md` with the final candidate matrix and conclusions.
- [x] Update `realtests-log.md` with high-level outcomes only.
- [x] Update `realtests-report-2026-05-28.md` with the final result of this investigation pass.

## Stream And Firewall Follow-Up Checklist

- [x] Confirm `@MC-0025644` is connected and status API still responds.
- [x] Inspect Windows Firewall posture without changing firewall settings.
- [x] Try one more direct stream pull pass against only reachable TCP services.
- [x] Try SHIX/CY365-style non-mutating local commands if documented or inferable.
- [x] Retry UDP discovery with same-socket receives and record whether firewall remains a plausible blocker.
- [x] Update this plan, the high-level log, and the report with outcomes.

## Mobile Phone Relay/Capture Plan

- [ ] Check whether the phone can see live video in Vue990 while connected to `@MC-0025644`.
- [x] If live video works on the phone, capture the phone's local traffic during live view.
- [ ] Prefer packet capture over phone-as-relay because phones usually NAT/isolate Wi-Fi sharing.
- [ ] If Android is available, try PCAPdroid/VPN capture or USB/ADB capture and export a `.pcap`.
- [ ] If iPhone is used, investigate a Mac/Windows-compatible packet capture route or app export.
- [ ] Use the captured traffic to identify stream ports, packet headers, credentials/session commands, and codec.

## Vue990 App Investigation Plan

- [x] Identify the likely Android package from public app metadata.
- [x] Reconnect the Samsung phone with ADB and check whether Vue990 is installed.
- [x] If installed, pull the Vue990 APK from the phone with `adb shell pm path`.
- [x] Inspect APK strings/resources/native libraries for `192.168.168.1`,
  `get_status.cgi`, BK7252N, CY365/SHIX-style commands, stream endpoints,
  UDP/TCP ports, and cloud/P2P SDK names.
- [x] If Vue990 live view works, use the APK findings to create a focused
  protocol probe rather than another broad URL/port scan.
- [x] If the phone is connected to the camera AP, collect filtered Vue990
  logcat and compare it with the static APK findings.
- [x] Repeat focused Vue990 logcat while the app is actually adding the camera
  or opening live view.
- [x] Probe the camera from the laptop while Vue990 is actively showing live
  video on the phone.
- [x] Create a dedicated Phase 15 plan for the Vue990 VStarcam/VeePai PPCS
  harness.
- [x] Recover JNI method signatures and native call order for the Phase 15
  harness.
- [x] Build and run an Android-side metadata-only PPCS harness using the Vue990
  native libraries.
- [x] Add a metadata-only VeePai player API/start attempt to the harness.
- [x] Recover the missing Vue990 live/open command or player source option that
  makes the player receive frames.
- [x] Create a C# generated-binding PPCS/player runner for the next capture
  attempt.
- [x] Run explicit C# still-image capture while the phone is on
  `@MC-0025644`.
- [x] Run explicit C# short-video artifact capture while the phone is on
  `@MC-0025644`.
- [x] Create the Windows-native C# capture phase for removing the Android phone
  helper from the runtime path.
- [x] Create PPCS protocol notes for the managed replacement work.
- [x] Implement a Windows-only status/topology probe.
- [x] Run the Windows-only status/topology probe while Windows is connected to
  `@MC-0025644`.
- [x] Probe direct HTTP media endpoints from Windows with the new C# command.
- [x] Create the managed PPCS control/raw-channel Phase 23 plan.
- [x] Add a C# DAS/server-parameter analyzer and run it against the live
  Windows-connected camera.
- [x] Add and run a bounded Windows PPCS/HLP2P transport fingerprint probe.
- [x] Decode the live `DAS-...` server parameter in managed C# and extract
  relay hosts.
- [x] Probe decoded relay hosts from Windows.
- [x] Re-run Android control capture as a stream-alive comparison and pull
  fresh image/frame artifacts.
- [x] Add a Windows C# MJPEG AVI assembly command for pulled frame sequences.
- [x] Create the managed HLP2P relay-hello Phase 25 plan.
- [x] Stabilize the Android C# capture path so the report completes after
  still/frame download.
- [x] Pull a fresh Android C# still image and frame sequence, then assemble the
  verified frames into an AVI on Windows with C#.
- [x] Add a Windows C# command that orchestrates the Android C# probe and
  downloads still/video artifacts from the phone.
- [x] Run corrected native-header relay probes against decoded TCP `65527`
  relay hosts from Windows.
- [x] Add an Android C# native packet oracle and confirm tiny PPCS packet
  creator bytes.
- [ ] Implement managed PPCS connect/login from Windows.
- [ ] Retrieve an image and short video directly from Windows C#.
- [ ] If the vendor screenshot API fails, try a C# owned render-surface
  capture fallback.

## Android Phone Probe App Plan

- [x] Create a small Android app that runs probes directly from the phone.
- [x] Include TCP port scan, HTTP status/stream endpoint probes, and UDP discovery probes.
- [x] Avoid packet capture, bridging, root, and vendor-app interception.
- [x] Build a signed debug APK.
- [x] Install on Samsung phone with ADB once USB debugging is connected and authorized.
- [x] Run the app while the phone is connected to `@MC-0025644`.
- [x] Copy/save the phone-side report and compare it to the laptop-side results.

## Rules

- Use short timeouts.
- Avoid mutating camera settings.
- Save raw captures under `.my/plan/m38-a9-camera/captures/`.
- Treat `get_status.cgi` as control/API reachability, not as video support.
- Do not implement Phase 11 or Phase 14 until the camera proves that branch.

## Outcomes

- RTSP direct probing was skipped by evidence: the connected-camera TCP scan
  found only port `81` open, with no RTSP-like ports such as `554`, `8554`, or
  `10554`.
- Direct HTTP MJPEG/JPEG probing on port `81` tested 75 common non-mutating
  endpoints. Only `get_status.cgi` returned `HTTP 200`; all stream/snapshot
  candidates were not matched.
- cam-reverse PPPP UDP `32108` discovery sent `LanSearch` to unicast,
  subnet broadcast, and global broadcast targets. No `PunchPkt` response was
  received.
- Prompt-listed PPPP/iLnk UDP discovery variants on `32108` and `20190`
  produced no response for the JSON, binary, or plain-text discovery probes
  attempted.
- V720/Naxclow probing was skipped by evidence: the camera is not on
  `192.168.169.1`, the SSID is not `Nax...`, and TCP `6123` is closed.
- Internet research found this is closest to the BK7252N/CY365 family. Public
  notes associate CY365 with SHIX JSON over PPPP/P2P rather than a simple local
  MJPEG/RTSP endpoint. No public source found a stock-firmware local stream URL
  for this exact `@MC` / BK7252N / `get_status.cgi` shape.

## Research References

- CY365 BK7252N teardown: https://www.elektroda.com/rtvforum/topic4156145.html
- BK7252N A9/OpenCam project: https://github.com/daniel-dona/beken7252-opencam
- PPPP protocol overview and app/protocol matrix: https://palant.info/2025/11/05/an-overview-of-the-pppp-protocol-for-iot-cameras/
- Vue990 Android app package: https://play.google.com/store/apps/details?id=com.langxing.zhilianjiumu
- Vue990 iOS listing: https://apps.apple.com/us/app/vue990/id6757391088

## Stream And Firewall Follow-Up Outcomes

- `@MC-0025644` remained connected during the follow-up. The status API still
  returned `HTTP 200`, with fresh battery/charging state.
- Windows Firewall is enabled and the camera Wi-Fi profile is categorized as
  `Public`. Effective policy is `BlockInbound,AllowOutbound`; unicast responses
  to multicast are enabled. This could affect unsolicited inbound UDP, but it is
  unlikely to hide remote TCP stream ports because outbound TCP to the camera is
  allowed and `192.168.168.1:81` responds.
- Direct TCP stream attempts against port `81` did not produce video bytes. The
  service returned empty `HTTP 404` responses for SHIX-style raw JSON, SHIX
  stream commands, and HTTP POST variants.
- The A9_PPPP/SHIX UDP seed (`2c ba 5f 5d`) did not receive any camera response.
- Fixed-local-port UDP probing on local port `32108` saw only this laptop's own
  broadcast echoes from its Wi-Fi/Ethernet addresses, not a response from
  `192.168.168.1`. This makes firewall blocking less likely as the only cause,
  though a temporary inbound allow-rule test could still be used later if
  desired.

## Mobile Phone Notes

- A phone is unlikely to be a simple transparent relay from laptop to camera.
  Most phone hotspots use NAT and client isolation, and many phones cannot join
  a Wi-Fi camera AP and provide a usable bridge to that same network at the same
  time.
- A phone is useful if Vue990 can show live video. In that case, the app can
  reveal the missing local/cloud protocol via APK inspection or packet capture.
- Public metadata identifies Vue990's Android package as
  `com.langxing.zhilianjiumu`. ADB later reconnected, the APK was pulled from
  the Samsung phone, and the native VStarcam/VeePai PPCS/player stack was
  confirmed.

## Vue990 APK Inspection Outcomes

- Samsung `SM_S931B` has Vue990 installed as `com.langxing.zhilianjiumu`,
  version `1.0.6`, installed from Google Play.
- Pulled the base APK and configuration splits to
  `.my/plan/m38-a9-camera/captures/vue990-apk/`.
- Vue990 is a Flutter app with native VStarcam/VeePai libraries. The important
  libraries are `libOKSMARTPPCS.so` for P2P/control and `libOKSMARTPLAY.so`
  for playback/decoding.
- `libOKSMARTPPCS.so` exposes `com.vstarcam.JNIApi` symbols for client create,
  connect, login, write, `writeCgi`, `clientSetVuid`, and buffer checks. It
  also includes PPCS, XQP2P, HLP2P, and VEEPAI P2P symbols, including LAN
  search and direct connect routines.
- `libOKSMARTPLAY.so` exposes `com.veepai.AppPlayerApi` symbols for player
  creation, live source setup, start/stop, screenshots, and H.264/H.265/JPEG
  decoding paths.
- Flutter/native strings reference `192.168.168.1`, `get_status.cgi`,
  `get_status.cgi?vuid=`, `clientSetVuid`, `supportVuid`,
  `https://vuid.eye4.cn`, and VeePai app IDs such as `veepai_OKAM` and
  `veepai_FOWL`.
- Vue990's bundled device configuration includes Wi-Fi camera setup language
  that matches the observed blue-light slow-flash setup state.
- After APK inspection the phone Wi-Fi interface was down, so no live Vue990
  add/live-view session could be captured yet.
- User re-enabled phone Wi-Fi and ADB confirmed the phone joined
  `@MC-0025644` as `192.168.168.101/24`, gateway `192.168.168.1`.
- A focused Vue990 logcat pass while connected to the camera AP saved
  `.my/plan/m38-a9-camera/captures/vue990-logcat-live-2026-05-28-1732.txt`.
  It showed only app/UI activity, not P2P/DID/VUID/connect logs.
- The Vue990 APK status URL
  `get_status.cgi?loginuse=admin&loginpas=888888` worked from the phone and
  returned `support_vuid=1`, `vuidResult=1`, `realdeviceid=BK0025644WBPD`,
  and `current_users=0`.
- User then confirmed Vue990 was connected and showing live video. During that
  state the laptop was also connected to `@MC-0025644` as
  `192.168.168.100/24`.
- The same status URL returned `current_users=1` while Vue990 live view was
  active, confirming the camera itself recognized an active viewer.
- A live-state probe saved
  `.my/plan/m38-a9-camera/captures/mc-0025644-live-vue990-probe-2026-05-28.json`.
  It still selected no supported direct protocol.
- A live-state HTTP matrix saved
  `.my/plan/m38-a9-camera/captures/mc-0025644-live-vue990-http-matrix-2026-05-28.txt`.
  It found only `get_status.cgi` variants as `HTTP 200`; snapshot, MJPEG,
  livestream, videostream, and generic live paths remained `404`.
- A live-state common-port scan still found only TCP `81` open.
- ADB was unavailable during the confirmed live-view state, so no phone-side
  socket/logcat evidence of the actual Vue990 stream session was captured.
- After reconnecting ADB, the phone was still on `@MC-0025644` as
  `192.168.168.101/24` and Vue990 was foregrounded, but the camera initially
  stayed at `current_users=0`.
- Two `current_users=1` trigger windows saved status logs under captures, but
  the camera did not enter live-view state during those windows.
- Recent Vue990 app logcat showed repeated DNS failures for
  `liteos-master.eye4.cn`.
- After the user found the Samsung setting, Vue990 entered live view while ADB
  stayed connected. Camera status stayed at `current_users=1`.
- Live Vue990 logcat captured the P2P path: `P2PClient.c` created a client for
  `BKGD00000100FMQLN`, connected with `connectType=0x3F` and the camera's
  `DAS-...` server parameter, then started `app_source_live`.
- Live playback logs showed `app_source_live_check_head` frames with
  `head->type:3`, increasing frame numbers, timestamps, and payload lengths
  around 9.8-10.2 KB.
- Phone-side live HTTP probing still found only `get_status.cgi` as `HTTP 200`;
  snapshot, MJPEG, videostream, livestream, and generic live paths remained
  `404`.
- Working conclusion: Vue990 moved the target from generic RTSP/MJPEG probing
  to a VStarcam/VeePai P2P plus CGI-over-P2P/player path. The next useful work
  is player/frame metadata on top of the proven VStarcam/VeePai PPCS harness.
- C# still-image capture succeeded through generated Android bindings and
  `AppPlayerApi.Screenshot(...)`; the pulled JPEG artifacts are verified at
  `640x480`.
- Vendor video download through `AppPlayerApi.StartDown(...)` returned `False`
  on the proven live player path, so the working short-video artifact is a C#
  bounded screenshot sequence packaged as an MJPEG AVI.
- New active target: replace the phone-helper runtime path with direct Windows
  C# capture. This is tracked in Phase 22 and backed by
  `ppcs-protocol-notes.md`.
- Windows later joined `@MC-0025644` as `192.168.168.101/24`; the managed C#
  status probe succeeded and the gated Windows-native status RealTest passed.
- Direct Windows HTTP media probing with `BodyCam.A9Probe vue990-http-media`
  tested common and APK-derived snapshot/video/livestream paths. All media
  candidates returned `404`; only `get_status.cgi` returned `200`, with no JPEG
  or video bytes.
- Windows DAS analysis with `BodyCam.A9Probe vue990-das` parsed the live
  `DAS-...` server parameter into a 96-byte opaque payload with known magic but
  no plaintext endpoint or common-port IPv4 candidate. This confirms the next
  blocker is the PPCS/XQP2P/HLP2P transport negotiation, not another HTTP URL.
- Windows transport fingerprinting with `BodyCam.A9Probe vue990-ppcs-transport`
  found no direct local signal on TCP `65527`, `20190`, `32108`, `15203`, or
  `3478`, and no target UDP response on `65531`, `32108`, or `20190`. This
  pushes the next attempt toward native `ConnectByServer`/DAS parsing rather
  than another broad port scan.
- Phase 23 now tracks the next viable implementation slice: managed PPCS
  connect/login/live-open and a bounded raw channel `1` byte dump.
- Phase 24 recovered the DAS decrypt path in C#: AES-CBC/no-padding with native
  MD5-derived ASCII key/IV values. The decoded relay hosts are
  `47.98.128.117`, `120.78.3.33`, and `47.109.80.221`.
- Windows relay probing found TCP `65527` open on all three decoded relay
  hosts. No bytes are returned without the missing native hello/session-open
  packets.
- The camera Wi-Fi profile is `Public` and Windows Firewall is enabled, but
  current evidence does not point to firewall as the primary blocker: Windows
  reaches `get_status.cgi`, receives real HTTP `404` media responses, and opens
  decoded relay TCP sockets.
- Android control capture on 2026-05-29 confirmed the stream is alive: a fresh
  `640x480` JPEG and six live frame JPEGs were pulled, then the frames were
  assembled on Windows into an MJPEG AVI using C#.
- Phase 25 now tracks the next viable implementation slice: managed HLP2P relay
  hello/server-request packets against decoded TCP `65527` relays.
- Phase 26 stabilized the Android C# capture path. The app now writes a
  frame-sequence manifest after verified JPEG frames, skips fragile Android-side
  AVI assembly, and lets Windows C# assemble the MJPEG AVI from pulled frames.
  The manual run and hardware-gated video RealTest both passed.
- Phase 27 added `BodyCam.A9Probe vue990-android-capture`, a Windows C# command
  that launches the Android C# probe, pulls a fresh `640x480` JPEG and six
  frame JPEGs, and assembles an MJPEG AVI on Windows.
- Corrected native-header relay probing tried `F1000000`, `F1700000`,
  `F2100000`, and short same-socket sequences against decoded TCP `65527`
  relay hosts. All sockets opened; no response bytes arrived. Pure Windows
  capture remains blocked on the larger session-open payload, not on artifact
  packaging.
- Phase 28 added an Android C# native packet oracle. Native `create_Hello`,
  `create_RlyHello`, and `create_SvrReq` exactly matched the managed C#
  builders: `F1000000`, `F1700000`, and `F2100000`.
- The Phase 28 loopback socket oracle recovered native `TCPSend_Hello` bytes:
  `000468007351673D7C5897F9`. Phase 30 later observed another valid value,
  `0004680067C6FE158F32C284`, with the same `00046800` prefix. Sending these
  observed hello payloads to decoded relays still produced no response, so the
  next unknown is correct `TCPRlyReq` / `TCPRSLgn` dynamic material.

## Phase 15 PPCS Harness Outcomes

- Local JADX `1.5.5` was installed under
  `%LOCALAPPDATA%\CodexTools\jadx\v1.5.5\` and used to recover the JNI
  signatures and Vue990 Flutter plugin call order.
- The Android phone probe now includes Vue990 native libraries and Java wrappers
  for `com.vstarcam.JNIApi`.
- The first native PPCS attempt crashed because the client pointer was not held
  with Vue990's `retain`/release callback lifecycle. Reusing that lifecycle
  fixed the native crash.
- The successful harness run connected to `@MC-0025644` with client id
  `BKGD00000100FMQLN`, VUID `BK0025644WBPD`, `connectType=0x3F`,
  `p2pType=1`, and the `DAS-...` server parameter from `get_status.cgi`.
- Proven outside Vue990: native init, create, set VUID, connect, login,
  checkMode, command/control callback, disconnect, and destroy.
- The missing live-open step was recovered: after PPCS login, call
  `JNIApi.writeCgi` on channel `1` with
  `livestream.cgi?streamid=10&substream=0&`.
- That live-open command returned `true`, produced a command ack
  (`type=24631`, `len=33`), and made channel `1` show buffered stream bytes
  (`checkBuffer=[0,0,15064]` on the repeat run).
- The first VeePai player attempt loaded `libOKSMARTPLAY.so`, created a player
  with a dummy `Surface`, set the source to the proven PPCS client pointer, and
  started the player.
- Player-layer return codes were partly encouraging but not sufficient:
  `checkPlayerSource=false`, `setPlayerSource=true`, and `start=true`.
- Without the live-open command, no `app_player_*` callbacks fired. With the
  recovered live-open command, progress/head/draw/gps callbacks fired and the
  repeat run reached `progress=33`, `head=32`, `draw=32`, `gps=32`.
- Repeated player draw metadata reported `width=640 height=480`, which confirms
  the harness can now reach useful live stream metadata without saving visual
  payloads.
- The Android probe UI report-rendering crash was fixed by keeping only a
  bounded report tail on screen while still persisting the full report file.
- Successful artifacts:
  `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-success-2026-05-28-185345.txt`
  and
  `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-success-logcat-2026-05-28-185345.txt`.
- Player attempt artifacts:
  `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-2026-05-28-201609.txt`
  and
  `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-logcat-2026-05-28-201609.txt`.
- Live-CGI player artifacts:
  `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-livecgi-fixed-2026-05-28-202924.txt`
  and
  `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-livecgi-fixed-logcat-2026-05-28-202924.txt`.
- Added `A9PhonePpcsPlayerRealTests` as the hardware-gated repeatability test
  for this path. It is intentionally gated by both `A9_E2E=1` and
  `A9_PHONE_PPCS_E2E=1`, because it installs/runs the Android probe over ADB
  and requires the phone to be connected to `@MC-0025644`.
- Default RealTest verification skipped cleanly. The enabled pass also skipped
  because the phone had moved to `192.168.1.67/24` instead of the camera AP
  subnet.
- Follow-up phase planning now splits the next work into:
  - Phase 16: explicit still/video capture download, starting with
    `AppPlayerApi.screenshot(...)`.
  - Phase 17: C# orchestration with only minimal vendor JNI stubs if required.
  - Phase 18: pure C# VStarcam/PPCS replacement with no vendor libraries.
- A generated-binding C# runner now builds and installs through the Android
  probe app. It uses `Com.Vstarcam.JNIApi` and `Com.Veepai.AppPlayerApi`
  directly from C# and exposes an explicit `capture_image=true` still-image
  mode.
- The current blocker is network state: the Samsung phone is on
  `192.168.1.67/24`, not `@MC-0025644`, and cannot ping `192.168.168.1`.
- After the phone stayed on `@MC-0025644`, the C# capture path succeeded and
  downloaded verified 640x480 JPEG artifacts through `AppPlayerApi.screenshot`.

## Android Phone Probe App Outcomes

- Created `tools/BodyCam.A9PhoneProbe`, a native .NET Android app.
- The app probes from the phone's own Wi-Fi connection and reports TCP, HTTP,
  and UDP outcomes for `192.168.168.1`.
- Build passed with no warnings or errors.
- APK path:
  `tools/BodyCam.A9PhoneProbe/bin/Debug/net10.0-android/com.bodycam.a9phoneprobe-Signed.apk`
- Install helper:
  `tools/BodyCam.A9PhoneProbe/install-a9-phone-probe.ps1`
- ADB saw Samsung `SM_S931B`, installed the APK successfully, and launched the
  app.
- The first installed debug APK crashed because it did not embed the managed
  app assembly. The project now sets `EmbedAssembliesIntoApk=true`, and the APK
  includes `BodyCam.A9PhoneProbe`.
- The app now allows cleartext HTTP because the camera status API is plain
  HTTP on `192.168.168.1:81`.
- Phone-side run at `2026-05-28T17:00:31+02:00` completed while the Samsung
  phone was connected to `@MC-0025644` with `wlan0=192.168.168.101/24`.
- The phone found only TCP `81` open.
- The phone confirmed `get_status.cgi` returns `HTTP 200 text/plain` with
  `device=BK0025644WBPD`, `alias=BK7252N`, and `battery=85`.
- The phone-side HTTP matrix found no JPEG, multipart, H.264-like, or other
  stream response.
- Phone-side UDP discovery produced no camera response. Fixed-local-port UDP
  only saw the phone's own broadcast echoes from `192.168.168.101:32108`.
- Saved phone report:
  `.my/plan/m38-a9-camera/captures/a9-phone-probe-2026-05-28-170031.txt`
- This makes Windows Firewall a less likely explanation for the missing stream;
  the phone sees the same control-only camera surface as the laptop.
