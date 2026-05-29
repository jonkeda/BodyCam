# Phase 22 - Windows-Native C# Capture

**Status:** In Progress - Windows status works; direct HTTP media ruled out;
managed PPCS handshake pending

## Goal

Retrieve images and short videos from `@MC-0025644` directly from Windows using
C# code, without using the Android phone helper, ADB, Java JNI stubs, or the
Vue990 Android native libraries at runtime.

This is now the active end goal. Phase 16/21 proved that the camera can deliver
real visual data. Phase 22 moves that capability from:

`Windows -> ADB -> Android probe -> vendor JNI -> camera`

to:

`Windows C# -> camera/PPCS -> image/video artifact`

## Success Criteria

- Windows is connected to the camera AP, or has a usable route to the camera.
- A C# Windows process fetches the camera status from `192.168.168.1:81`.
- A managed C# client performs the VStarcam/PPCS connect/login path.
- The client sends the known live-open command:
  `livestream.cgi?streamid=10&substream=0&` on channel `1`.
- The client reads stream bytes without Android, Java, or vendor native
  libraries.
- The client saves one verified JPEG image.
- The client saves one bounded short-video artifact.
- Hardware-gated RealTests prove the Windows path with no Android device.

## Known Inputs

- Camera SSID/tag: `@MC-0025644`
- Camera/AP IP: `192.168.168.1`
- Status URL:
  `http://192.168.168.1:81/get_status.cgi?loginuse=admin&loginpas=888888`
- VUID / real device id: `BK0025644WBPD`
- PPCS client id: `BKGD00000100FMQLN`
- Alias/chip hint: `BK7252N`
- Login: `admin` / `888888`
- PPCS connect values used by Vue990:
  - `connectType=0x3F`
  - `p2pType=1`
  - `server` parameter from `get_status.cgi`, beginning with `DAS-...`
- Live-open channel: `1`
- Live-open CGI:
  `livestream.cgi?streamid=10&substream=0&`
- Proven output size through the VeePai player: `640x480`
- Proven still image path: vendor player screenshot, pulled as JPEG
- Proven video fallback: six JPEG frames packaged as C# MJPEG AVI

## Boundary

Allowed:

- Direct Windows network probes.
- Managed C# implementation of the PPCS/client protocol.
- Local image/video artifacts under `.my/plan/m38-a9-camera/captures/`.
- Hardware-gated RealTests with explicit opt-in environment variables.

Not allowed:

- Android phone helper as the runtime capture path.
- ADB as part of the Windows capture path.
- Java JNI stubs or Android-only generated bindings.
- Re-broadcasting, bridging, or continuous recording.
- Camera setting mutation, firmware changes, or SD-card/cloud account changes.

## Relationship To Phase 18

Phase 18 remains the protocol-replacement research phase: learn and implement
the VStarcam/PPCS connect/login/channel protocol in managed C#.

Phase 22 is the deliverable phase: turn that managed protocol into a Windows
capture tool/provider that retrieves image and video artifacts.

In practice, Phase 22 will pull implementation work from Phase 18 as soon as
each protocol boundary is understood.

## Work Plan

### Stage 1 - Evidence Inventory

Create a protocol evidence pack from existing artifacts:

- status responses with `server`, `deviceid`, `realdeviceid`;
- PPCS harness reports;
- filtered logcat from Vue990 and the Android probe;
- native strings/symbol notes from `libOKSMARTPPCS.so`;
- frame/capture artifacts from Phase 16/21.

Output:

- `ppcs-protocol-notes.md` with known constants, packet/field guesses, and
  evidence-backed unknowns.

### Stage 2 - Windows Status And Topology Probe

Add a Windows C# probe path that does not use Android:

- verify the PC is on `192.168.168.x` or can route to `192.168.168.1`;
- fetch and parse the Vue990-style status URL;
- extract `deviceid`, `realdeviceid`, `server`, alias, battery, and users;
- record network topology because relay/cloud behavior may require both camera
  AP access and internet access through a wired/mobile route.

Output:

- a Windows status artifact and RealTest preflight.

### Stage 3 - Managed PPCS Control Client

Implement the smallest managed client that reproduces a non-visual control
command:

- connect using the status `server` parameter;
- login with `admin` / `888888`;
- send a safe CGI command;
- receive the same kind of ack that the Android harness records through command
  callbacks.

Output:

- `A9Vue990PpcsClient` or equivalent C# client.
- Hardware-gated control RealTest:
  `A9_WINDOWS_PPCS_E2E=1`.

### Stage 4 - Managed Live Channel

Open channel `1` from Windows:

- send `livestream.cgi?streamid=10&substream=0&`;
- read a bounded stream byte sample;
- identify frame boundaries and codec/container bytes.

Output:

- raw stream sample artifact, only when explicit capture gates are set.
- protocol notes updated with actual channel framing.

### Stage 5 - Windows Image Capture

Extract one still image:

- if channel data contains JPEG frames, extract SOI/EOI bounded JPEG bytes;
- if channel data is H.264/H.265, add or reuse a Windows decoder path and save
  one decoded frame;
- record dimensions, byte count, and SHA-256.

Output:

- `.jpg` under `.my/plan/m38-a9-camera/captures/phase-22/`.
- Hardware-gated image RealTest:
  `A9_WINDOWS_CAPTURE_E2E=1`.

### Stage 6 - Windows Video Capture

Save one bounded short-video artifact:

- prefer the native stream container if identified;
- otherwise package extracted JPEG frames as MJPEG AVI using the C# writer;
- keep duration short and explicit.

Output:

- `.avi` or codec-appropriate video artifact under
  `.my/plan/m38-a9-camera/captures/phase-22/`.
- Hardware-gated video RealTest:
  `A9_WINDOWS_VIDEO_E2E=1`.

## Implementation Checklist

- [x] Create this Phase 22 plan doc.
- [x] Create `ppcs-protocol-notes.md`.
- [x] Update roadmap and overview to make Phase 22 the active Windows-native
      target.
- [x] Add a Windows-only C# status/topology probe.
- [x] Add RealTest gates for Windows-native PPCS/capture tests.
- [x] Implement status parsing as shared C# code.
- [x] Run the Windows-only status/topology probe while Windows is actually on
      `192.168.168.x`.
- [x] Add a Windows-only C# direct HTTP media probe.
- [x] Rule out direct HTTP snapshot/video/livestream endpoints while Windows is
      connected.
- [x] Add managed C# CGI-over-PPCS request frame builder.
- [x] Add managed C# `DAS-...` parser/analyzer and save a live Windows
      analysis artifact.
- [x] Add a bounded Windows PPCS/HLP2P transport fingerprint probe and
      hardware-gated RealTest.
- [ ] Decode or parse the `DAS-...` server parameter enough to know where the
      managed PPCS client should connect.
- [ ] Implement managed PPCS connect handshake.
- [ ] Implement managed login.
- [ ] Implement managed CGI-over-PPCS command write.
- [ ] Implement command ack parsing.
- [ ] Implement managed channel `1` read.
- [ ] Identify stream codec/frame boundaries.
- [ ] Save one Windows-captured JPEG.
- [ ] Save one Windows-captured short-video artifact.
- [ ] Update `realtests-log.md` with high-level outcomes only.
- [ ] Update the dated report with Windows-native results.

## RealTests

All tests skip by default.

- `A9_E2E=1`
- `A9_WINDOWS_PPCS_E2E=1`
- `A9_WINDOWS_CAPTURE_E2E=1`
- `A9_WINDOWS_VIDEO_E2E=1`
- `A9_CAMERA_IP=192.168.168.1`

Control proof:

- No Android device is required.
- No Android package is installed or launched.
- Windows fetches status and completes managed PPCS login.

Image proof:

- Windows opens live channel `1`.
- Windows saves one JPEG and verifies marker, dimensions, bytes, and SHA-256.

Video proof:

- Windows saves one bounded video artifact and verifies container/header,
  dimensions/frame count or duration, bytes, and SHA-256.

## Risks

- The `DAS-...` parameter may encode relay behavior that requires internet in
  addition to camera AP access.
- PPCS authentication may use crypto hidden in `libOKSMARTJIAMI.so` or
  `libOKSMARTPPCS.so`.
- The stream payload may not be JPEG; Windows may need a decoder phase.
- The Android vendor library may use private keepalive/timing behavior that is
  not obvious from logs.

## Current Result

Implemented:

- `A9Vue990StatusClient` in shared C#.
- `BodyCam.A9Probe vue990-status`.
- `A9Vue990HttpMediaProbeClient` in shared C#.
- `BodyCam.A9Probe vue990-http-media`.
- `A9MjpegAviWriter` in shared C# for future Windows MJPEG artifacts.
- `A9Vue990CgiCommandBuilder` for the known CGI-over-PPCS live-open request.
- `A9Vue990DasServerParameter` for managed DAS shape analysis.
- `BodyCam.A9Probe vue990-das`.
- `A9Vue990PpcsTransportProbeClient` for bounded candidate transport
  fingerprinting.
- `BodyCam.A9Probe vue990-ppcs-transport`.
- `A9WindowsNativeVue990RealTests` with `A9_WINDOWS_PPCS_E2E=1`.

Verification:

- `dotnet build tools/BodyCam.A9Probe` passed.
- The Windows-native status RealTest compiles and skips by default.
- With `A9_E2E=1`, `A9_WINDOWS_PPCS_E2E=1`, and Windows connected to
  `@MC-0025644`, the Windows-native status RealTest passed.
- `A9Vue990CgiCommandBuilderTests` passed, 2/2.
- `A9Vue990DasServerParameterTests` passed, 5/5.
- `A9WindowsNativeVue990RealTests` passed with the Windows PPCS gate enabled,
  2/2, including the new transport fingerprint RealTest.

Live status attempt:

- Command:
  `BodyCam.A9Probe vue990-status --host 192.168.168.1`
- Earlier outcome: Windows topology was captured, but status fetch timed out
  because Wi-Fi was software-off and Windows was on `192.168.1.81/24`, not
  `192.168.168.x`.
- Artifact:
  `.my/plan/m38-a9-camera/captures/phase-22-windows-status-2026-05-28.json`
- Connected outcome: Windows Wi-Fi joined `@MC-0025644` as
  `192.168.168.101/24`, status returned `HTTP 200`, and the parsed identity
  matched `deviceid=BKGD00000100FMQLN`, `realdeviceid=BK0025644WBPD`,
  `alias=BK7252N`, `battery=100`, `server=DAS-...`.
- Connected artifact:
  `.my/plan/m38-a9-camera/captures/phase-22-windows-status-connected-2026-05-28.json`

Direct HTTP media probe:

- Command:
  `BodyCam.A9Probe vue990-http-media --host 192.168.168.1`
- Outcome: 64 direct HTTP media candidates were tested from Windows. All
  snapshot/video/livestream candidates returned `HTTP 404`; only
  `get_status.cgi` returned `HTTP 200`; no JPEG frames or video bytes were
  captured.
- Variant follow-up: exact APK-style candidates such as
  `snapshot.cgi?res=1&`, `livestream.cgi?streamid=4/5/16/17`, `get_record.cgi`,
  and `get_params.cgi` also returned `HTTP 404`.
- Artifacts:
  `.my/plan/m38-a9-camera/captures/phase-22/windows-http-media-probe-2026-05-28.json`
  and
  `.my/plan/m38-a9-camera/captures/phase-22/windows-http-media-apk-variants-2026-05-28.json`

Next required step:

- Implement Phase 23: managed PPCS connect/login/live-open from Windows and
  save a bounded raw channel `1` byte sample.
- Do not start the image/video decoder until raw channel bytes exist.

## Stop Conditions

Stop and document if:

- the managed client needs server-side secrets not present in APK/status data;
- the connection is cloud-mediated and cannot be reproduced from Windows
  without credentials we do not have;
- stream payload cannot be identified after a bounded raw capture;
- a contained vendor adapter is safer than full protocol replacement for this
  hardware.

Live DAS attempt:

- Command:
  `dotnet .\tools\BodyCam.A9Probe\bin\Debug\net10.0-windows10.0.19041.0\BodyCam.A9Probe.dll vue990-das --host 192.168.168.1 --output .\.my\plan\m38-a9-camera\captures\phase-23-das-analysis-2026-05-29.json`
- Result: status succeeded, battery was charging (`isCharge=1`) and had risen
  to `25`.
- DAS result: 96-byte opaque payload, known magic
  `8ED76A3380D998ECDA94D6D805A36877`, no plaintext or common-port IPv4
  endpoint candidate.
- Impact: direct Windows status and DAS parsing are working C# code, but
  Windows image/video capture still depends on implementing the managed PPCS
  transport handshake.

Live transport fingerprint attempt:

- Command:
  `dotnet .\tools\BodyCam.A9Probe\bin\Debug\net10.0-windows10.0.19041.0\BodyCam.A9Probe.dll vue990-ppcs-transport --host 192.168.168.1 --timeout-ms 1200 --read-ms 750 --max-bytes 4096 --output .\.my\plan\m38-a9-camera\captures\phase-23\windows-ppcs-transport-2026-05-29.json`
- Result: status and DAS parsing succeeded; battery had risen to `46`.
- Transport result: no direct local signal. TCP `65527`, `20190`, `32108`,
  `15203`, and `3478` timed out. UDP `65531`, `32108`, and `20190` returned no
  target response for the bounded discovery payloads.
- RealTest result: `A9WindowsNativeVue990RealTests` passed 2/2 with
  `A9_E2E=1`, `A9_WINDOWS_PPCS_E2E=1`, and `A9_CAMERA_IP=192.168.168.1`.
- Impact: the next Windows-native attempt should focus on DAS decryption and
  `ConnectByServer` parsing rather than broader direct port scans.
