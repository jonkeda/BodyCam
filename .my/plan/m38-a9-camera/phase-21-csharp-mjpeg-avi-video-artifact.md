# Phase 21 - C# MJPEG AVI Video Artifact

**Status:** Completed

## Goal

Create a bounded short-video artifact from the proven Vue990/PPCS live stream
when the vendor player download APIs do not start outside the full Vue990 app.

This phase is the practical video fallback for Phase 16. It keeps the same
explicit capture gate and uses C# for the capture loop, artifact packaging,
verification, ADB pull, and RealTest assertions.

## Reason

The first vendor-download attempt added generated bindings for:

- `AppPlayerApi.StartDown(long, string)`
- `AppPlayerApi.StopDown(long)`
- `AppPlayerApi.TsToMP4(string, string)`
- `AppPlayerApi.SaveMP4(string, string, int, int, int)`

The live run on `@MC-0025644` proved the stream was active, but
`StartDown(...)` returned `False` every time and created no `.ts` file.

Because `AppPlayerApi.Screenshot(...)` was already proven, the fallback is to
capture a short sequence of verified JPEG frames and package them into a simple
Motion-JPEG AVI container written in C#.

## Implementation

- `capture_video=true` remains opt-in.
- The runner first attempts `StartDown(...)` once and records the result.
- If `StartDown(...)` returns `False`, the runner falls back to a C# frame
  sequence:
  - capture 6 JPEG frames with `AppPlayerApi.Screenshot(...)`;
  - verify each frame has non-zero bytes and `640x480` dimensions;
  - package the frames into an MJPEG AVI with `MjpegAviWriter`;
  - record bytes, SHA-256, and file prefix.
- The AVI is stored in the same phase capture directory:
  `files/captures/phase-16/a9-video-*-mjpeg.avi`.

## Outcome

Manual connected run:

- Local artifact:
  `.my/plan/m38-a9-camera/captures/phase-16/a9-video-2026-05-28-230038-mjpeg.avi`
- Bytes: `436722`
- Header: `RIFF ... AVI`
- SHA-256:
  `A92E3C3B79A9CEE92166E9B92506DB320BCE6D9E19F3FD527CCA2823F01F3504`
- Matching report:
  `.my/plan/m38-a9-camera/captures/a9-phone-video-mjpeg-avi-success-2026-05-28-230038.txt`

Hardware-gated RealTest:

- Test: `A9PhonePpcsPlayer_CapturesShortVideoArtifact`
- Gates: `A9_E2E=1`, `A9_PHONE_VIDEO_E2E=1`
- Result: passed while the phone was connected to `@MC-0025644`.
- Local artifact:
  `.my/plan/m38-a9-camera/captures/phase-16/a9-video-2026-05-28-230235-mjpeg.avi`
- Bytes: `420638`
- Header: `RIFF ... AVI`
- SHA-256:
  `E898807A7F7F9B9325057C69A453DB64B5EFEC8404C6DBD8CAB99C654A129130`
- Matching report:
  `.my/plan/m38-a9-camera/captures/a9-phone-video-realtest-2026-05-28-230251.txt`

## Checklist

- [x] Add the generated binding declaration for `tsToMP4`.
- [x] Try the vendor `startDown` / `stopDown` download path once.
- [x] Record that `startDown` returns `False` on the proven live player path.
- [x] Add a bounded C# frame-sequence fallback.
- [x] Add a pure C# MJPEG AVI writer.
- [x] Pull and verify one manual AVI artifact.
- [x] Add hardware-gated video RealTest.
- [x] Pull and verify one RealTest AVI artifact.
- [x] Update phase docs, report, and high-level log.

## Acceptance

Phase 21 satisfies the short-video artifact requirement for this hardware
without broadening capture scope:

- capture is explicit and bounded;
- no feed is bridged or rebroadcast;
- the artifact is local only;
- the run records PPCS/player proof, frame dimensions, bytes, and SHA-256;
- the same path is covered by an opt-in hardware RealTest.
