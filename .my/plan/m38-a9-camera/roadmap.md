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

Do next:

1. Implement Phase 0 CLI skeleton.
2. Run it with one powered-on camera.
3. Save the JSON probe result under `.my/plan/m38-a9-camera/captures/`.
4. Update `protocol-variants.md`.
5. Pick the first protocol implementation branch from the probe output.

Avoid doing next:

- Do not implement FFmpeg/LibVLC until a real H.264 stream is proven.
- Do not implement V720 STA/fake-server mode before AP-mode probing works.
- Do not broaden settings UX until the selected protocol can return one frame.

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
| 6 | 5 plus a working protocol | Saved devices |
| 7 | working protocol | Stream controls and diagnostics |
| 8 | working protocol | Optional audio |
| 9 | working protocol | Capture preview |
| 13 | stable variants | Public A9 API |
