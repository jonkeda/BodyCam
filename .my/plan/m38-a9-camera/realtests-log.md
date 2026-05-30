# M38 A9 Realtests Log

## 2026-05-28 14:13:55 +02:00

- Outcome: Read the M38 plan and confirmed Phase 0 is the right next step: reusable A9 probe CLI plus hardware-gated RealTests.
- Outcome: Checked the current network state. The machine is on `Exact-WLAN` (`10.3.81.153`); no obvious A9/V720/YsxLite camera AP SSID is visible yet.
- Outcome: AliExpress direct page content was not readable from the browser tool, but search results align this product family with A9/YsxLite/V720-style mini Wi-Fi cameras and a blinking blue setup/pairing indicator.
- Outcome: Added the Phase 0 probe implementation skeleton: local interface discovery, candidate host selection, RTSP, HTTP/MJPEG, V720 TCP 6123, and PPPP UDP 32108/20190 probes.
- Outcome: Added `tools/BodyCam.A9Probe` CLI and new hardware-gated RealTests for discovery, protocol matrix detection, and selected-protocol first-frame capture.

## 2026-05-28 14:21:54 +02:00

- Outcome: CLI build passed for `tools/BodyCam.A9Probe` on `net10.0-windows10.0.19041.0`.
- Outcome: A9 unit/integration tests passed: 12/12 against protocol helpers and the fake UDP camera server.
- Outcome: A9 RealTests default safety gate passed: 4/4 skipped when `A9_E2E` was not set.
- Outcome: A9 RealTests with `A9_E2E=1` and `A9_DISCOVERY_E2E=1` completed without failures, but skipped hardware assertions because no camera answered discovery.
- Outcome: Live CLI probe saved `.my/plan/m38-a9-camera/captures/a9-probe-latest.json`; no supported protocol was detected on `10.3.80.1`, `192.168.1.1`, or `192.168.169.1`.
- Outcome: CLI now treats "no camera found" as a successful diagnostic run while keeping `success=false` in JSON.
- Outcome: Follow-up network check showed `Ethernet 2` is disconnected despite a stale-looking route, and ARP only showed the corporate Wi-Fi gateway.
- Outcome: Camera was not connected or used for a frame capture because the current machine network has no reachable A9 endpoint.

## 2026-05-28 14:34:42 +02:00

- Outcome: Fresh Wi-Fi scan still showed only `Exact-WLAN`, `Exact-IOT`, and `Exact-Mobile`; no visible A9/V720/YsxLite-style camera AP.
- Outcome: Added `192.168.4.1` as another default AP-mode probe candidate based on A9 variant setup references.
- Outcome: Expanded live CLI probe checked `10.3.80.1`, `192.168.1.1`, `192.168.169.1`, and `192.168.4.1`; no supported protocol was detected.
- Outcome: Reverified after the candidate change: CLI build passed, A9 unit/integration tests passed 12/12, and A9 RealTests skipped 4/4 without `A9_E2E`.
- Outcome: Polled Wi-Fi three more times for common camera AP prefixes (`Nax`, `ACCQ`, `CAMS`, `TBAT`, `IPC`, `Cam`, `YSX`, `A9`, `V720`); no camera SSID appeared.

## 2026-05-28 14:40:51 +02:00

- Outcome: Fresh Wi-Fi scan now sees `@MC-0025644` and `CS-A2.05`.
- Outcome: `@MC-0025644` is open, 2.4 GHz, 802.11g, 97% signal; this is the likely camera AP candidate.
- Outcome: `CS-A2.05` is WPA2-Personal, 5 GHz, 29% signal; this is less likely to be the A9 camera.

## 2026-05-28 14:43:57 +02:00

- Outcome: Added `wifi-ap` command to `tools/BodyCam.A9Probe` for SSID-level probing before joining the camera AP.
- Outcome: `wifi-ap --ssid @MC-0025644` confirmed the camera tag SSID is visible: open network, BSSID `00:e0:4c:14:79:0f`, 99% signal, 2.4 GHz channel 11.
- Outcome: PC is still connected to `Exact-WLAN` with local IPv4 `10.3.81.153`; IP/protocol probes should wait until the PC joins `@MC-0025644`.

## 2026-05-28 14:48:22 +02:00

- Outcome: User-provided screenshot shows `M01 Pro_D879B87FE6C9`.
- Outcome: Windows scan at this time only reported `Exact-WLAN`; `M01 Pro_D879B87FE6C9` was not visible to this PC scan.

## 2026-05-28 14:49:34 +02:00

- Outcome: User confirmed `M01 Pro_D879B87FE6C9` is visible in Windows Wi-Fi Settings and is a locked/protected Wi-Fi network.
- Outcome: Follow-up `netsh` scans still did not expose `M01 Pro_D879B87FE6C9`; they listed `Exact-Visitors`, `Exact-IOT`, `Exact-Mobile`, `Exact-WLAN`, `@MC-0025644`, and `CS-A2.05`.

## 2026-05-28 15:13:16 +02:00

- Outcome: Wired fallback is active on `Ethernet 4` with IPv4 `10.3.60.124`, so Wi-Fi can be used for camera testing without losing access.
- Outcome: Initial `netsh wlan show profiles` showed `M01 Pro_D879B87FE6C9` as a current-user profile with WPA2-Personal, key present, and manual connection mode.
- Outcome: `wifi-ap --ssid M01 Pro_D879B87FE6C9` reported the profile but no CLI-visible AP and no active connection.
- Outcome: Windows accepted an M01 connect request once, but Wi-Fi stayed on/returned to corporate networks and never connected to M01 within the wait window.
- Outcome: After disconnecting Wi-Fi and retrying, `netsh` reported no profile assigned/found for `M01 Pro_D879B87FE6C9`; the profile is no longer visible in `netsh` or the WLAN profile XML store.
- Outcome: Wi-Fi is currently disconnected and free for the user to join `M01 Pro_D879B87FE6C9` manually from Windows Settings; IP/protocol probing should resume after Windows reports it connected.

## 2026-05-28 15:31:18 +02:00

- Outcome: Created and added a Windows open-Wi-Fi profile for `@MC-0025644`, then connected to the camera AP successfully.
- Outcome: While connected, Windows assigned Wi-Fi IPv4 `192.168.168.100/24`; the camera/AP gateway was `192.168.168.1` with BSSID/MAC `00:e0:4c:14:79:0f`.
- Outcome: Initial A9 matrix probe against `@MC-0025644` found no supported RTSP, HTTP/MJPEG on port `80`, V720 TCP `6123`, or PPPP UDP `32108`/`20190` protocol.
- Outcome: Manual local diagnostics showed `192.168.168.1` was pingable and only TCP `81` was open among the common camera ports tested.
- Outcome: HTTP probing on `192.168.168.1:81` confirmed a live control API: `/get_status.cgi?user=admin&pwd=admin` returned `HTTP 200 text/plain`, `realdeviceid=BK0025644WBPD`, `alias=BK7252N`, and firmware/app version `21.120.101.34`.
- Outcome: Common generic MJPEG/snapshot paths on port `81` returned `404`; no first video frame was captured from `@MC-0025644` yet.
- Outcome: Updated the reusable A9 probe to include HTTP port `81` and to report the `get_status.cgi` control API as reachable without selecting it as a video protocol.
- Outcome: Verification after the port `81` update passed: CLI build passed, A9 unit/integration tests passed 12/12, and A9 RealTests skipped cleanly 4/4 without `A9_E2E`.
- Outcome: The `@MC-0025644` AP dropped out of the Windows scan after the HTTP probes; reconnect attempts currently leave Wi-Fi disconnected, so the next live run should wait until the AP is visible again.

## 2026-05-28 15:40:30 +02:00

- Outcome: User switched the camera on again, then Windows was polled for `@MC-0025644` for about 40 seconds.
- Outcome: `@MC-0025644` did not appear in the CLI-visible Wi-Fi scan; `CS-A2.05` remained visible.
- Outcome: Direct connect to the saved `@MC-0025644` profile was accepted by Windows, but Wi-Fi stayed disconnected and no `192.168.168.x` address was assigned.
- Outcome: `wifi-ap --ssid @MC-0025644 --no-protocol-probe` confirmed the saved open profile exists, but the AP is currently not visible and the PC is not connected.

## 2026-05-28 15:49:18 +02:00

- Outcome: User observed that the mode light blinks blue for a while and then stops; this matches the AP appearing only during a short setup/boot window unless the PC connects quickly.
- Outcome: A watcher caught `@MC-0025644` at 15:41:31 and connected successfully; Wi-Fi reported `192.168.168.100/24`, gateway `192.168.168.1`, and 98-99% signal.
- Outcome: Updated probe run saved `.my/plan/m38-a9-camera/captures/mc-0025644-probe-latest.json`; it confirmed `192.168.168.1:81/get_status.cgi` responds with `alias=BK7252N` and `device=BK0025644WBPD`, but selected no supported video protocol.
- Outcome: Extra HTTP CGI probes for user/password variants, params, snapshot, videostream, livestream, audiostream, RTSP, and ONVIF endpoints all returned `404` except `get_status.cgi`.
- Outcome: SSDP and WS-Discovery/ONVIF broadcast probes returned no responses.
- Outcome: Hardware-gated A9 RealTests initially exposed a stale assumption in `A9Session_ConnectsAndReceivesJpegFrame`: it tried PPPP whenever `A9_CAMERA_IP` was set.
- Outcome: Updated that RealTest to preflight PPPP support with `A9ProbeRunner`; rerun with `A9_E2E=1`, `A9_DISCOVERY_E2E=1`, and `A9_CAMERA_IP=192.168.168.1` now skips cleanly 4/4 instead of failing on this BK7252N control-API-only camera.

## 2026-05-28 16:05:06 +02:00

- Outcome: Created `.my/plan/m38-a9-camera/protocol-investigation-plan.md` so the remaining protocol work is tracked as a checklist and checked off one task at a time.
- Outcome: Re-read the M38 overview, roadmap, Phase 0, Phase 5, Phase 10, Phase 11, Phase 14, and saved `pmpt.md` to align the live investigation with the documented decision tree.
- Outcome: While still connected to `@MC-0025644`, repeated the broad TCP scan; only TCP `81` was open.
- Outcome: Broad non-mutating HTTP probing on port `81` tested 75 common stream/snapshot/control paths; only `get_status.cgi` returned `HTTP 200`.
- Outcome: Direct RTSP, alternate HTTP/MJPEG, cam-reverse UDP `32108`, prompt-listed UDP `32108`/`20190`, and V720/Naxclow paths were ruled out or skipped by current evidence.
- Outcome: Internet research points this hardware toward a BK7252N/CY365/SHIX-family variant, but no stock-firmware local stream URL was found for the exact `@MC` / BK7252N / `get_status.cgi` shape.
- Outcome: Updated `.my/plan/m38-a9-camera/protocol-variant-investigation.md`; next viable branch is app/APK/protocol capture rather than Phase 11 TCP/H.264 or Phase 14 V720 implementation.

## 2026-05-28 16:28:37 +02:00

- Outcome: Follow-up stream attempt confirmed the camera remained connected on `@MC-0025644`; `get_status.cgi` still returned `HTTP 200` with fresh battery/charging state.
- Outcome: Windows Firewall is enabled and the camera Wi-Fi network is categorized as `Public`; policy is `BlockInbound,AllowOutbound`, with unicast responses to multicast enabled.
- Outcome: Firewall could affect unsolicited inbound UDP, but it is unlikely to explain missing TCP stream ports because outbound TCP works and `192.168.168.1:81` responds.
- Outcome: SHIX/A9_PPPP UDP seed probing (`2c ba 5f 5d`) received no camera packets.
- Outcome: SHIX/CY365-style raw TCP and HTTP POST command attempts against port `81` returned empty `HTTP 404` responses; no stream bytes or JPEG/H.264-like payloads were captured.
- Outcome: Fixed-local-port UDP probing on local port `32108` saw only the laptop's own broadcast echoes, not a response from `192.168.168.1`.

## 2026-05-28 16:41:20 +02:00

- Outcome: Created `tools/BodyCam.A9PhoneProbe`, a native Android probe app that runs TCP, HTTP, and UDP checks from the phone's own Wi-Fi connection.
- Outcome: The phone app avoids packet capture, vendor-app interception, root, and bridging; it simply probes `192.168.168.1` directly while the phone is connected to `@MC-0025644`.
- Outcome: Android probe app build passed with no warnings or errors and produced `tools/BodyCam.A9PhoneProbe/bin/Debug/net10.0-android/com.bodycam.a9phoneprobe-Signed.apk`.
- Outcome: Added `tools/BodyCam.A9PhoneProbe/install-a9-phone-probe.ps1` for build-and-install via ADB.
- Outcome: ADB currently lists no connected Android device, so installation is waiting for the Samsung phone to be connected with USB debugging enabled and authorized.

## 2026-05-28 16:46:44 +02:00

- Outcome: ADB detected Samsung `SM_S931B`, installed `A9 Phone Probe`, and launched it successfully.
- Outcome: After launch, ADB reported the phone Wi-Fi interface as disconnected/no IP, so the app still needs to be run after the phone joins `@MC-0025644` and stays connected despite no internet.

## 2026-05-28 17:00:41 +02:00

- Outcome: User reported the Android probe app crashed; the debug APK had been installed without embedded managed assemblies, so the probe was rebuilt as a self-contained APK.
- Outcome: Enabled cleartext HTTP for the probe app so Android can reach `http://192.168.168.1:81`.
- Outcome: Phone stayed connected to `@MC-0025644` with Wi-Fi IP `192.168.168.101/24`, and the phone-side probe completed successfully.
- Outcome: Phone-side probe found only TCP `81` open and confirmed `get_status.cgi` returns `HTTP 200` with `device=BK0025644WBPD`, `alias=BK7252N`, and `battery=85`.
- Outcome: Phone-side HTTP and UDP probing found no JPEG/multipart/H.264 stream endpoint and no camera UDP discovery response; fixed-local-port UDP only saw the phone's own broadcast echoes.
- Outcome: The phone result makes a Windows laptop firewall block less likely as the cause of the missing stream.

## 2026-05-28 17:09:00 +02:00

- Outcome: User identified the vendor app as Vue990.
- Outcome: Public app metadata identifies the likely Android package as `com.langxing.zhilianjiumu`.
- Outcome: ADB was not connected at this moment, so the Vue990 APK could not yet be pulled from the Samsung phone.
- Outcome: Updated the protocol plan with a Vue990 APK-inspection branch as the next focused route for stream/protocol discovery.

## 2026-05-28 17:28:39 +02:00

- Outcome: Confirmed Vue990 is installed on the Samsung phone as `com.langxing.zhilianjiumu`, version `1.0.6`.
- Outcome: Pulled and extracted Vue990's APK and configuration splits under `.my/plan/m38-a9-camera/captures/vue990-apk/`.
- Outcome: APK inspection shows Vue990 is a Flutter app using VStarcam/VeePai native libraries, especially `libOKSMARTPPCS.so` for P2P/control and `libOKSMARTPLAY.so` for live playback.
- Outcome: App strings explicitly reference `192.168.168.1`, `get_status.cgi`, VUID support, `clientSetVuid`, and VeePai/VStarcam P2P paths.
- Outcome: The camera stream is now most likely behind Vue990's VeePai/PPCS control and player stack, not exposed as a simple RTSP/MJPEG URL.
- Outcome: The phone Wi-Fi interface was down after inspection, so no live Vue990 add/live-view session was captured yet.

## 2026-05-28 17:34:45 +02:00

- Outcome: User switched phone Wi-Fi back on; ADB confirmed the Samsung phone is connected to `@MC-0025644` with `wlan0=192.168.168.101/24`.
- Outcome: Focused Vue990 logcat capture saved `.my/plan/m38-a9-camera/captures/vue990-logcat-live-2026-05-28-1732.txt`.
- Outcome: Vue990 logcat showed app/UI activity only; no P2P, DID, VUID, `writeCgi`, or live-stream connect logs appeared.
- Outcome: The exact Vue990 APK status URL `get_status.cgi?loginuse=admin&loginpas=888888` returned `HTTP 200`, `support_vuid=1`, `vuidResult=1`, `realdeviceid=BK0025644WBPD`, and `current_users=0`.
- Outcome: The phone and camera are reachable, but Vue990 has not yet attempted or opened a live camera session.

## 2026-05-28 17:51:11 +02:00

- Outcome: User confirmed Vue990 is connected and showing a live camera image.
- Outcome: ADB was unavailable during the confirmed live-view state, so phone-side socket/logcat capture could not be collected.
- Outcome: Laptop was connected to `@MC-0025644` with `192.168.168.100/24` while the phone app was live.
- Outcome: Camera status changed to `current_users=1`, confirming the camera recognizes the Vue990 live viewer.
- Outcome: Live-state common-port scan still found only TCP `81` open.
- Outcome: Live-state A9 probe saved `.my/plan/m38-a9-camera/captures/mc-0025644-live-vue990-probe-2026-05-28.json` and still selected no direct protocol.
- Outcome: Live-state HTTP matrix saved `.my/plan/m38-a9-camera/captures/mc-0025644-live-vue990-http-matrix-2026-05-28.txt`; only `get_status.cgi` variants returned `HTTP 200`, while snapshot/video/live paths returned `404`.

## 2026-05-28 18:09:12 +02:00

- Outcome: ADB reconnected and the Samsung phone remained on `@MC-0025644` with `wlan0=192.168.168.101/24`.
- Outcome: Vue990 was running and foregrounded, but the camera stayed at `current_users=0` during two live-trigger capture windows.
- Outcome: Saved phone-side retry artifacts under captures, including Vue990 logcat, status wait logs, socket snapshots, and current state.
- Outcome: Vue990 app logcat repeatedly reported DNS failures for `liteos-master.eye4.cn`.
- Outcome: Phone routing showed no default internet route while connected to the camera AP; `liteos-master.eye4.cn` could not resolve and `8.8.8.8` could not be pinged.
- Outcome: Next live capture needs the phone to keep camera AP access while also having internet/cloud reachability, likely via mobile data.

## 2026-05-28 18:18:30 +02:00

- Outcome: User found the Samsung setting and Vue990 entered a live state while ADB stayed connected.
- Outcome: Camera status stayed at `current_users=1` during the capture; status artifacts were saved under captures.
- Outcome: Vue990 live logcat confirmed VStarcam/VeePai P2P playback: it created a P2P client for `BKGD00000100FMQLN`, connected with `connectType=0x3F` and the camera's `DAS-...` server parameter, then produced `app_source_live` frame logs.
- Outcome: Live playback frame logs showed `head->type:3` with frame numbers, timestamps, and payload lengths around 9.8-10.2 KB, confirming the app was consuming a real stream.
- Outcome: Phone-side live HTTP probing still found only `get_status.cgi` as `HTTP 200`; snapshot, MJPEG, videostream, livestream, and generic live paths remained `404`.
- Outcome: Normal Android socket inspection did not expose a simple stable camera-side media socket; the usable path is inside Vue990's private P2P/native playback stack.
- Outcome: No camera image/screenshot was captured or stored; the saved evidence is status, sockets, HTTP headers, and app log metadata only.
- Outcome: Static native-library check found `libOKSMARTPPCS.so` exports the expected `com.vstarcam.JNIApi` entry points (`init`, `create`, `connect`, `login`, `writeCgi`, `write`, `checkBuffer`, `clientSetVuid`), so a focused JNI/PPCS harness is feasible as the next implementation branch.

## 2026-05-28 18:30:12 +02:00

- Outcome: Created Phase 15, `Vue990 VStarcam/VeePai PPCS Harness`, as the next implementation branch.
- Outcome: Phase 15 is scoped to native-library loading, connection return codes, and stream metadata only; it explicitly excludes feed bridging, screenshots, payload storage, root access, and camera-setting mutations.

## 2026-05-28 18:56:29 +02:00

- Outcome: Installed local JADX `1.5.5` under `%LOCALAPPDATA%\CodexTools\jadx\v1.5.5\` and recovered Vue990's VStarcam JNI call shape without changing system-wide tooling.
- Outcome: Extended the Android phone probe with a metadata-only Vue990/VStarcam PPCS harness using the pulled native libraries.
- Outcome: The first PPCS attempt crashed because the native client pointer was used without Vue990's `retain`/release callback lifecycle; after mimicking that lifecycle, the native connect/login path worked.
- Outcome: Successful phone-side PPCS run connected to `@MC-0025644` using client id `BKGD00000100FMQLN`, VUID `BK0025644WBPD`, `connectType=0x3F`, `p2pType=1`, and the `DAS-...` server parameter from `get_status.cgi`.
- Outcome: The harness proved `JNIApi.init`, `create`, `clientSetVuid`, `connect`, `login`, `checkMode`, command/control callback, `disconnect`, and `destroy` outside Vue990.
- Outcome: `checkBuffer(1)` returned `[0,0,0]`, so playable frame extraction is not solved yet; the next useful branch is Vue990/VeePai player-frame metadata rather than another generic RTSP/MJPEG scan.
- Outcome: Saved the successful metadata report at `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-success-2026-05-28-185345.txt` and the matching logcat at `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-success-logcat-2026-05-28-185345.txt`.

## 2026-05-28 20:17:30 +02:00

- Outcome: Added a metadata-only `com.veepai.AppPlayerApi` wrapper and a PPCS-only autorun path to the Samsung probe app.
- Outcome: Android build/install passed, and the phone remained connected to `@MC-0025644` as `192.168.168.101/24`.
- Outcome: The probe loaded `libOKSMARTPLAY.so`, created a player, set the live source to the proven PPCS client pointer, and `AppPlayerApi.start` returned `true`.
- Outcome: No player frame callbacks fired, `checkBuffer(1)` stayed `[0,0,0]`, and the harness did not reproduce Vue990's `app_source_live_check_head` frame-header logs.
- Outcome: The missing piece is likely a Vue990 live/open command or player source option before player start, not a laptop firewall issue or a basic library-loading/signature issue.
- Outcome: Saved the player attempt report at `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-2026-05-28-201609.txt` and the matching logcat at `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-logcat-2026-05-28-201609.txt`.

## 2026-05-28 20:31:26 +02:00

- Outcome: Recovered the missing live-open step: after PPCS login, `JNIApi.writeCgi` on channel `1` with `livestream.cgi?streamid=10&substream=0&` returns `true`.
- Outcome: The live-open command produced a command ack (`type=24631`, `len=33`) and `checkBuffer(channel=1)` showed stream data buffered (`[0,0,15064]` on the repeat run).
- Outcome: The VeePai player now emits metadata callbacks in the harness: progress/head/draw/gps callbacks fired, including `app_player_draw_info width=640 height=480`.
- Outcome: The probe still stores no screenshots, video, or image payloads; saved artifacts are reports and log metadata only.
- Outcome: Fixed the Android probe UI report rendering crash by keeping a bounded on-screen log while still writing the full report to disk.
- Outcome: Rebuild passed, reinstall passed, and the repeat run stayed alive after completion with no `AndroidRuntime`/fatal crash in the filtered logcat.
- Outcome: Saved the repeat live-CGI player report at `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-livecgi-fixed-2026-05-28-202924.txt` and the matching logcat at `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-livecgi-fixed-logcat-2026-05-28-202924.txt`.

## 2026-05-28 20:36:37 +02:00

- Outcome: Added `A9PhonePpcsPlayerRealTests`, a hardware-gated RealTest for the phone/PPCS live-CGI player metadata path.
- Outcome: The new RealTest is gated by `A9_E2E=1` plus `A9_PHONE_PPCS_E2E=1`, installs/runs the Android probe over ADB, and asserts on PPCS login, live `writeCgi`, non-zero stream buffer, 640x480 draw metadata, clean destroy, and no fatal Android crash.
- Outcome: Default RealTest verification passed in skipped mode: 1/1 skipped when the phone/PPCS gate is not set.
- Outcome: Enabled RealTest verification skipped because the phone had moved off the camera AP and was on `192.168.1.67/24` instead of `192.168.168.x`.

## 2026-05-28 20:42:34 +02:00

- Outcome: Created Phase 16, `C#-First Vue990 Capture Download`, for the explicit opt-in step that may save a real image or short video artifact.
- Outcome: Phase 16 keeps Phase 15 metadata-only and moves visual capture behind separate capture gates.
- Outcome: The plan records that a literal no-Java rewrite is high risk because the vendor libraries expect Android JNI class names; the practical target is C# orchestration with only minimal Java/JNI stubs if required.
- Outcome: Updated the overview and roadmap to link Phase 16 and keep image/video storage out of Phase 15.

## 2026-05-28 20:51:17 +02:00

- Outcome: Used three subagents to prepare the longer C# and image-download path: C# vendor-adapter feasibility, capture API options, and pure-C# PPCS replacement roadmap.
- Outcome: Created Phase 17, `C# Vendor Adapter And Java Reduction`, to move the working PPCS/player state machine into C# while keeping only exact vendor JNI declaration/interface stubs if required.
- Outcome: Created Phase 18, `Pure C# VStarcam/PPCS Replacement`, as the final no-vendor-libraries/no-Java-stubs target.
- Outcome: Subagent capture review found a concrete still-image path: Vue990 exposes `AppPlayerApi.screenshot(long, String, int, int, float, float, int)`, and the first capture attempt should call it after `app_player_draw_info width=640 height=480`.
- Outcome: Updated Phase 16 with the screenshot API evidence, fallback render-surface readback path, and explicit image/video verification checklist.
- Outcome: Updated the overview and roadmap to link Phase 17 and Phase 18.

## 2026-05-28 21:02:09 +02:00

- Outcome: Created Phase 19, `Generated Binding Screenshot Spike`, for a buildable C# generated-binding capture attempt.
- Outcome: Added missing VeePai player native declarations for `screenshot`, `save`, `saveMP4`, `startDown`, and `stopDown`.
- Outcome: Added `Vue990PpcsSession` so the PPCS/player state machine and screenshot attempt are now orchestrated in C# instead of `PpcsProbeBridge.java`.
- Outcome: Added explicit `capture_image=true` autorun mode; metadata-only mode remains the default.
- Outcome: Android build passed and the updated probe APK installed successfully on the Samsung phone.
- Outcome: Added a hardware-gated C# RealTest, `A9PhonePpcsPlayer_CapturesStillImage`, that runs capture mode, asserts JPEG metadata, and pulls the image over ADB when `A9_PHONE_CAPTURE_E2E=1`.
- Outcome: Targeted RealTest verification passed in skipped mode: 2/2 skipped when the phone/PPCS and capture gates were not set.
- Outcome: The image was not downloaded yet because the phone is currently on `192.168.1.67/24`, cannot reach `192.168.168.1`, and must be reconnected to `@MC-0025644` before the live capture can run.
- Outcome: Created Phase 20, `C# Render Surface Capture Fallback`, as the next idea if the vendor screenshot API fails during the connected run.

## 2026-05-28 21:07:47 +02:00

- Outcome: Tried to reconnect the Samsung phone to saved network `@MC-0025644` through Android's `cmd wifi connect-network` shell path.
- Outcome: The camera AP was not present in current scan results; the connect attempt left Wi-Fi disconnected, then Wi-Fi auto-join was restored.
- Outcome: The phone is back on `jobaboe` with `wlan0=192.168.1.67/24`; no image capture was attempted because `192.168.168.1` is still unreachable.

## 2026-05-28 22:27:22 +02:00

- Outcome: The Samsung phone briefly reported `@MC-0025644` with `wlan0=192.168.168.100/24`, and `192.168.168.1` answered ping.
- Outcome: The C# capture intent was launched, but Android had already fallen back to `jobaboe` by the time the app started; the app report showed `wlan0=192.168.1.67/24`.
- Outcome: The capture run failed at the initial status fetch with `TaskCanceledException`, so no image was downloaded.
- Outcome: A 90-second follow-up watch did not see `@MC-0025644` in the phone scan results.
- Outcome: Saved the failed capture attempt report and logcat under captures as `a9-phone-capture-attempt-network-fallback-2026-05-28-222717.txt` and `a9-phone-capture-attempt-network-fallback-logcat-2026-05-28-222717.txt`.

## 2026-05-28 22:31:23 +02:00

- Outcome: Re-enabled phone Wi-Fi; the Samsung phone rejoined `@MC-0025644` as `192.168.168.100/24`, and `192.168.168.1` answered ping.
- Outcome: The C# generated-binding capture run succeeded: PPCS connect/login worked, live `writeCgi` opened channel `1`, player draw metadata reported `640x480`, and `AppPlayerApi.screenshot` returned `True`.
- Outcome: Downloaded the first real camera JPEG to `.my/plan/m38-a9-camera/captures/phase-16/a9-capture-2026-05-28-222832.jpg`; verified `37244` bytes, `640x480`, SHA-256 `3B7610B415D7748C1D28117B2FDEDF87E2FAFD45B53A54AC8EBE56BF36866C4E`.
- Outcome: Saved the successful capture report and logcat as `a9-phone-capture-success-2026-05-28-222832.txt` and `a9-phone-capture-success-logcat-2026-05-28-222832.txt`.
- Outcome: Fixed the capture RealTest harness so it clears stale phone reports and accepts C# boolean casing.
- Outcome: Hardware-gated `A9PhonePpcsPlayer_CapturesStillImage` passed with `A9_E2E=1`, `A9_PHONE_CAPTURE_E2E=1`, and `A9_CAMERA_IP=192.168.168.1`.
- Outcome: The RealTest pulled a second verified JPEG to `.my/plan/m38-a9-camera/captures/phase-16/a9-capture-2026-05-28-223037.jpg`; verified `29678` bytes, `640x480`, SHA-256 `42EF85EF58C7EF9CA788AB7BB65E5FD999493CF9183B115E654BD76C9E0A40F7`.

## 2026-05-28 23:03:20 +02:00

- Outcome: Added Phase 21, `C# MJPEG AVI Video Artifact`, after the vendor `AppPlayerApi.StartDown(...)` path returned `False` and created no `.ts` file.
- Outcome: Added a pure C# MJPEG AVI writer and a bounded `capture_video=true` fallback that captures six verified `640x480` JPEG frames from the live player path.
- Outcome: Manual video capture produced `.my/plan/m38-a9-camera/captures/phase-16/a9-video-2026-05-28-230038-mjpeg.avi`; verified `436722` bytes, `RIFF ... AVI`, SHA-256 `A92E3C3B79A9CEE92166E9B92506DB320BCE6D9E19F3FD527CCA2823F01F3504`.
- Outcome: Added hardware-gated `A9PhonePpcsPlayer_CapturesShortVideoArtifact`, gated by `A9_E2E=1` and `A9_PHONE_VIDEO_E2E=1`.
- Outcome: The video RealTest passed while the phone was on `@MC-0025644` and pulled `.my/plan/m38-a9-camera/captures/phase-16/a9-video-2026-05-28-230235-mjpeg.avi`; verified `420638` bytes, `RIFF ... AVI`, SHA-256 `E898807A7F7F9B9325057C69A453DB64B5EFEC8404C6DBD8CAB99C654A129130`.

## 2026-05-28 23:15:00 +02:00

- Outcome: Created Phase 22, `Windows-Native C# Capture`, as the active goal for image/video capture directly from Windows without the Android phone helper at runtime.
- Outcome: Created `ppcs-protocol-notes.md` to track evidence-backed VStarcam/VeePai PPCS constants, call order, callback observations, and protocol unknowns.
- Outcome: Updated Phase 18 so it is now the managed PPCS protocol-replacement foundation for Phase 22.
- Outcome: Spawned subagents to investigate the managed PPCS/session gaps and the Windows media extraction path in parallel.
- Outcome: Added shared C# `A9Vue990StatusClient`, the `BodyCam.A9Probe vue990-status` command, and a Windows-native status RealTest gated by `A9_WINDOWS_PPCS_E2E=1`.
- Outcome: Windows-native status command captured topology but timed out against `192.168.168.1` because Windows Wi-Fi was software-off and the PC was not on `192.168.168.x`.
- Outcome: The Windows-native status RealTest builds and skips safely both by default and when explicitly gated while Windows is off the camera subnet.
- Outcome: Native string reconnaissance found `PPCS_Read`, `XQP2P_Read`, `vp_session_read`, `vp_channel_read`, and `cs2p2p_PPPP_*` packet helper names, confirming the next hard step is managed PPCS connect/login/channel framing.

## 2026-05-28 23:43:30 +02:00

- Outcome: Windows joined `@MC-0025644` as `192.168.168.101/24`; ping to `192.168.168.1` succeeded.
- Outcome: Windows-native C# status fetch succeeded against `192.168.168.1:81` and wrote `.my/plan/m38-a9-camera/captures/phase-22-windows-status-connected-2026-05-28.json`.
- Outcome: Hardware-gated `A9WindowsNativeVue990RealTests` passed with `A9_E2E=1`, `A9_WINDOWS_PPCS_E2E=1`, and `A9_CAMERA_IP=192.168.168.1`.
- Outcome: Added a Windows-only C# direct HTTP media probe and shared C# MJPEG AVI writer for future Windows media artifacts.
- Outcome: Direct Windows HTTP media probing tested common and APK-derived snapshot/video/livestream paths; only `get_status.cgi` returned `HTTP 200`, all media candidates returned `404`, and no image/video bytes were downloaded.
- Outcome: Added and tested a managed C# Vue990 CGI-over-PPCS request builder for the known live command `livestream.cgi?streamid=10&substream=0&`.
- Outcome: Created Phase 23 for the next attempt: managed PPCS connect/login/live-open and a bounded raw channel `1` byte dump from Windows.

## 2026-05-29 09:32:33 +02:00

- Outcome: Windows was connected to `@MC-0025644` as `192.168.168.100/24` while wired Ethernet remained available.
- Outcome: Added managed C# DAS/server-parameter analysis with `A9Vue990DasServerParameter` and `BodyCam.A9Probe vue990-das`.
- Outcome: Live status still succeeded against `192.168.168.1:81`; the camera reported charging state and battery rose to `25` after USB power was connected.
- Outcome: Saved DAS analysis to `.my/plan/m38-a9-camera/captures/phase-23-das-analysis-2026-05-29.json`.
- Outcome: The DAS payload is 96 opaque bytes with known magic `8ED76A3380D998ECDA94D6D805A36877`; no plaintext endpoint or common-port IPv4 candidate was found.
- Outcome: Windows-native image/video capture is still blocked on managed PPCS/XQP2P/HLP2P transport negotiation, not on HTTP media endpoints or the laptop firewall.

## 2026-05-29 09:45:00 +02:00

- Outcome: Added `A9Vue990PpcsTransportProbeClient` and `BodyCam.A9Probe vue990-ppcs-transport` for bounded Windows candidate transport fingerprinting.
- Outcome: Live transport fingerprinting saved `.my/plan/m38-a9-camera/captures/phase-23/windows-ppcs-transport-2026-05-29.json`.
- Outcome: No direct local transport signal was found: TCP `65527`, `20190`, `32108`, `15203`, and `3478` timed out; UDP `65531`, `32108`, and `20190` returned no target response.
- Outcome: Added and ran the hardware-gated Windows transport RealTest; `A9WindowsNativeVue990RealTests` passed 2/2 with `A9_E2E=1`, `A9_WINDOWS_PPCS_E2E=1`, and `A9_CAMERA_IP=192.168.168.1`.
- Outcome: The next phase should focus on `DAS-...` decryption and native `ConnectByServer` parsing rather than broader direct endpoint probing.

## 2026-05-29 12:13:04 +02:00

- Outcome: Reconnected Windows to `@MC-0025644`; status on `192.168.168.1:81` remained reachable and reported battery `100`.
- Outcome: Implemented and verified managed C# DAS decode. The live `DAS-...` value decrypts to relay hosts `47.98.128.117`, `120.78.3.33`, and `47.109.80.221`.
- Outcome: Re-ran direct Windows HTTP media probing; all image/video/livestream candidates still returned `404`, while `get_status.cgi` returned `200`.
- Outcome: Re-ran Windows transport probing with decoded relay hosts. Local camera PPCS/HLP2P ports still produced no direct signal, but TCP `65527` opened on all decoded relay hosts.
- Outcome: Windows Firewall remains a possible UDP/listener caveat because the camera AP is `Public`, but it is not the primary blocker: Windows can reach status, gets real HTTP media `404`s, and can open relay TCP sockets.
- Outcome: Used the Android phone as a control probe while it was on `@MC-0025644`; it captured a fresh `640x480` JPEG and six live frame JPEGs from the vendor player path.
- Outcome: Pulled the Android control image/frames to `.my/plan/m38-a9-camera/captures/phase-24-android-control-2026-05-29/`.
- Outcome: Added a Windows C# `BodyCam.A9Probe mjpeg-avi` command and assembled the six pulled frames into `a9-video-2026-05-29-120833-mjpeg.avi`; verified `156106` bytes and RIFF/AVI header.
- Outcome: Created Phase 25 for the next Windows-native step: implement the managed HLP2P relay hello/session-open packet sequence against decoded TCP `65527` relays.

## 2026-05-29 12:28:10 +02:00

- Outcome: Stabilized the Android C# capture path so the report completes after image/frame capture instead of stalling during Android-side AVI assembly.
- Outcome: Phone remained connected to `@MC-0025644` as `192.168.168.101/24`; PPCS connect/login/live-open and player callbacks worked.
- Outcome: Downloaded a fresh `640x480` still image and six `640x480` frame JPEGs to `.my/plan/m38-a9-camera/captures/phase-26-android-csharp-capture-2026-05-29-122420/`.
- Outcome: Assembled the pulled frame sequence on Windows with C# into `a9-video-2026-05-29-122420-mjpeg.avi`; verified `153334` bytes, `RIFF ... AVI`, SHA-256 `00777EBE8E2CE141ECF6D59DBCA3A328B382D68C4E67363E17979DC828DAF64C`.
- Outcome: Hardware-gated `A9PhonePpcsPlayer_CapturesShortVideoArtifact` passed with the new contract: Android C# downloads frame images, Windows C# assembles the AVI.
- Outcome: Created Phase 26 to document Android C# capture stabilization before resuming the Windows-native Phase 25 relay-hello work.

## 2026-05-29 13:13:01 +02:00

- Outcome: Added `BodyCam.A9Probe vue990-relay-hello` and corrected native-derived empty-header candidates for TCP hello, relay hello, and server request.
- Outcome: Ran the relay probe against decoded TCP `65527` hosts. All relay sockets opened and accepted bytes, but no candidate returned response bytes.
- Outcome: Added `BodyCam.A9Probe vue990-android-capture`, a Windows C# command that drives the Android C# probe over ADB and packages artifacts locally.
- Outcome: The new Windows C# Android-capture command succeeded and downloaded a fresh `640x480` JPEG plus six live frame JPEGs to `.my/plan/m38-a9-camera/captures/phase-27-android-csharp-orchestrated-2026-05-29-131301/`.
- Outcome: Windows C# assembled the pulled frames into `a9-video-2026-05-29-131301-mjpeg.avi`; verified `150612` bytes, `RIFF ... AVI`, SHA-256 `D21CFBD55E001F6086D1C55498BDE66EFF9EED6E424DE9C1F80E7500E2680FC7`.
- Outcome: Created Phase 27 to document the working C#-controlled Android capture path and the remaining pure Windows blocker.

## 2026-05-29 13:20:28 +02:00

- Outcome: Added an Android C# native packet oracle mode for exported Vue990 PPCS packet creator functions.
- Outcome: The oracle confirmed native `create_Hello`, `create_RlyHello`, and `create_SvrReq` return `4` and write `F1000000`, `F1700000`, and `F2100000`.
- Outcome: Added and ran a loopback socket oracle for native `TCPSend_Hello`; it returned `0` and first emitted bytes `000468007351673D7C5897F9`.
- Outcome: Added the native `TCPSend_Hello` payload as a managed C# relay candidate and tested it against decoded TCP `65527` relays; all sockets opened, but no response bytes arrived.
- Outcome: Saved the oracle report under `.my/plan/m38-a9-camera/captures/phase-28-native-packet-oracle/`.
- Outcome: Created Phase 28 to document that the managed empty headers and native TCP-send hello match native output; the remaining pure Windows blocker is the larger second-stage session payload.

## 2026-05-29 13:40:00 +02:00

- Outcome: Added managed DAS re-encoding and verified the current live decoded DAS payload re-encrypts to the original `DAS-...` value.
- Outcome: Created Phase 29 for the fake DAS/local relay oracle path.
- Outcome: Created Phase 30 for native second-stage packet helper mapping.
- Outcome: Created Phase 31 for the final cross-platform C#-only PPCS library target.

## 2026-05-29 14:05:00 +02:00

- Outcome: Implemented Android C# `server_override` and `fake_relay` probe modes, plus Windows C# `vue990-das --replace-relays ... --server-only`.
- Outcome: Tried three fake-DAS relay rewrites: short loopback, same-length loopback, and phone Wi-Fi IP. All started the local listener but recorded `fake relay: connections=0`.
- Outcome: Phase 29 conclusion: decoded DAS relay hosts are likely protected by an additional checksum/token, so host replacement alone cannot force the native stack to connect to our local listener.
- Outcome: Added native `Write_TCPRlyReq`, `Write_TCPRSLgn`, `TCPSend_TCPRlyReq`, and `TCPSend_TCPRSLgn` oracle calls in the Android C# probe.
- Outcome: Captured native-generated second-stage TCP frames: 64-byte `TCPRlyReq` and 68-byte `TCPRSLgn`, repeated once with identical output.
- Outcome: Promoted those oracle frames into managed C# `A9Vue990P2pPacketBuilder` and default Windows relay candidates.
- Outcome: Retested decoded TCP `65527` relays with native-generated second-stage frames and hello+second-stage sequences; all relay sockets opened but returned no bytes.
- Outcome: Created Phase 32 for the next unblocker: mapping dynamic fields inside `TCPRlyReq` / `TCPRSLgn` by varying native oracle arguments.
- Outcome: Ran the first Phase 32 write-variant oracle and saved `.my/plan/m38-a9-camera/captures/phase-32-write-variant-oracle-2026-05-29-141000/`.
- Outcome: The variant oracle mapped client id, VUID, `sockaddr_cs2`, and numeric-field byte movement in native `Write_TCPRlyReq` / `Write_TCPRSLgn`; the tested relay-token argument did not affect the write struct.
- Outcome: Focused A9/Vue990 unit tests passed: 13/13.

## 2026-05-29 15:12:30 +02:00

- Outcome: Added a shared managed C# PPCS packet layer with packet parsing, XOR1 discovery encoding/decoding, DRW/ACK helpers, and `55 AA 15 A8` video chunk reassembly.
- Outcome: Added Android managed-direct C# probing without `JNIApi` / `AppPlayerApi` and reran it while the phone was connected to `@MC-0025644` as `192.168.168.101/24`.
- Outcome: Android local C# probing again found only TCP `81` status, no direct HTTP JPEG/MJPEG/H264 payloads, and only UDP self-echoes; no C#-only image or video was downloaded.
- Outcome: Added a bounded managed C# relay/session-open fallback to the Android probe using decoded DAS relays and native-derived candidate frames.
- Outcome: Android decoded DAS relays correctly, but all 24 TCP `65527` relay attempts timed out before opening while the phone was on the camera Wi-Fi.
- Outcome: Phase 34 was created for the next step: run the shared C# relay/channel dump path from Windows, then replace fixed oracle frames with parameterized `TCPRlyReq` / `TCPRSLgn` builders.
- Outcome: Focused Vue990 unit tests passed 19/19 and the Android probe APK built successfully.

## 2026-05-29 15:33:32 +02:00

- Outcome: Fixed the Windows `vue990-relay-hello` CLI regression and added cached DAS/client-id/VUID options for relay testing without refetching status.
- Outcome: Ran the cached Windows relay probe with decoded DAS. Windows opened most TCP `65527` relay sockets, but no candidate returned response bytes.
- Outcome: Added shared C# control-channel builders for `ConnectUser`, `VideoResolution`, `StartVideo`, `StopVideo`, and `DeviceStatus`.
- Outcome: Corrected managed `DrwAck` to use ACK marker `0xD2`.
- Outcome: Added an Android C# classic PPPP stream attempt. The phone sent plain and XOR `LanSearch`, but the camera returned no remote `PunchPkt` / `P2pReady`, so no C#-only image or video was downloaded.
- Outcome: Ported the exported Vue990 proprietary P2P/TCP relay codec shape into managed C# using the native 256-byte table and derived key bytes.
- Outcome: Created Phase 35 for the Android managed stream attempt and Phase 36 for the relay-encryption/second-stage builder work.
- Outcome: Focused Vue990 unit tests passed 24/24 and the Android probe APK built successfully.

## 2026-05-29 15:56:33 +02:00

- Outcome: Added Android Wi-Fi process binding plus multicast/Wi-Fi locks to the managed-direct C# probe.
- Outcome: Expanded the Android local C# port matrix to include UDP `32108`, `20190`, `65529`, and `65531`, with multiple PPPP/HLP2P discovery/session variants.
- Outcome: Confirmed the Vue990 vendor app shows live video and owns UDP `65529` while streaming.
- Outcome: Force-stopped the vendor app and reran the managed C# direct probe; the phone still saw only UDP self-echo packets and no remote session response.
- Outcome: No C#-only image or video was downloaded; Phase 37 documents that the blocker is now the Vue990/OKSMART session opener, not Android Wi-Fi routing or Windows Firewall.

## 2026-05-29 16:10:29 +02:00

- Outcome: Ported the Vue990 native TCP relay frame shape to managed C#: `len:BE16 68 00 seed0 seed1 crc0 crc1 ciphertext`.
- Outcome: Added managed C# builders for `TCPRlyReq` and `TCPRSLgn` and verified both against stable native Phase 32 vectors.
- Outcome: Wired generated managed TCP relay packets into the Windows/Android shared relay probe before the older fixed oracle replays.
- Outcome: Focused Vue990 tests passed `29/29` and the Android probe APK built successfully.
- Outcome: Ran the generated managed relay packets against decoded TCP `65527` relay hosts from Windows; sockets opened and accepted bytes, but no response bytes arrived.
- Outcome: Phase 38 documents that C# now reproduces the native relay packet bytes, but image/video retrieval is still blocked by missing live session context or a different local transport.

## 2026-05-29 16:35:00 +02:00

- Outcome: Clarified the next target: Android is not a relay; it is the first runtime for C#-only Vue990 streaming, with Windows as the later portability target.
- Outcome: Added Phase 39, `Android C# UDP Session Opener`, to focus on the native UDP session-open path seen during successful native-backed streaming.
- Outcome: Current evidence points to UDP `65529` plus dynamic UDP ports during the working stream, with no TCP media/session socket observed in the ADB sample.
- Outcome: C#-only image/video retrieval is still not finished; the next task is to reverse and port the native UDP opener rather than repeat HTTP URL or fixed UDP port guessing.

## 2026-05-29 16:51:42 +02:00

- Outcome: Added `csharp-only-vue990-roadmap.md` as the controlling roadmap for the pure C# Vue990 image/video goal.
- Outcome: The roadmap makes Android the protocol proving ground, not a relay, and defers Windows-only work until the C# session opener succeeds on Android.
- Outcome: Repeated broad HTTP, RTSP, generic UDP, fixed relay replay, and socket-snapshot-only attempts are now explicitly blocked unless new evidence changes the hypothesis.
- Outcome: The next useful branch is a native channel/session oracle, followed by a managed C# session opener built from the oracle output.

## 2026-05-29 17:05:00 +02:00

- Outcome: Added Phase 40 and implemented a native channel oracle using native `client_read` after the working live CGI command.
- Outcome: The oracle pulled raw channel bytes from Android to Windows and showed the live media payload is JPEG frames in a `55 AA 15 A8` Vue990 envelope.
- Outcome: Added C# `A9Vue990ChannelMediaExtractor` and focused tests; Vue990 tests passed `36/36`.
- Outcome: Extracted `54` JPEG frames from the channel bytes and saved first still frame `channel-frame-000.jpg`, `640x480`, `8371` bytes.
- Outcome: Assembled the extracted frames into `native-channel-oracle-mjpeg.avi`, `452860` bytes, SHA-256 `5EF54A34006270658E48B8D58279C5F5C57C6AACC80849CD8D9F6496E5E11827`.
- Outcome: This still uses native transport/session setup, but `AppPlayerApi` is no longer required to save image/video once channel bytes are available.

## 2026-05-29 17:12:00 +02:00

- Outcome: Ran Phase 41 first managed-live-CGI oracle using native `connect`/`login`/`client_read` but C# `A9Vue990CgiCommandBuilder` through `JNIApi.write`.
- Outcome: The C# payload was accepted as `69` bytes and returned `var result=0`, but no stream-start callback or channel bytes followed.
- Outcome: No image/video was produced in `.my/plan/m38-a9-camera/captures/phase-41-managed-live-cgi-2026-05-29-170844/`.
- Outcome: The remaining immediate blocker is matching native `writeCgi` framing, not decoding, Windows Firewall, or generic endpoint discovery.

## 2026-05-29 17:28:00 +02:00

- Outcome: Tested the bounded Phase 41 managed-live-CGI variants; raw CGI, raw GET, no-leading-slash GET, and the old `D1` wrapper did not start channel media.
- Outcome: Native disassembly showed `writeCgi` builds a credentialed CGI command body and sends an 8-byte command header plus body on channel `0`.
- Outcome: Added C# command-frame builders for the native live-stream CGI body and header.
- Outcome: `command-cgi-split` succeeded: C# sent header `01 0A 00 00 61 00 00 00` and body `GET /livestream.cgi?streamid=10&substream=0&loginuse=admin&loginpas=888888&user=admin&pwd=888888&`.
- Outcome: Saved a real still image from the resulting channel bytes: `640x480`, `9247` bytes, SHA-256 `6CBF309650B4EAEC9B6712D8F679C7DA83CCDE398C5B711DC56AB757ACC90188`.
- Outcome: Extracted `73` JPEG frames and assembled `native-channel-oracle-mjpeg.avi`, `677120` bytes, SHA-256 `64A5607A0FEDFD0FC3510D2CBFED255192CB8C89C1867182ADFE1F9A502D8257`.
- Outcome: The next blocker is the native session carrier (`connect`/`login`/raw read/write), not live-CGI framing or media decoding.

## 2026-05-29 18:02:12 +02:00

- Outcome: Started Phase 42 and mapped the native wrapper layer: `login` stores credentials then sends `get_status.cgi?name=admin&` through the same CGI command path.
- Outcome: Mapped native raw read/write/check-buffer to the active session interface at `client + 0x80` with session handle `client + 0x208`.
- Outcome: Identified `connectType=0x3F` as the V4/HLP2P path with subtype `1`; the next blocker is now the HLP2P session carrier handshake.
- Outcome: Added tested C# helpers for the native login-status CGI body and native 8-byte command header parsing.
- Outcome: Mapped native DRW packet byte order: HLP2P outer packet headers use big-endian length fields, while DRW command indexes and ACK index entries are native little-endian.
- Outcome: Corrected the managed C# DRW/DRW ACK packet helpers and kept the focused Vue990 tests plus Android probe build green.

## 2026-05-29 21:10:00 +02:00

- Outcome: Continued Phase 42 using Android phone Wi-Fi only; laptop Wi-Fi was not used for camera access.
- Outcome: Added Android managed-direct progress logging, explicit ephemeral UDP socket binding, and fast-only HTTP status probing so the Android probe reaches the session-carrier attempts reliably.
- Outcome: Added and granted Android `NEARBY_WIFI_DEVICES`; the previous UDP `Permission denied` failures disappeared.
- Outcome: Android phone was connected to `@MC-0025644` as `192.168.168.100/24`; the probe saw TCP `81` status and no plain HTTP media.
- Outcome: Managed UDP probes sent on camera Wi-Fi and received only self-echoes from `192.168.168.100` on UDP `32108` / `65529`; no remote camera `PunchPkt` / `P2pReady` response arrived.
- Outcome: Managed relay fallback decoded DAS and attempted `24` TCP `65527` candidates; all timed out with `0` response bytes.
- Outcome: No C#-only image or video was downloaded in `.my/plan/m38-a9-camera/captures/phase-42-android-wifi-permission-2026-05-29-210712/`.

## 2026-05-29 21:25:00 +02:00

- Outcome: Created Phase 43 to avoid repeating broad probes and focus on native `ConnectByServer` / `_p2p_connect_check_svr`.
- Outcome: Confirmed from disassembly that native HLP2P validates the full decrypted DAS token set before session setup; relay-host replacement alone is therefore not enough.
- Outcome: Next Android-only work is native HLP2P debug capture and managed DAS connect descriptor parsing. Laptop Wi-Fi remains out of scope.

## 2026-05-29 21:26:31 +02:00

- Outcome: Captured native HLP2P debug logs from an Android-only native-backed run; laptop Wi-Fi was not used.
- Outcome: Native `ConnectByServer` succeeded through local UDP LAN-hole flow (`_se_lan_hole`, `dev lan hole`, `dev lan hole ack`) and then kept alive to `192.168.168.1:53674`.
- Outcome: Saved fresh native-backed artifacts in `.my/plan/m38-a9-camera/captures/phase-43-native-hlp2p-log-2026-05-29-211949/`: still SHA-256 `BD89669D1244913B888E5AF2EF5CC376CEF9EC30C10A8D9D4D9814D2950E4369`, MJPEG AVI SHA-256 `5B0D8D1550D332D8126EC21144BF84D554EA851F4DFB6CEE808EBB8379A84FBF`.
- Outcome: Added managed C# DAS connect descriptor parsing that preserves token bytes, including the opaque binary token and selector token.
- Outcome: Focused Vue990 tests passed `42/42`; Android phone probe build passed for `net10.0-android`.
- Outcome: Created Phase 44 for the next gate: implement a focused managed C# LAN-hole session opener on Android phone Wi-Fi.

## 2026-05-29 21:36:53 +02:00

- Outcome: Added Android native oracle calls for `create_LstReq`, `create_PunchPkt`, `create_P2pRdy`, and `create_P2pReq`.
- Outcome: Ran the oracle over USB/ADB only; phone network state was `wlan0: 192.168.168.100/24`, and laptop Wi-Fi was not used.
- Outcome: Saved report in `.my/plan/m38-a9-camera/captures/phase-44-native-hlp2p-packet-oracle-2026-05-29-213653/`.
- Outcome: Native helper previews show four zero bytes between the HLP2P header and P2P id, but helper return lengths may slice through the scratch buffer; next mapping target is the send wrapper (`Send_Pkt_ListReq` / `Send_Pkt_P2PReq`) before changing managed packet bytes.
- Outcome: Android phone probe build passed after the oracle expansion.
- Outcome: Static follow-up mapped `pack_ClntPkt`: the final native send shape repacks to `header + P2P id` or `header + P2P id + reverse address`, so the existing managed basic HLP2P packet builder shape is correct.

## 2026-05-29 22:14:00 +02:00

- Outcome: Continued Phase 44 as local-only development; no commit or push was performed.
- Outcome: Added `A9Vue990ConnectByServerState` to preserve live DAS tokens, client id, VUID, local endpoint, and native structured P2P IDs in C#.
- Outcome: Added focused unit coverage for current camera DAS state and native-shaped basic HLP2P opener packets; focused Vue990 tests passed `46/46`.
- Outcome: Added Android `managed_lan_hole` autorun mode and Windows/ADB support for launching it.
- Outcome: Android phone was connected to `@MC-0025644` as `192.168.168.100/24`; laptop Wi-Fi was not used.
- Outcome: Ran the focused managed LAN-hole probe and saved the report in `.my/plan/m38-a9-camera/captures/phase-44-managed-lan-hole-local-2026-05-29-221252/`.
- Outcome: Fixed UDP `65529` sent confirmed C# basic HLP2P packets and received only self-echo packets from `192.168.168.100`; the ephemeral socket received no responses.
- Outcome: No non-self camera response was captured, so the next work must map the real `_se_lan_hole` session-engine packet instead of repeating the basic helper packet burst.

## 2026-05-29 22:55:00 +02:00

- Outcome: Continued local-only development; no commit or push was performed.
- Outcome: Created Phase 45 for the native LAN-hole session-engine map so the next work does not loop back to broad probing.
- Outcome: Static mapping showed `_clientSessionToSetup` sends a narrower setup subset: client-id `ListReq`, client-id `P2PReq4`, and `LanSearch`.
- Outcome: Mapped native alive helpers as header-only packets `F1E00000` and `F1E10000`.
- Outcome: Added C# builders/tests for the native setup subset and alive headers.
- Outcome: Updated Android managed LAN-hole mode to send the native setup subset first and include decoded DAS relay hosts as candidate targets.

## 2026-05-30 00:03:00 +02:00

- Outcome: Managed C# compact HLP2P direct probe reached the camera LAN-hole response and ready packet on Android phone Wi-Fi.
- Outcome: Fixed compact alive handling enough to receive the 830-byte post-hole command response from `192.168.168.1`.
- Outcome: No image/video was saved in this attempt; the missing piece was the native-paced post-hole control order.

## 2026-05-30 00:15:00 +02:00

- Outcome: Replayed the native "ACK 830 then resend control[3]" step from C#.
- Outcome: The camera ACKed the repeated control packet but still did not send the 62-byte response or media header.
- Outcome: This confirmed the full pacing around control packets mattered, not only the 830 ACK.

## 2026-05-30 00:18:00 +02:00

- Outcome: Changed the C# direct replay to the native-paced order: control `0`, control `1`, wait, control `2`, control `3`, wait, repeat control `1`, ACK the 830-byte response, then repeat control `3`.
- Outcome: Android C# received the missing 62-byte response, then the `55 AA 15 A8` media header and JPEG fragments.
- Outcome: Saved C# runtime still image `managed-direct-still.jpg`, `640x480`, `9487` bytes, SHA-256 `9C124F13027538D726D2E72A83F06D5B03B08573FDD5A53B79DFD685B6A0A951`.
- Outcome: Saved C# runtime MJPEG AVI `managed-direct-video-mjpeg.avi`, `12` frames, `640x480`, `113896` bytes, SHA-256 `F08D052541F4A902E1F278509A9D09E4D73F1E58DC01E902C575504EABD512FB`.
- Outcome: Wrote Phase 47 for the Android C# capture success and documented the remaining caveat: post-hole control payloads are still native-observed encrypted vectors that need C# derivation.

## 2026-05-30 00:40:00 +02:00

- Outcome: Laptop Wi-Fi was connected directly to `@MC-0025644` as `192.168.168.101/24`; the camera gateway `192.168.168.1` was reachable and `get_status.cgi` returned `BK0025644WBPD`, `BKGD00000100FMQLN`, and `BK7252N`.
- Outcome: Added the Windows `vue990-direct-capture` command and shared C# `A9Vue990DirectCaptureClient` for the Android-proven compact HLP2P/direct capture sequence.
- Outcome: First Windows direct run reached LAN-hole, ready, post-hole packets, and saved about `1.8 MB` of raw channel bytes, but did not extract frames during the live receive loop.
- Outcome: Added fallback extraction from `managed-hlp2p-direct-channel.bin`; the second Windows run saved a real still image and MJPEG AVI.
- Outcome: Windows C# still image saved at `.my/plan/m38-a9-camera/captures/phase-48-windows-direct-2026-05-30-004023/managed-direct-still.jpg`, `640x480`, `9123` bytes, SHA-256 `52444D62CF8E3F2520F1436F57E02E26FCF3D26323C6FFD8739E5C6AE0E6CE30`.
- Outcome: Windows C# video saved at `.my/plan/m38-a9-camera/captures/phase-48-windows-direct-2026-05-30-004023/managed-direct-video-mjpeg.avi`, `12` frames, `640x480`, `110204` bytes, SHA-256 `8C07FC2095F84209C52B06A16BE80A972E8C67CE8C00D566BDB63A684D74FC87`.
- Outcome: Build passed for `tools/BodyCam.A9Probe`, and focused HLP2P direct tests passed `6/6`.
- Outcome: Phase 48 now marks Windows C# image/video capture as succeeded; the remaining caveat is that the post-hole control payloads are still static native-observed encrypted vectors rather than generated in C#.

## 2026-05-30 00:55:00 +02:00

- Outcome: Created Phase 49, `Final C# Hardening`, to track the last work after Android and Windows C# capture success.
- Outcome: Phase 49 owns control derivation or scoped-vector documentation, shared capture API cleanup, repeatability proof runs, and final report updates.

## 2026-05-30 01:04:00 +02:00

- Outcome: Added shared `A9Vue990PostHoleControlProvider` so the four static post-hole controls are named, scoped, defensive byte vectors instead of anonymous replay bytes.
- Outcome: Wired Windows direct capture and the Android phone probe to the shared post-hole provider; normal reports now say `post-hole control` instead of `replay control`.
- Outcome: Added focused post-hole provider tests; focused direct/post-hole tests passed `12/12`.
- Outcome: `tools/BodyCam.A9Probe` build passed, and `tools/BodyCam.A9PhoneProbe` Android build passed.
- Outcome: Fresh Phase 49 Windows proof run 1 saved `.my/plan/m38-a9-camera/captures/phase-49-final-windows-direct-2026-05-30-010401/managed-direct-still.jpg`, `640x480`, `8104` bytes, SHA-256 `F36EF09D8BBFA5A8330D9BE54F46158E9AAB4B2C37E13F9CB632F39B632A498D`.
- Outcome: Fresh Phase 49 Windows proof run 1 saved `.my/plan/m38-a9-camera/captures/phase-49-final-windows-direct-2026-05-30-010401/managed-direct-video-mjpeg.avi`, `12` frames, `640x480`, `97868` bytes, SHA-256 `3E1EA8F16061840F039422C6C38C5F31F4F5179C020FC87BD5CAE97FFF83E80A`.
- Outcome: Fresh Phase 49 Windows proof run 2 saved `.my/plan/m38-a9-camera/captures/phase-49-final-windows-direct-2026-05-30-010441/managed-direct-still.jpg`, `640x480`, `8152` bytes, SHA-256 `5DF8B1778937805BE84EAA86ED6CC9802CE64209908F1AC36AD9BDFD848F5516`.
- Outcome: Fresh Phase 49 Windows proof run 2 saved `.my/plan/m38-a9-camera/captures/phase-49-final-windows-direct-2026-05-30-010441/managed-direct-video-mjpeg.avi`, `12` frames, `640x480`, `98312` bytes, SHA-256 `0D2825A46C5C8AA6D93FFBB60936A740A0D638C21C7E399D9B1C5435ED8D6BA2`.
- Outcome: Phase 49 is complete for the current camera: Windows C# capture is repeatable, and the remaining control derivation is now a future broader-compatibility task rather than a blocker for `@MC-0025644`.

## 2026-05-30 01:20:00 +02:00

- Outcome: Added `.my/plan/m38-a9-camera/vue990-csharp-capture-solution.md` as the durable solution document for the working Windows C# capture path.
- Outcome: Documented the run command, expected artifacts, solution flow, code map, regression tests, proof artifacts, troubleshooting, and future compatibility caveat.
- Outcome: Linked the solution document from the current status report and the C#-only Vue990 roadmap.

## 2026-05-30 01:30:00 +02:00

- Outcome: Added `.my/plan/m38-a9-camera/vue990-capture-journey-report.md` as a factual companion to the story document.
- Outcome: Captured the investigation ups and downs, failed paths, key turning points, Windows proof, current code surface, and remaining scoped-vector caveat.

## 2026-05-30 01:50:00 +02:00

- Outcome: Created Phase 50, `.my/plan/m38-a9-camera/phase-50-vue990-bodycam-provider.md`, for the final BodyCam app provider hookup.
- Outcome: Added standalone `Vue990CameraProvider` with provider id `vue990-camera`, leaving the classic iLnkP2P `A9CameraProvider` unchanged.
- Outcome: Added separate `Vue990CameraIp` settings, a Vue990 settings page/view model, DI registration, route registration, and an Add Devices card for `Add Vue990 Camera`.
- Outcome: Added focused provider/settings/AddDevices tests; `dotnet test src\BodyCam.Tests\BodyCam.Tests.csproj --filter "Vue990CameraProvider|Vue990CameraSettings|AddDevices"` passed `18/18`.
- Outcome: `dotnet build src\BodyCam\BodyCam.csproj -f net10.0-windows10.0.19041.0` passed, and `dotnet build tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj` passed.
- Outcome: `dotnet build src\BodyCam.RealTests\BodyCam.RealTests.csproj` could not run because restore hit an existing target-framework mismatch: RealTests requested `net10.0-ios26.2` while `BodyCam` supports `net10.0-ios26.5`.

## 2026-05-30 02:00:00 +02:00

- Outcome: Updated `.my/plan/m38-a9-camera/vue990-capture-story.md` to describe the probe building, installed test tools, native-code reverse parsing, and division of labor between hardware handling and protocol reconstruction.
- Outcome: Added `.my/plan/m38-a9-camera/vue990-capture-story-30-lines.md` as a compact 30-line version of the story.
