# M38 A9 Realtests Report - 2026-05-28

## Summary

Phase 0 is now implemented as a repeatable probe loop:

- `tools/BodyCam.A9Probe` runs a readable and JSON-emitting camera probe.
- `A9ProbeRunner` is shared by the CLI and RealTests.
- A9 RealTests now cover discovery, protocol matrix detection, and selected-protocol first-frame capture.
- Hardware tests remain opt-in and skip cleanly without `A9_E2E=1`.

The `@MC-0025644` camera AP was reached and used from this machine. It exposed
an HTTP control API on `192.168.168.1:81`, but no direct RTSP/MJPEG/JPEG
endpoint was found. The working stream path is the Vue990/VStarcam/VeePai
PPCS/player path recovered later in this report.

The Samsung phone probe also reached the camera while connected directly to the
camera Wi-Fi. It saw the same control-only surface, which makes the laptop
firewall an unlikely primary explanation for the missing stream.

Vue990 was then installed on the Samsung phone and inspected. The app confirms
this camera family uses a VStarcam/VeePai P2P/player stack, not an obvious
plain RTSP/MJPEG local endpoint.

Phase 15 now has a phone-side metadata harness that reuses the Vue990/VStarcam
native PPCS path. It can connect, log in, receive command/control metadata, and
disconnect cleanly outside Vue990. The missing live-open step was recovered as
a CGI-over-PPCS command:
`livestream.cgi?streamid=10&substream=0&` on channel `1`.

With that command, the harness buffers stream bytes and receives VeePai player
metadata callbacks, including 640x480 draw metadata. Phase 15 remains
metadata-only by default; Phase 16/21 add explicit opt-in media capture on top
of the same proven path.

Follow-up planning split the remaining work into three explicit phases:

- Phase 16: opt-in image/video capture download, starting with the VeePai
  `AppPlayerApi.screenshot(...)` API.
- Phase 17: move the vendor-library workflow into C# orchestration and reduce
  Java to minimal JNI declaration/interface stubs.
- Phase 18: replace the vendor libraries and Java stubs with a pure C#
  VStarcam/PPCS implementation.

The C# capture implementation is now proven. The Android probe uses generated
.NET Android bindings for `Com.Vstarcam.JNIApi` and `Com.Veepai.AppPlayerApi`,
exposes explicit `capture_image=true` and `capture_video=true` modes, and has
downloaded verified image and video artifacts from the live camera stream.

The first Windows-native slice is also now proven: Windows joined the camera
AP, managed C# fetched and parsed Vue990 status, and the gated Windows status
RealTest passed. Direct HTTP media from Windows was then ruled out again with a
C# probe, so the remaining Windows-native media path must go through managed
PPCS rather than a hidden snapshot URL.

Three follow-up phase docs cover the capture attempts:

- Phase 19: use generated .NET Android bindings for a buildable C# screenshot
  attempt.
- Phase 20: use C# owned render-surface readback as the fallback if the vendor
  screenshot API fails.
- Phase 21: use a C# MJPEG AVI fallback when the vendor video download API
  refuses to start.

## Current Network Evidence

- Wired fallback is active on `Ethernet 4` with IPv4 `10.3.60.124`, so Wi-Fi
  can be dedicated to camera AP testing.
- `@MC-0025644` was visible as an open 2.4 GHz AP with BSSID
  `00:e0:4c:14:79:0f` and strong signal.
- After joining `@MC-0025644`, Windows assigned Wi-Fi IPv4
  `192.168.168.100/24`; the camera/AP gateway was `192.168.168.1`.
- The camera/AP gateway was pingable, and TCP scanning found only port `81`
  open among the common camera ports tested.
- After HTTP probing, `@MC-0025644` dropped out of the Windows scan and Wi-Fi
  returned to a disconnected state. The saved profile remains present.
- `Ethernet 2` had a stale-looking default route toward `192.168.1.1`, but the adapter was disconnected and ARP showed no camera-like neighbor.
- A follow-up three-pass Wi-Fi poll looked for `Nax`, `ACCQ`, `CAMS`, `TBAT`, `IPC`, `Cam`, `YSX`, `A9`, and `V720` SSID prefixes; none appeared.

Follow-up web/manual notes for the blue blinking camera state:

- V720/Naxclow AP mode commonly uses a `Nax_...` open hotspot and `192.168.169.1`.
- Some A9 setup guides mention `ACCQ_...`, `CAMS_...`, `TBAT...`, `IPC-...`, or `Cam-...` hotspot prefixes.
- Some mini A9 variants use `192.168.4.1` in AP mode, so this was added as a default probe candidate.

## Live Probe Outcome

Saved JSON artifacts from the pre-port-81 matrix runs:

`./captures/a9-probe-latest.json`
`./captures/mc-0025644-probe-latest.json`

Probe outcomes:

- RTSP `554`: timed out on all candidates.
- HTTP/MJPEG `80`: closed or timed out on all candidates.
- V720/Naxclow TCP `6123`: closed or timed out on all candidates.
- PPPP UDP `32108`: no unicast or broadcast `PunchPkt` response.
- PPPP UDP `20190`: no unicast or broadcast response.

Selected protocol: none.

The CLI exits successfully for this diagnostic "no camera found" state; the JSON
`success` field remains `false`.

Additional live `@MC-0025644` findings:

- `192.168.168.1:81/get_status.cgi?user=admin&pwd=admin` returned
  `HTTP 200 text/plain`.
- The control API identified the device as `realdeviceid=BK0025644WBPD`,
  `alias=BK7252N`, firmware/app version `21.120.101.34`.
- Generic HTTP/MJPEG and snapshot candidates on port `81`, including `/`,
  `/video`, `/video.cgi`, `/videostream.cgi`, `/mjpeg`, `/mjpegstream.cgi`,
  `/snapshot.jpg`, `/?action=stream`, and `/cgi-bin/snapshot.cgi`, returned
  empty `404` responses.
- The reusable probe now checks HTTP ports `80` and `81`, and records the
  `get_status.cgi` control API as reachable without marking it as a selected
  video protocol.
- The camera AP is time-sensitive: the mode light blinked blue during the
  short AP/setup window, and a watcher caught `@MC-0025644` when it reappeared.
- Additional CGI probes for params, snapshot, videostream, livestream,
  audiostream, RTSP, and ONVIF endpoints returned `404` except
  `get_status.cgi`.
- SSDP and WS-Discovery/ONVIF broadcast probes returned no responses.
- Follow-up checklist-driven probing repeated the scan while connected and
  confirmed only TCP `81` is open.
- A broader non-mutating HTTP endpoint matrix tested 75 common stream,
  snapshot, and control paths on port `81`; only `get_status.cgi` returned
  `HTTP 200`.
- UDP `32108` cam-reverse discovery and prompt-listed UDP `32108`/`20190`
  JSON/binary/plain discovery variants returned no responses.
- Current evidence does not justify Phase 11 TCP/H.264 or Phase 14
  V720/Naxclow implementation yet. The next branch is Phase 10-style
  BK7252N/CY365/SHIX protocol identification.
- A follow-up firewall check found the camera Wi-Fi network is `Public` with
  Windows Firewall policy `BlockInbound,AllowOutbound`. This can matter for
  unsolicited inbound UDP, but it is unlikely to hide TCP stream ports because
  outbound TCP to `192.168.168.1:81` works.
- SHIX/A9_PPPP UDP seed probing (`2c ba 5f 5d`) and fixed-local-port UDP
  probing on `32108` did not receive camera packets.
- SHIX/CY365-style raw TCP and HTTP POST command attempts against port `81`
  returned empty `HTTP 404` responses, with no JPEG/H.264-like stream bytes.
- Android phone-side probing from `wlan0=192.168.168.101/24` found the same
  result: only TCP `81` open, `get_status.cgi` returning `HTTP 200` with
  `device=BK0025644WBPD` and `alias=BK7252N`, no stream-like HTTP response,
  and no camera UDP discovery response.
- Phone-side report saved at
  `.my/plan/m38-a9-camera/captures/a9-phone-probe-2026-05-28-170031.txt`.

## Vue990 APK Outcome

- Vue990 is installed as `com.langxing.zhilianjiumu`, version `1.0.6`.
- The APK and splits were pulled to
  `.my/plan/m38-a9-camera/captures/vue990-apk/`.
- Static inspection shows a Flutter app with native VStarcam/VeePai libraries:
  `libOKSMARTPPCS.so` for P2P/control and `libOKSMARTPLAY.so` for live
  playback/decoding.
- The P2P library exposes `com.vstarcam.JNIApi` methods for connect, login,
  write, `writeCgi`, `clientSetVuid`, and buffer checks. It includes PPCS,
  XQP2P, HLP2P, and VEEPAI symbols.
- The player library exposes `com.veepai.AppPlayerApi` paths for live playback
  and H.264/H.265/JPEG decoding.
- App strings include `192.168.168.1`, `get_status.cgi`,
  `get_status.cgi?vuid=`, `supportVuid`, `clientSetVuid`,
  `https://vuid.eye4.cn`, and VeePai app IDs.
- Vue990's device setup configuration includes the same blue-light slow-flash
  setup state observed on the camera.
- User re-enabled phone Wi-Fi; ADB confirmed `@MC-0025644` on
  `wlan0=192.168.168.101/24`.
- Focused Vue990 logcat while connected to the camera AP saved
  `.my/plan/m38-a9-camera/captures/vue990-logcat-live-2026-05-28-1732.txt`.
  It showed app/UI activity only, with no P2P/DID/VUID/connect logs.
- The Vue990-style status URL
  `get_status.cgi?loginuse=admin&loginpas=888888` returned `HTTP 200`,
  `support_vuid=1`, `vuidResult=1`, `realdeviceid=BK0025644WBPD`, and
  `current_users=0`.
- User later confirmed Vue990 was connected and showing a live image.
- During that confirmed live-view state, laptop-side status changed to
  `current_users=1`, proving the camera recognized an active Vue990 viewer.
- A live-state common-port scan still found only TCP `81` open.
- Live-state probe artifact:
  `.my/plan/m38-a9-camera/captures/mc-0025644-live-vue990-probe-2026-05-28.json`
- Live-state HTTP matrix artifact:
  `.my/plan/m38-a9-camera/captures/mc-0025644-live-vue990-http-matrix-2026-05-28.txt`
- The live-state HTTP matrix still found only `get_status.cgi` variants as
  `HTTP 200`; snapshot, videostream, livestream, MJPEG, and generic live paths
  remained `404`.
- ADB was unavailable during the confirmed live-view state, so phone-side socket
  or logcat evidence of the actual Vue990 stream channel was not captured.
- After ADB reconnected, the phone was still on `@MC-0025644` and Vue990 was
  foregrounded, but the camera remained at `current_users=0` during two
  trigger windows.
- Recent Vue990 logcat showed repeated DNS failures for `liteos-master.eye4.cn`.
- Phone routing showed no default internet route while connected to the camera
  AP. The phone could not resolve `liteos-master.eye4.cn` and could not ping
  `8.8.8.8`.
- After the user found the Samsung setting, Vue990 entered live view while ADB
  remained connected. Camera status stayed at `current_users=1`.
- Live Vue990 logcat captured `P2PClient.c` creating a client for
  `BKGD00000100FMQLN`, connecting with `connectType=0x3F` and the camera's
  `DAS-...` server parameter, then starting `app_source_live`.
- Live playback logs showed `app_source_live_check_head` frames with
  `head->type:3`, increasing frame numbers, timestamps, and payload lengths
  around 9.8-10.2 KB.
- Live evidence artifacts:
  `.my/plan/m38-a9-camera/captures/vue990-live-logcat-2026-05-28-181457.txt`,
  `.my/plan/m38-a9-camera/captures/vue990-live-status-2026-05-28-181457.txt`,
  `.my/plan/m38-a9-camera/captures/vue990-live-sockets-2026-05-28-181457.txt`.
- Phone-side live HTTP probing still found only `get_status.cgi` as `HTTP 200`;
  snapshot, MJPEG, videostream, livestream, and generic live paths remained
  `404`.
- Normal Android socket inspection did not expose a simple stable camera-side
  media socket; the reusable path is the Vue990 private P2P/native playback
  stack.
- Static native-library inspection found `libOKSMARTPPCS.so` exports
  `com.vstarcam.JNIApi` entry points including `init`, `create`, `connect`,
  `login`, `writeCgi`, `write`, `checkBuffer`, and `clientSetVuid`.

## Phase 15 PPCS Harness Outcome

- Local JADX `1.5.5` was installed under
  `%LOCALAPPDATA%\CodexTools\jadx\v1.5.5\` and used to recover the
  `com.vstarcam.JNIApi` signatures and Vue990's Flutter plugin call order.
- `tools/BodyCam.A9PhoneProbe` now includes Java JNI wrappers, the required
  Vue990 native libraries, and a metadata-only PPCS probe path.
- The important Vue990 call pattern is `create`, `clientSetVuid`, `connect`,
  `login`, `checkMode`, command listener callbacks, `checkBuffer`, then
  `disconnect`/`destroy`.
- The native lifecycle was the key missing detail. Vue990 calls
  `JNIApi.retain(...)` before connect/login/check calls and balances it through
  the native release callback on the Android main thread. Without that pattern,
  the harness crashed in `client_connect`.
- The successful run used client id `BKGD00000100FMQLN`, VUID
  `BK0025644WBPD`, `connectType=0x3F`, `p2pType=1`, and the `DAS-...` server
  parameter returned by `get_status.cgi`.
- Successful native results: `JNIApi.init=true`, non-zero `JNIApi.create`,
  `clientSetVuid=true`, `connect=3`, `login=true`, `checkMode=[1,1]`,
  command listener type `24577` length `788`, `disconnect=true`, and
  `destroy=done`.
- The live-open command is now known: after PPCS login, call `JNIApi.writeCgi`
  on channel `1` with `livestream.cgi?streamid=10&substream=0&`.
- The live-open command returned `true`, produced a command ack
  (`type=24631`, `len=33`), and made channel `1` show buffered stream bytes
  (`checkBuffer=[0,0,15064]` on the repeat run).
- Successful report:
  `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-success-2026-05-28-185345.txt`
- Successful logcat:
  `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-success-logcat-2026-05-28-185345.txt`
- The first player-layer attempt added a local `com.veepai.AppPlayerApi`
  wrapper and a dummy `Surface` render target for metadata-only observation.
- `libOKSMARTPLAY.so` loaded, `AppPlayerApi.init` completed,
  `createPlayer` returned a non-zero pointer, `setPlayerSource` returned
  `true` with the proven PPCS client pointer, and `start` returned `true`.
- The first player attempt without live-open did not emit callbacks. After the
  recovered live-open command, the same player path emitted progress/head/draw/
  gps callbacks and consumed the channel buffer.
- Repeat live-CGI player run: `checkPlayerSource=false`,
  `setPlayerSource=true`, `start=true`, callback counters reached
  `progress=33`, `head=32`, `draw=32`, `gps=32`, and draw metadata reported
  `width=640 height=480`.
- Matching player attempt report:
  `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-2026-05-28-201609.txt`
- Matching player attempt logcat:
  `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-logcat-2026-05-28-201609.txt`
- Matching live-CGI player report:
  `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-livecgi-fixed-2026-05-28-202924.txt`
- Matching live-CGI player logcat:
  `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-livecgi-fixed-logcat-2026-05-28-202924.txt`
- The Android probe UI crash after the first live-CGI success was traced to
  rendering the full growing report in the UI. The app now keeps a bounded
  on-screen report tail and continues writing the full report to disk.

## Phase 16 And 21 Capture Outcomes

- Manual still-image capture succeeded through the C# generated-binding path.
  Artifact:
  `.my/plan/m38-a9-camera/captures/phase-16/a9-capture-2026-05-28-222832.jpg`
  with `37244` bytes, `640x480`, SHA-256
  `3B7610B415D7748C1D28117B2FDEDF87E2FAFD45B53A54AC8EBE56BF36866C4E`.
- Hardware-gated still-image RealTest
  `A9PhonePpcsPlayer_CapturesStillImage` passed and pulled
  `.my/plan/m38-a9-camera/captures/phase-16/a9-capture-2026-05-28-223037.jpg`
  with `29678` bytes, `640x480`, SHA-256
  `42EF85EF58C7EF9CA788AB7BB65E5FD999493CF9183B115E654BD76C9E0A40F7`.
- Vendor short-video download was attempted through generated bindings. The
  live player path was active, but `AppPlayerApi.StartDown(...)` returned
  `False` and no `.ts` file was created.
- Phase 21 added a C# fallback: capture six verified `640x480` JPEG frames and
  package them as an MJPEG AVI with `MjpegAviWriter`.
- Manual short-video artifact:
  `.my/plan/m38-a9-camera/captures/phase-16/a9-video-2026-05-28-230038-mjpeg.avi`
  with `436722` bytes, `RIFF ... AVI`, SHA-256
  `A92E3C3B79A9CEE92166E9B92506DB320BCE6D9E19F3FD527CCA2823F01F3504`.
- Hardware-gated video RealTest
  `A9PhonePpcsPlayer_CapturesShortVideoArtifact` passed and pulled
  `.my/plan/m38-a9-camera/captures/phase-16/a9-video-2026-05-28-230235-mjpeg.avi`
  with `420638` bytes, `RIFF ... AVI`, SHA-256
  `E898807A7F7F9B9325057C69A453DB64B5EFEC8404C6DBD8CAB99C654A129130`.

## Phase 22 Windows-Native Outcome

- Windows connected to `@MC-0025644` with Wi-Fi IPv4 `192.168.168.101/24`,
  gateway `192.168.168.1`, and strong signal.
- `BodyCam.A9Probe vue990-status --host 192.168.168.1` returned `HTTP 200`
  through managed C# and parsed `deviceid=BKGD00000100FMQLN`,
  `realdeviceid=BK0025644WBPD`, `alias=BK7252N`, `battery=100`, and the
  `DAS-...` server parameter.
- The gated Windows-native status RealTest passed while connected to the camera
  subnet.
- `BodyCam.A9Probe vue990-http-media` tested 64 direct HTTP media candidates
  plus exact APK-derived variants. All snapshot/video/livestream candidates
  returned `404`; only `get_status.cgi` returned `200`; no JPEG frames or video
  bytes were downloaded directly from port `81`.
- Added managed C# `A9Vue990CgiCommandBuilder` for the live CGI request that
  must be sent after PPCS login.
- Created Phase 23 for the next implementation slice: managed PPCS
  connect/login/live-open and raw channel `1` byte capture from Windows.

## Verification

- `dotnet build .\tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -f net10.0-windows10.0.19041.0 -p:SkipBuildNumberIncrement=true`
  - Result: passed after the port `81` control-API probe update.
- `dotnet test .\src\BodyCam.Tests\BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0 --filter "FullyQualifiedName~BodyCam.Tests.Services.Camera.A9" -p:SkipBuildNumberIncrement=true`
  - Result: passed, 12/12.
- `dotnet test .\src\BodyCam.RealTests\BodyCam.RealTests.csproj -p:TargetFrameworks=net10.0-windows10.0.19041.0 -f net10.0-windows10.0.19041.0 --filter "FullyQualifiedName~BodyCam.RealTests.A9" --no-restore -p:SkipBuildNumberIncrement=true`
  - Result: passed, 4/4 skipped because `A9_E2E` was not set.
- RealTests with `A9_E2E=1` and `A9_DISCOVERY_E2E=1`
  - Result: passed test-runner execution, 4/4 skipped because no camera answered discovery or no `A9_CAMERA_IP` was set.
- RealTests with `A9_E2E=1`, `A9_DISCOVERY_E2E=1`, and
  `A9_CAMERA_IP=192.168.168.1`
  - Initial result: one failure because the legacy `A9Session` RealTest assumed
    PPPP/MJPEG whenever a camera IP was set.
  - Follow-up result after adding a PPPP preflight gate: passed test-runner
    execution, 4/4 skipped because this BK7252N camera exposes only the HTTP
    status/control API discovered so far.
- `dotnet build .\tools\BodyCam.A9PhoneProbe\BodyCam.A9PhoneProbe.csproj -f net10.0-android -p:SkipBuildNumberIncrement=true`
  - Result: passed after embedding managed assemblies in the APK and enabling
    cleartext HTTP for the camera status API.
- `dotnet build .\tools\BodyCam.A9PhoneProbe\BodyCam.A9PhoneProbe.csproj`
  - Result: passed after adding the Vue990/VStarcam native PPCS harness.
- Samsung phone autorun probe against `192.168.168.1`
  - Result: completed and saved a report; matched the laptop-side control-only
    finding.
- Samsung phone PPCS native harness against `@MC-0025644`
  - Result: completed and saved a metadata report; native PPCS connect/login
    worked outside Vue990, while player/frame extraction remains pending.
- Samsung phone PPCS-plus-player harness against `@MC-0025644`
  - First result: completed and saved a metadata report; `libOKSMARTPLAY.so`
    loaded and the player started, but no player/frame callbacks fired before
    the live-open command was known.
- Samsung phone PPCS live-CGI player harness against `@MC-0025644`
  - Result: completed and saved metadata reports; `writeCgi` opened the live
    stream over PPCS, channel `1` buffered stream bytes, player metadata
    callbacks fired, and no visual payloads were stored.
- `dotnet build .\tools\BodyCam.A9PhoneProbe\BodyCam.A9PhoneProbe.csproj -f net10.0-android -p:SkipBuildNumberIncrement=true`
  - Result: passed after fixing the Android UI report-rendering crash.
- `dotnet build .\tools\BodyCam.A9PhoneProbe\BodyCam.A9PhoneProbe.csproj -f net10.0-android -p:SkipBuildNumberIncrement=true`
  - Result: passed after moving the PPCS/player orchestration into
    `Vue990PpcsSession` and adding the screenshot declarations.
- `adb install -r .\tools\BodyCam.A9PhoneProbe\bin\Debug\net10.0-android\com.bodycam.a9phoneprobe-Signed.apk`
  - Result: passed.
- `adb shell ip addr show wlan0`
  - Result: phone is on `192.168.1.67/24`, not the camera AP subnet.
- `adb shell ping -c 1 -W 1 192.168.168.1`
  - Result: 100% packet loss, so the live capture was not run.
- `adb shell cmd wifi connect-network '@MC-0025644' open`
  - Result: Android accepted the request, but the camera AP was not present in
    current scan results. The phone was restored to `jobaboe` afterward.
- C# capture intent while the phone briefly reported `@MC-0025644`
  - Result: not a media failure. Android fell back to `jobaboe` before the app
    started, the app reported `wlan0=192.168.1.67/24`, and the status fetch to
    `192.168.168.1:81` timed out with `TaskCanceledException`.
  - Artifacts:
    `.my/plan/m38-a9-camera/captures/a9-phone-capture-attempt-network-fallback-2026-05-28-222717.txt`
    and
    `.my/plan/m38-a9-camera/captures/a9-phone-capture-attempt-network-fallback-logcat-2026-05-28-222717.txt`.
- C# capture intent after re-enabling phone Wi-Fi and holding `@MC-0025644`
  - Result: passed. PPCS connect/login worked, live `writeCgi` opened channel
    `1`, player draw metadata reported `640x480`, `AppPlayerApi.screenshot`
    returned `True`, and the JPEG was pulled from the phone.
  - Image:
    `.my/plan/m38-a9-camera/captures/phase-16/a9-capture-2026-05-28-222832.jpg`
  - Bytes/dimensions/hash:
    `37244`, `640x480`,
    `3B7610B415D7748C1D28117B2FDEDF87E2FAFD45B53A54AC8EBE56BF36866C4E`.
  - Report/logcat:
    `.my/plan/m38-a9-camera/captures/a9-phone-capture-success-2026-05-28-222832.txt`
    and
    `.my/plan/m38-a9-camera/captures/a9-phone-capture-success-logcat-2026-05-28-222832.txt`.
- `dotnet test .\src\BodyCam.RealTests\BodyCam.RealTests.csproj -p:TargetFrameworks=net10.0-windows10.0.19041.0 -f net10.0-windows10.0.19041.0 --filter "FullyQualifiedName~A9PhonePpcsPlayerRealTests" --no-restore -p:SkipBuildNumberIncrement=true`
  - Result: passed test-runner execution, 2/2 skipped because the phone/PPCS
    and capture gates were not set.
- `dotnet test .\src\BodyCam.RealTests\BodyCam.RealTests.csproj -p:TargetFrameworks=net10.0-windows10.0.19041.0 -f net10.0-windows10.0.19041.0 --filter "FullyQualifiedName~A9PhonePpcsPlayer_CapturesStillImage" --no-restore -p:SkipBuildNumberIncrement=true`
  - Result: passed with `A9_E2E=1`, `A9_PHONE_CAPTURE_E2E=1`, and
    `A9_CAMERA_IP=192.168.168.1`.
  - Image:
    `.my/plan/m38-a9-camera/captures/phase-16/a9-capture-2026-05-28-223037.jpg`
  - Bytes/dimensions/hash:
    `29678`, `640x480`,
    `42EF85EF58C7EF9CA788AB7BB65E5FD999493CF9183B115E654BD76C9E0A40F7`.
- `dotnet build .\tools\BodyCam.A9PhoneProbe\BodyCam.A9PhoneProbe.csproj -f net10.0-android -p:SkipBuildNumberIncrement=true`
  - Result: passed after adding `capture_video=true`, `tsToMP4`, and the
    C# MJPEG AVI fallback writer.
- C# video capture intent while the phone held `@MC-0025644`
  - Result: `StartDown(...)` returned `False`, then the C# fallback captured
    six verified `640x480` JPEG frames and wrote an MJPEG AVI.
  - Video:
    `.my/plan/m38-a9-camera/captures/phase-16/a9-video-2026-05-28-230038-mjpeg.avi`
  - Bytes/header/hash:
    `436722`, `RIFF ... AVI`,
    `A92E3C3B79A9CEE92166E9B92506DB320BCE6D9E19F3FD527CCA2823F01F3504`.
- `dotnet test .\src\BodyCam.RealTests\BodyCam.RealTests.csproj -p:TargetFrameworks=net10.0-windows10.0.19041.0 -f net10.0-windows10.0.19041.0 --filter "FullyQualifiedName~A9PhonePpcsPlayer_CapturesShortVideoArtifact" --no-restore -p:SkipBuildNumberIncrement=true`
  - Result: passed with `A9_E2E=1`, `A9_PHONE_VIDEO_E2E=1`, and
    `A9_CAMERA_IP=192.168.168.1`.
  - Video:
    `.my/plan/m38-a9-camera/captures/phase-16/a9-video-2026-05-28-230235-mjpeg.avi`
  - Bytes/header/hash:
    `420638`, `RIFF ... AVI`,
    `E898807A7F7F9B9325057C69A453DB64B5EFEC8404C6DBD8CAB99C654A129130`.
- Samsung phone PPCS live-CGI player rerun after UI fix
  - Result: completed, the app stayed alive after completion, and filtered
    logcat showed no `AndroidRuntime`/fatal crash.
- `dotnet test .\src\BodyCam.RealTests\BodyCam.RealTests.csproj -p:TargetFrameworks=net10.0-windows10.0.19041.0 -f net10.0-windows10.0.19041.0 --filter "FullyQualifiedName~A9PhonePpcsPlayerRealTests" --no-restore -p:SkipBuildNumberIncrement=true`
  - Result: passed test-runner execution, 1/1 skipped because
    `A9_PHONE_PPCS_E2E` was not set.
- RealTest with `A9_E2E=1`, `A9_PHONE_PPCS_E2E=1`, and
  `A9_CAMERA_IP=192.168.168.1`
  - Result: passed test-runner execution, 1/1 skipped because the phone had
    moved off the camera AP and reported `wlan0=192.168.1.67/24`.
- `dotnet build .\tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -f net10.0-windows10.0.19041.0 -p:SkipBuildNumberIncrement=true`
  - Result: passed after adding the Windows HTTP media probe and shared C#
    MJPEG AVI writer.
- `BodyCam.A9Probe vue990-status --host 192.168.168.1`
  - Result: passed while Windows was connected to `@MC-0025644`.
  - Artifact:
    `.my/plan/m38-a9-camera/captures/phase-22-windows-status-connected-2026-05-28.json`.
- `dotnet test .\src\BodyCam.RealTests\BodyCam.RealTests.csproj -p:TargetFrameworks=net10.0-windows10.0.19041.0 -f net10.0-windows10.0.19041.0 --filter "FullyQualifiedName~A9WindowsNativeVue990RealTests" --no-restore -p:SkipBuildNumberIncrement=true`
  - Result: passed with `A9_E2E=1`, `A9_WINDOWS_PPCS_E2E=1`, and
    `A9_CAMERA_IP=192.168.168.1`.
- `BodyCam.A9Probe vue990-http-media --host 192.168.168.1`
  - Result: no media; all direct media candidates returned `404`.
  - Artifacts:
    `.my/plan/m38-a9-camera/captures/phase-22/windows-http-media-probe-2026-05-28.json`
    and
    `.my/plan/m38-a9-camera/captures/phase-22/windows-http-media-apk-variants-2026-05-28.json`.
- `dotnet test .\src\BodyCam.Tests\BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0 --filter "FullyQualifiedName~A9Vue990CgiCommandBuilderTests" --no-restore -p:SkipBuildNumberIncrement=true`
  - Result: passed, 2/2.

## Next Step

The immediate hardware goal is complete: the C# Android probe can connect to
`@MC-0025644`, open the live PPCS/player stream, download a still JPEG, and
produce a bounded short-video AVI artifact.

The next engineering step requested by the user is stricter than product
integration: Windows-native C# capture without the Android phone helper.

Phase 22 tracks the deliverable goal, and Phase 23 now tracks the next protocol
slice. Current progress:

1. Added a shared C# `A9Vue990StatusClient`.
2. Added `BodyCam.A9Probe vue990-status`.
3. Added `A9WindowsNativeVue990RealTests`, gated by
   `A9_WINDOWS_PPCS_E2E=1`.
4. Created `ppcs-protocol-notes.md`.
5. Proved Windows-native status while joined to `@MC-0025644`.
6. Added `BodyCam.A9Probe vue990-http-media` and ruled out direct HTTP media.
7. Added `A9Vue990CgiCommandBuilder` for the live CGI request frame.

The next required proof is managed VStarcam/PPCS connect/login/live-open from
Windows and a bounded raw channel-1 byte dump. Windows-native image/video
decoding cannot honestly start until that boundary is crossed.
