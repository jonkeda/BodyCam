# M38 - A9 Camera Roadmap

**Status:** Planned

## Purpose

M38 now covers more than one A9 camera family. This roadmap defines the order of
work so implementation follows hardware evidence instead of guessing a protocol.

Use this file to decide what to build next. Use the individual phase docs for
the detailed acceptance criteria.

## Recommended Order

### 1. Stabilize The Probe Loop

Start with [Phase 0 - A9 Hardware Probe CLI And RealTests](./phase-0-realtests.md).

Goal:

- Add `tools/BodyCam.A9Probe`.
- Let Codex ask the user to power on one camera.
- Probe known A9 paths with short timeouts.
- Print readable and JSON diagnostics.
- Keep RealTests as the repeatable, hardware-gated follow-up.

Outcome:

- We know which camera/protocol variant is physically present.
- We have a saved diagnostic artifact for later implementation.

### 2. Add Adaptive Discovery

Then implement [Phase 5 - A9 Discovery](./phase-5-discovery.md).

Probe order:

1. Direct RTSP
2. Direct HTTP MJPEG
3. V720/Naxclow AP mode on `192.168.169.1:6123` when applicable
4. cam-reverse A9/X5 UDP/MJPEG on `32108`
5. saved-prompt PPPP/iLnk discovery on `32108` and `20190`

Outcome:

- The settings page can discover cameras without assuming one protocol.
- Discovered cards include IP, port, stream URL, and protocol variant.

### 3. Lock The Variant Matrix

Use [Phase 10 - Protocol Variant Spike](./phase-10-protocol-variant-spike.md)
after the CLI has real output.

Outcome:

- Create `protocol-variants.md`.
- Decide which variant names are stable.
- Decide which protocol-specific phase to implement next.

### 4. Implement The Proven Variant

Choose only the branch your hardware proves:

- Existing cam-reverse UDP/MJPEG is already covered by phases 1-3.
- If the camera is V720/Naxclow, implement [Phase 14](./phase-14-v720-naxclow-variant.md).
- If the camera is direct RTSP or HTTP MJPEG, keep it inside phase 5 first and
  only split a new phase if provider work grows.
- If the camera is TCP PPPP/iLnk H.264, implement [Phase 11](./phase-11-tcp-h264-session.md)
  and [Phase 12](./phase-12-h264-decoding.md).
- If Vue990 is the only confirmed viewer path, implement
  [Phase 15](./phase-15-vue990-vstarcam-ppcs-harness.md).
- If Phase 15 proves metadata and an explicit image/video artifact is needed,
  implement [Phase 16](./phase-16-csharp-capture-download.md).
- If the vendor-library path works but too much behavior still lives in Java,
  implement [Phase 17](./phase-17-csharp-vendor-adapter.md).
- If the final goal is no vendor libraries and no Java stubs, implement
  [Phase 18](./phase-18-pure-csharp-ppcs-replacement.md).
- If Phase 16 needs the fastest image proof, run
  [Phase 19](./phase-19-generated-binding-screenshot-spike.md).
- If `AppPlayerApi.Screenshot(...)` fails, fall back to
  [Phase 20](./phase-20-csharp-render-surface-capture-fallback.md).
- If vendor video download refuses to start, use
  [Phase 21](./phase-21-csharp-mjpeg-avi-video-artifact.md) for a bounded C#
  MJPEG AVI artifact.
- If the goal is Windows capture without the Android phone helper, implement
  [Phase 22](./phase-22-windows-native-csharp-capture.md).
- If Phase 22 rules out direct HTTP media, implement
  [Phase 23](./phase-23-managed-vue990-ppcs-control.md) to get managed PPCS
  connect/login/live-open and raw channel bytes before media decode.
- If the Android C# helper captures images but stalls before report completion,
  use [Phase 26](./phase-26-android-csharp-capture-stabilization.md) to
  stabilize picture/frame download and assemble video artifacts on Windows
  with C#.

Outcome:

- One real camera works end to end before expanding variant support.

### 5. Add Product UX

After a proven protocol works, implement user-facing polish:

- [Phase 6 - Known A9 Devices](./phase-6-known-devices-multiple-cameras.md)
- [Phase 7 - Stream Controls & Diagnostics](./phase-7-stream-controls-diagnostics.md)
- [Phase 9 - A9 Capture Preview](./phase-9-capture-preview.md)

Outcome:

- Users can save, select, diagnose, and preview the camera.

### 6. Add Optional Media Capabilities

Only after stable video:

- [Phase 8 - A9 Audio Input](./phase-8-audio-input.md)
- [Phase 12 - H.264 Decoding](./phase-12-h264-decoding.md), if H.264 hardware is
  actually present

Outcome:

- Audio and decoder dependencies do not destabilize the basic camera path.

### 7. Add Public API

Finish with [Phase 13 - Public A9 Camera API](./phase-13-public-api.md).

Outcome:

- `A9Camera.DiscoverAsync()`
- `A9Camera.ConnectAsync(device)`
- Shared frame APIs across supported variants

## Decision Tree

```text
Start
  |
  v
Run Phase 0 CLI probe
  |
  +-- RTSP responds ----------------------> implement direct stream path in Phase 5
  |
  +-- HTTP MJPEG responds ----------------> implement direct stream path in Phase 5
  |
  +-- V720/Naxclow responds on 6123 ------> implement Phase 14
  |
  +-- cam-reverse UDP 32108 responds -----> continue existing UDP/MJPEG path
  |
  +-- TCP PPPP/H.264 evidence appears ----> implement Phase 11, then Phase 12
  |
  +-- Vue990 live view works --------------> implement Phase 15 PPCS harness
  |
  +-- no camera found --------------------> improve Phase 0 diagnostics first
```

## Required vs Optional

Required for a reliable M38:

- Phase 0
- Phase 5
- Phase 10
- One working protocol branch for the actual camera
- Focused RealTests for that branch

Optional until hardware proves need:

- Phase 8 audio
- Phase 11 TCP PPPP/iLnk H.264
- Phase 12 decoder integration
- STA-mode V720/Naxclow fake-server support
- Multi-camera support in Phase 6
- Public facade in Phase 13

## Current Recommendation

For the current pure C# Vue990 goal, use
[C#-Only Vue990 Stream Roadmap](./csharp-only-vue990-roadmap.md) as the
controlling roadmap before creating or continuing more phase docs. The list
below is historical context for how the investigation reached the current
native-oracle-first strategy.

Do next:

1. Close the connected-phone pass for
   [Phase 15](./phase-15-vue990-vstarcam-ppcs-harness.md).
2. Phase 19 is now complete: the C# generated-binding screenshot path produced
   verified 640x480 JPEG artifacts.
3. Continue [Phase 17](./phase-17-csharp-vendor-adapter.md) so the working
   PPCS/player workflow is fully C# owned and RealTested.
4. Phase 21 is now complete: vendor `startDown` returned `False`, so the C#
   runner writes a verified MJPEG AVI from a bounded screenshot sequence.
5. Use [Phase 20](./phase-20-csharp-render-surface-capture-fallback.md) only
   if the screenshot API regresses or cannot satisfy product needs.
6. Phase 24 recovered DAS decode and decoded relay hosts. TCP `65527` opens on
   all decoded relays, but no bytes arrive without the native hello. Use
   [Phase 25](./phase-25-managed-hlp2p-relay-hello.md) to implement the managed
   HLP2P relay hello/session-open packets before attempting Phase 23 login/raw
   channel bytes again.
7. Phase 26 is complete: Android C# capture now finishes cleanly, downloads a
   still plus frame images, and the Windows C# tool assembles those frames into
   verified MJPEG AVI artifacts. Continue Phase 25 for direct Windows capture.
8. Phase 27 is complete: Windows C# can now run `vue990-android-capture` to
   drive the Android C# probe over ADB and download a fresh still JPEG plus
   MJPEG AVI. This is the working artifact path while Phase 25 continues toward
   a pure Windows PPCS session.
9. Phase 28 confirmed the tiny native packet creators and native
   `TCPSend_Hello` output.
10. Phase 29 tried the fake DAS/local relay route. The Android probe can
    override `serverParam` and listen locally, but rewritten DAS relay-host
    values produced no local connections, likely because another DAS token or
    checksum is validated by native code.
11. Phase 30 recovered native `TCPSend_TCPRlyReq` and `TCPSend_TCPRSLgn`
    loopback bytes and promoted them into managed C# packet builders. The
    decoded relays still return no bytes, so the next problem is dynamic
    argument material rather than packet length/header shape.
12. Phase 31 is the final conversion target: a C#-only PPCS library for both
    Windows and Android.
13. Phase 32 is the immediate next unblocker for Phase 31: map dynamic
    second-stage fields by varying native oracle arguments one at a time.
14. Phases 33-35 ruled out local Android HTTP/UDP/classic-PPPP media for this
    Vue990 camera; Android can run the shared C# code, but the camera does not
    answer the local stream session.
15. Phase 36 is now the active unblocker: port the Vue990 proprietary relay
    encryption and reproduce a relay-accepted second-stage request in C#.
16. Phase 37 expanded the Android direct/local matrix with Wi-Fi process
    binding, multicast/Wi-Fi locks, UDP `65529`, and vendor-app force-stop
    testing. It still produced no C#-only image/video, so the next attempt
    should focus on the native Vue990 session opener rather than more local
    endpoint guessing.
17. Phase 38 ported the native TCP relay frame builder to managed C# and
    verified `TCPRlyReq` / `TCPRSLgn` byte-for-byte against native vectors.
    Live decoded relays still returned no bytes, so the next useful branch is
    probing camera-local TCP `81` with the same managed messages and then
    observing the working Android app's traffic shape if needed.
18. Phase 39 closed Android UDP/HLP2P session opener attempts as negative
    evidence under the pure C# roadmap.
19. Phase 40 exposed the actual channel media: JPEG frames inside a
    `55 AA 15 A8` Vue990 envelope, extractable and packageable by C#.
20. Phase 41 replaced native `writeCgi` framing with C# command bytes:
    command channel `0`, header `01 0A 00 00 61 00 00 00`, and the
    credentialed live-stream CGI body. It produced a real still image plus
    MJPEG AVI while native connect/login/read still carried the session.
21. Phase 42 is now the next pure-C# gate: keep the confirmed live-open command
    fixed and replace the remaining native session transport/read carrier.

Avoid doing next:

- Do not implement FFmpeg/LibVLC until a real H.264 stream is proven.
- Do not implement V720 STA/fake-server mode before AP-mode probing works.
- Do not broaden settings UX until the selected protocol can return one frame.
- Do not store images or bridge the feed inside Phase 15. Any visual capture
  belongs in Phase 16 with explicit capture gates.
- Do not attempt a literal no-Java rewrite while still using JNI vendor libs
  unless the generated .NET Android class names can be proven equivalent.
- Do not remove `libOKSMARTPPCS.so` / `libOKSMARTPLAY.so` until Phase 18 has a
  managed PPCS replacement and image RealTest.

Current Phase 15 evidence:

- Vue990 JNI signatures and lifecycle are recovered.
- The phone harness can connect and log in through `libOKSMARTPPCS.so` using
  client id `BKGD00000100FMQLN`, VUID `BK0025644WBPD`, `connectType=0x3F`,
  `p2pType=1`, and the `DAS-...` server parameter.
- Direct RTSP/MJPEG remains unproven; only TCP `81` and `get_status.cgi` are
  exposed directly.
- Frame metadata is proven after the live `writeCgi` command:
  `livestream.cgi?streamid=10&substream=0&`.
- Visual capture is now tracked separately in Phase 16.
- C# orchestration and Java reduction are tracked in Phase 17.
- Full vendor-library removal is tracked in Phase 18.
- A C# generated-binding screenshot attempt succeeded in Phase 19.
- C# render-surface readback fallback is tracked in Phase 20.
- A C# MJPEG AVI short-video artifact succeeded in Phase 21.
- Windows-native C# capture without the phone helper is tracked in Phase 22.
- The immediate managed PPCS control/raw-channel spike is tracked in Phase 23.
- DAS decode and native `ConnectByServer` parsing were completed in Phase 24.
- Managed HLP2P relay hello/session-open is tracked in Phase 25.
- Android C# capture stabilization and Windows C# packaging were completed in
  Phase 26.
- Windows C# Android-capture orchestration was completed in Phase 27.
- Native empty-header packet bytes were confirmed in Phase 28.
- Fake DAS/local relay capture was attempted in Phase 29 and is blocked by
  likely DAS checksum/token validation.
- Native second-stage packet helper mapping was completed in Phase 30; dynamic
  fields are still missing.
- Cross-platform C# PPCS library extraction is tracked in Phase 31.
- Parameterized second-stage field mapping is tracked in Phase 32.
- Android managed direct port-matrix testing is tracked in Phase 37 and
  confirms the current blocker is the Vue990/OKSMART session opener.
- Managed TCP relay packet construction is tracked in Phase 38.

## Hardware Checkpoints

- Probe finds one camera.
- Probe selects a protocol.
- First frame is captured.
- RealTests reproduce the CLI result.
- Settings test connection uses the same selected protocol.
- Connected Devices shows the configured A9 card.

## Phase Dependency Summary

| Phase | Depends On | Purpose |
|-------|------------|---------|
| 0 | none | CLI probe and hardware-gated RealTests |
| 5 | 0 | Adaptive discovery and protocol selection |
| 10 | 0, 5 | Protocol compatibility matrix |
| 14 | 0, 5, 10 | V720/Naxclow AP-mode implementation |
| 11 | 0, 5, 10 | TCP PPPP/iLnk H.264 session, only if proven |
| 12 | 11 | H.264 decoding, only if proven |
| 15 | 0, 5, 10, Vue990 live-view evidence | Vue990 VStarcam/VeePai PPCS harness |
| 16 | 15 | Explicit C#-first still/video capture download |
| 17 | 15, 16 | C# orchestration with minimal vendor JNI stubs |
| 18 | 16, 17 | Pure C# VStarcam/PPCS replacement |
| 19 | 16, 17 | Generated-binding screenshot attempt |
| 20 | 16, 19 | Render-surface still-image fallback |
| 21 | 16, 19 | C# MJPEG AVI fallback video artifact |
| 22 | 18, 21 | Windows-native C# image/video capture |
| 23 | 18, 22 | Managed Vue990 PPCS control and raw channel bytes |
| 24 | 23 | DAS decode and `ConnectByServer` reverse path |
| 25 | 24 | Managed HLP2P relay hello/session-open |
| 26 | 16, 21 | Android C# capture stabilization and Windows C# packaging |
| 27 | 26 | Windows C# Android capture orchestration |
| 28 | 25 | Android native packet oracle |
| 29 | 24, 28 | Fake DAS local relay oracle, blocked by DAS validation |
| 30 | 28, 29 | Native second-stage packet oracle, packet shapes recovered |
| 31 | 25, 29, 30 | Cross-platform C# PPCS library |
| 32 | 30, 31 | Parameterized second-stage field mapping |
| 37 | 33, 35, 36 | Android direct/local C# port matrix and vendor-app socket check |
| 38 | 32, 36, 37 | Managed TCP relay frame builder and live relay retest |
| 39 | 37, 38 | Android C# UDP/HLP2P session opener close-out |
| 40 | 39 | Native channel/session oracle |
| 41 | 40 | Managed live-CGI command framing and channel media proof |
| 42 | 41 | Managed session transport/read replacement |
| 6 | 5 plus a working protocol | Saved devices |
| 7 | working protocol | Stream controls and diagnostics |
| 8 | working protocol | Optional audio |
| 9 | working protocol | Capture preview |
| 13 | stable variants | Public A9 API |
