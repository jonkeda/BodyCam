# Phase 15 - Vue990 VStarcam/VeePai PPCS Harness

**Status:** In Progress - PPCS live-open and player metadata callbacks proven;
hardware-gated RealTest added, connected-phone pass pending

## Goal

Turn the confirmed Vue990 live-view evidence into a repeatable Android-side
diagnostic harness that can connect to `@MC-0025644` through the same
VStarcam/VeePai native stack and report stream metadata.

This phase exists because the camera does not expose a reusable local RTSP,
MJPEG, snapshot, or simple HTTP video endpoint. Vue990 can view live video, and
live logcat proves the working path is inside `libOKSMARTPPCS.so` plus
`libOKSMARTPLAY.so`.

## Evidence

- Camera AP SSID: `@MC-0025644`
- Camera/AP IP: `192.168.168.1`
- Confirmed open TCP port: `81`
- Confirmed status URL:
  `http://192.168.168.1:81/get_status.cgi?loginuse=admin&loginpas=888888`
- VUID / real device id: `BK0025644WBPD`
- Vue990 P2P client id seen in logs: `BKGD00000100FMQLN`
- Alias/chip hint: `BK7252N`
- Firmware/app version: `21.120.101.34`
- Vue990 package: `com.langxing.zhilianjiumu`
- Important native libraries:
  - `libOKSMARTPPCS.so`
  - `libOKSMARTPLAY.so`
  - `libOKSMARTJIAMI.so`
- Live logcat confirmed:
  - `P2PClient.c` created a client for `BKGD00000100FMQLN`
  - connect used `connectType=0x3F`
  - the camera status `server` field supplied a `DAS-...` server parameter
  - `app_source_live` started and read frame headers
  - frame metadata had `head->type:3` and payload lengths around 9.8-10.2 KB
- Local JADX `1.5.5` was installed under
  `%LOCALAPPDATA%\CodexTools\jadx\v1.5.5\` and used to recover the Flutter
  plugin JNI call shape without changing system-wide tooling.

## What We Learned

- Vue990 does not use a direct RTSP, MJPEG, snapshot, or HTTP video endpoint on
  this camera. Direct probing still only finds TCP `81` and `get_status.cgi`.
- The working viewer path is VStarcam/VeePai native P2P through
  `libOKSMARTPPCS.so`, with a Vue990/player layer above it.
- The key values for `@MC-0025644` are:
  - client id: `BKGD00000100FMQLN`
  - VUID / real device id: `BK0025644WBPD`
  - connect type: `0x3F`
  - P2P type: `1`
  - server parameter: the `DAS-...` value returned by `get_status.cgi`
- The native client lifecycle matters. Vue990 calls `JNIApi.retain(...)` before
  operations such as connect/login/check and balances it through the native
  release callback on the Android main thread. Calling `connect` without this
  retain/release pattern can crash inside the native library.
- The phone harness can now initialize the native stack, create a client, set
  VUID, connect, login, check mode, receive command/control metadata, and
  disconnect/destroy cleanly.
- The harness can also load `libOKSMARTPLAY.so`, initialize
  `com.veepai.AppPlayerApi`, create a player with a dummy `Surface`, set the
  source to the PPCS client pointer, and start/stop/destroy the player without
  crashing.
- The missing live-open step is a CGI-over-PPCS call after login:
  `JNIApi.writeCgi(clientPtr, "livestream.cgi?streamid=10&substream=0&", 1)`.
  The trailing `&` matters because the native library appends the login/user
  parameters itself.
- After that live-open command, channel `1` buffers stream bytes and the VeePai
  player emits metadata callbacks. The repeat run reported
  `checkBuffer(channel=1)=[0,0,15064]`, player callback counters above 30, and
  `app_player_draw_info width=640 height=480`.
- The harness still records metadata only. It does not save screenshots,
  video/image payloads, or rebroadcast the feed.

## Scope

1. Recover the Java/JNI method signatures and likely call order for
   `com.vstarcam.JNIApi`.
2. Create or extend an Android diagnostic harness that loads the Vue990 native
   libraries from the app package.
3. Add a minimal `com.vstarcam.JNIApi` wrapper matching the recovered exports.
4. Try status-safe native calls first: init/create, `clientSetVuid`, connect,
   login/check mode, buffer checks, and disconnect/destroy.
5. Capture only connection and stream metadata by default: return codes, state
   transitions, frame counts, frame types, byte lengths, and timestamps.
6. Save harness logs under `.my/plan/m38-a9-camera/captures/`.
7. Promote the successful behavior into hardware-gated RealTests, or document
   the exact blocker if the native stack cannot be reused outside Vue990.

## Non-Goals

- Do not bridge or rebroadcast the camera feed.
- Do not store screenshots or image/video payloads by default.
- Do not mutate camera settings, Wi-Fi configuration, credentials, IR, reboot,
  firmware, or SD-card state.
- Do not require root access.
- Do not extract user cloud credentials or private app account data.

## Implementation Checklist

- [x] Create this Phase 15 plan doc.
- [x] Recover JNI method signatures and call order from the APK, DEX, symbols,
      and live logcat evidence.
- [x] Decide whether to extend `tools/BodyCam.A9PhoneProbe` or create a focused
      `tools/BodyCam.A9PpcsProbe` app.
- [x] Copy only the required native libraries into the probe app and verify they
      load on the Samsung phone.
- [x] Add the `com.vstarcam.JNIApi` native wrapper.
- [x] Add a guarded connection attempt using the known VUID/client/server
      values from the status endpoint and live logs.
- [x] Add metadata-only PPCS command/control observation.
- [x] Add a metadata-only VeePai player API wrapper and first player/source/start
      attempt.
- [x] Identify the missing Vue990 live/open command or player source option that
      makes the player receive frames.
- [x] Save each run as a timestamped capture artifact.
- [x] Add hardware-gated RealTests once a repeatable connect or frame-metadata
      path exists.
- [x] Update the final realtests report with the Phase 15 result.

## Player/Frame Metadata Plan

1. Add a local `com.veepai.AppPlayerApi` Java wrapper with the recovered native
   signatures from Vue990.
2. Reuse the existing phone probe after PPCS login and create a VeePai player
   with a dummy Android `Surface`; the player library requires a render target
   even when the probe records metadata only.
3. Set the source as `AppPlayerLiveSource` using the proven PPCS client pointer.
4. Start the player and record only return codes and callbacks:
   `app_player_head_info`, `app_player_progress`, `app_player_gps_info`, and
   `app_player_draw_info`.
5. Wait a short bounded window for player/native log metadata, then stop and
   destroy the player.
6. Save the report and logcat under captures, with no screenshots, no feed
   bridge, and no stored visual payloads.

## Phase 15 Run Outcomes

Artifacts:

- `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-success-2026-05-28-185345.txt`
- `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-success-logcat-2026-05-28-185345.txt`
- `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-2026-05-28-201609.txt`
- `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-logcat-2026-05-28-201609.txt`
- `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-livecgi-2026-05-28-202309.txt`
- `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-livecgi-logcat-2026-05-28-202309.txt`
- `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-livecgi-fixed-2026-05-28-202924.txt`
- `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-player-livecgi-fixed-logcat-2026-05-28-202924.txt`
- Earlier crash/fix captures:
  - `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-2026-05-28-184647.txt`
  - `.my/plan/m38-a9-camera/captures/a9-phone-probe-ppcs-retain-2026-05-28-185050.txt`

Native result:

- `JNIApi.init=true`
- `JNIApi.create` returned a non-zero native pointer.
- `JNIApi.clientSetVuid=true`
- `JNIApi.connect=3` with `connectType=0x3F`, while native logs show the
  connection session opening.
- `JNIApi.login=true`
- `JNIApi.checkMode=[1,1]`
- `commandListener` received a type `24577`, length `788` control payload.
- `JNIApi.writeCgi(channel=1)` with
  `livestream.cgi?streamid=10&substream=0&` returned `true`.
- `commandListener` received a type `24631`, length `33` live-open ack payload.
- `JNIApi.checkBuffer(channel=1)` returned `[0,0,15064]` after live-open on
  the repeat run, proving the camera stream was buffered before player
  consumption.
- `JNIApi.disconnect=true`
- `JNIApi.destroy=done`

Player API attempt:

- `libOKSMARTPLAY.so` loaded in the probe app.
- `AppPlayerApi.init` and `setProgressCallback` completed.
- `AppPlayerApi.createPlayer` returned a non-zero native pointer.
- `AppPlayerApi.checkPlayerSource` returned `false` for the live source.
- `AppPlayerApi.setPlayerSource` returned `true` when passed the proven PPCS
  client pointer.
- First attempt without live-open: `AppPlayerApi.start=true`, but no
  `app_player_progress`, `app_player_head_info`, `app_player_draw_info`, or
  `app_player_gps_info` callbacks fired.
- Live-open attempt: `AppPlayerApi.start=true` and all metadata callbacks
  fired. The repeat run reached `progress=33`, `head=32`, `draw=32`,
  `gps=32`.
- Repeated draw metadata included `app_player_draw_info width=640 height=480`
  with `drawType=0`.
- `app_player_head_info` reported `type=22 width=0 height=3`; this appears to
  be native/player header metadata rather than final decoded image dimensions.
- `checkBuffer(1)` returned `[0,0,0]` after the player had consumed the
  pre-opened channel buffer.
- `AppPlayerApi.stop=true` and `AppPlayerApi.destroy=true`.
- Logcat showed `app_source_live_loading ... done` in the harness. The current
  repeat run did not need to store frame payloads or screenshots to prove the
  metadata path.

The first PPCS attempt crashed at native `client_connect` because the harness did
not yet mimic Vue990's `retain`/`releaseListener` lifecycle. After adding that
pattern, the connect/login path worked. A second crash happened after the
successful native run because the C# JNI wrapper tried to delete a class
reference that Android/Mono treated as global; removing that cleanup made the
third run complete and persist the report. A later UI crash happened after the
successful live-CGI report because the app tried to render the full growing
report from the Android UI thread; the app now keeps a bounded on-screen log and
writes the full report to disk.

Hardware-gated RealTest:

- Added `src/BodyCam.RealTests/A9/A9PhonePpcsPlayerRealTests.cs`.
- The test is gated by both `A9_E2E=1` and `A9_PHONE_PPCS_E2E=1`.
- It installs the Android probe over ADB, verifies the phone is on the expected
  camera subnet, runs the PPCS-only autorun, saves report/logcat artifacts, and
  asserts on PPCS login, live `writeCgi`, non-zero channel buffer, 640x480 draw
  metadata, clean destroy, and no fatal Android crash.
- Verification passed in default skipped mode. An enabled pass was attempted
  after the successful manual run, but skipped because the phone had moved to
  `192.168.1.67/24` instead of the camera AP subnet.

## Acceptance Criteria

- The harness can be installed and run on the Samsung phone while connected to
  `@MC-0025644`.
- The harness records whether the VStarcam/VeePai native libraries load
  successfully outside Vue990.
- The harness records native return codes for create/connect/login/check calls.
- If streaming opens, the harness records frame metadata without saving visual
  content.
- If streaming does not open, the report names the missing signature, parameter,
  permission, dependency, or app-private behavior that blocked reuse.

## Risks And Blockers

- JNI method signatures may need DEX decompilation or disassembly, not just
  exported native names.
- The native libraries may depend on Vue990 package names, Flutter-side
  initialization, assets, or app-private files.
- PPCS login with `admin` / `888888` works through the native harness.
- The recovered live-open command is repeatable, but it should be promoted into
  a hardware-gated RealTest so future changes can prove the same metadata path
  without manual log inspection.
