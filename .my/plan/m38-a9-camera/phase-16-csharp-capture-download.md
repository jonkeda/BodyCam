# Phase 16 - C#-First Vue990 Capture Download

**Status:** In Progress

## Goal

Turn the Phase 15 metadata proof for `@MC-0025644` into an explicit,
opt-in capture path that can download a real image or short video artifact from
the camera.

This phase also answers the C# rewrite question: move as much orchestration as
possible into C#/.NET Android, while keeping only the minimum Java/JNI surface
needed to satisfy the vendor native libraries.

## Starting Point

Phase 15 proved the live stream path without storing visual content:

- Phone joins camera AP `@MC-0025644`.
- Camera/AP IP is `192.168.168.1`.
- Status endpoint returns VUID `BK0025644WBPD`, alias `BK7252N`, and the
  `DAS-...` server parameter.
- VStarcam PPCS connect/login works with client id `BKGD00000100FMQLN`,
  `connectType=0x3F`, and `p2pType=1`.
- The missing live-open command is:
  `livestream.cgi?streamid=10&substream=0&` on PPCS channel `1`.
- After live-open, the player receives live metadata and reports draw size
  `640x480`.

## What "C#-First" Means Here

The Vue990 native libraries are Android JNI libraries, not normal C ABI
libraries. They look for Java class names and callback interfaces such as:

- `com.vstarcam.JNIApi`
- `com.vstarcam.app_p2p_api.ClientStateListener`
- `com.vstarcam.app_p2p_api.ClientCommandListener`
- `com.vstarcam.app_p2p_api.ClientReleaseListener`
- `com.veepai.AppPlayerApi`
- `com.veepai.AppPlayerApi.AppPlayerProgress`

Because of that, a literal "no Java at all" rewrite is high risk. The practical
target for this phase is:

- C# owns the probe/capture state machine, UI, file naming, artifact handling,
  assertions, and RealTests.
- Java is reduced to thin native declaration/callback stubs only, or replaced
  with generated .NET Android callable wrappers if that proves reliable.
- No protocol decisions or capture policy live in Java.

If the generated-wrapper approach cannot satisfy the vendor native class names,
keep the tiny Java stubs. That still gives us a C#-first implementation without
rewriting or reverse-engineering the vendor native libraries.

## Capture Boundary

Phase 16 is the first phase allowed to save visual camera data. It must be
explicit and controlled.

Allowed:

- Save one still image from a user-prepared test scene.
- Save one short video clip from a user-prepared test scene.
- Store those artifacts under `.my/plan/m38-a9-camera/captures/phase-16/`.
- Save matching metadata: timestamp, camera id, stream command, width/height,
  duration, byte count, codec/container, and hash.

Not allowed:

- Continuous recording.
- Background recording.
- Rebroadcasting or bridging the feed.
- Capturing people, private spaces, screens, documents, or audio unless the
  user explicitly prepares and approves that test scene.
- Uploading artifacts anywhere.
- Mutating camera settings, Wi-Fi config, firmware, SD-card state, or cloud
  account data.

## Implementation Paths

### Path A - Vendor Player Screenshot API

Use the already-working `libOKSMARTPLAY.so` player path and call the VeePai
player screenshot API after the player reports a valid drawn frame.

Evidence:

- Vue990's decompiled `com.veepai.AppPlayerApi` exposes:
  `screenshot(long playerPtr, String path, int width, int height, float baseWidth, float baseHeight, int mode)`.
- Vue990's Flutter plugin calls it as `app_player_screenshot` with seven args:
  `playerId`, `path`, two integers, two floats, and one integer.
- `libOKSMARTPLAY.so` strings include `renderScreenshot`,
  `app_video_frame_convert_jpeg_file`, and
  `Java_com_veepai_AppPlayerApi_screenshot`.

Steps:

1. Use the Phase 15 PPCS path exactly as proven.
2. Call `writeCgi(clientPtr, "livestream.cgi?streamid=10&substream=0&", 1)`.
3. Create the VeePai player and set live source to the PPCS client pointer.
4. Start the player.
5. Wait for at least one `app_player_draw_info width=640 height=480`.
6. Call:
   `AppPlayerApi.screenshot(playerPtr, jpgPath, 640, 480, 640f, 480f, 0)`.
7. Save the JPEG image to
   `.my/plan/m38-a9-camera/captures/phase-16/`.
8. Pull the file over ADB and compute dimensions, byte count, and hash.

Pros:

- Uses the proven player path.
- Avoids needing to parse the proprietary frame payload.
- Closest to what Vue990 does.

Risks:

- Screenshot parameters are partly inferred; wrong clip/base dimensions may
  trigger native errors such as invalid clip base width/height.
- Calling too early may hit native "screenshot invalid frame" paths.
- This still uses `libOKSMARTPLAY.so`; pure C# image extraction belongs to
  Phase 18.

### Path B - C# Owned Render Surface Readback

If the vendor screenshot API returns `false` or creates an invalid file, render
into a C# controlled Android view/surface and copy pixels from that render
target.

Steps:

1. Replace the dummy `SurfaceTexture` with a C# owned `TextureView`, `Surface`,
   or `ImageReader` path.
2. Start the proven PPCS/player workflow.
3. After draw metadata reports a frame, use Android `PixelCopy` or
   `TextureView.GetBitmap()` to copy one frame.
4. Save the bitmap as JPEG/PNG under the phase capture directory.
5. Verify dimensions, format, byte count, and hash.

Pros:

- Keeps capture policy and file writing in C#.
- Still uses the vendor decoder/player but not the vendor screenshot helper.

Risks:

- More Android UI/rendering complexity.
- Dummy surfaces may not be readable; a real UI surface may be required.

### Path C - Channel Payload Recorder

Read raw stream bytes from PPCS channel `1` after live-open and package them
into a short capture artifact.

Steps:

1. Identify whether `JNIApi.read` or an equivalent native callback exists; do
   not guess.
2. If readable, capture a bounded byte window after live-open.
3. Determine frame/container format from headers.
4. Save a raw stream file plus metadata.
5. Decode only if the format is known and a local decoder is already available.

Pros:

- Could lead to a non-Android provider path later.

Risks:

- Current JNI surface proves `checkBuffer`, not direct frame reads.
- Payload framing may be proprietary.
- More likely to store undecodable data before it stores a useful image.

Preferred order: Path A, then Path B, then Path C only if needed.

## Video Download Plan

A "full video download" in this phase means a bounded, intentional local file,
not an ongoing stream bridge.

Initial target:

- Duration: 3 seconds.
- Resolution: whatever the player reports, currently `640x480`.
- Container: MP4 if Android `MediaCodec`/`MediaMuxer` can encode from the
  rendered frames; otherwise raw frame sequence plus metadata.
- No audio in the first pass.

Steps:

1. Prove one still image first.
2. Try `AppPlayerApi.save(...)`, `startDown(...)` / `stopDown(...)`,
   `saveMP4(...)`, or a C# controlled render/encoder path only after the still
   capture works.
3. Capture a short sequence from the same controlled render path.
4. Encode locally on the phone, or save a bounded image sequence if encoding is
   not available yet.
5. Pull the artifact over ADB.
6. Verify file exists, duration/frame count is non-zero, dimensions are known,
   and hash is recorded.

## RealTests

Add separate hardware gates so normal test runs never capture media:

- `A9_E2E=1`
- `A9_PHONE_PPCS_E2E=1`
- `A9_PHONE_CAPTURE_E2E=1`

Still image RealTest:

- Phone is on `192.168.168.x`.
- Android probe app installs/runs.
- PPCS connect/login succeeds.
- live `writeCgi` succeeds.
- one image file is created.
- image byte count is greater than a minimum threshold.
- image dimensions are `640x480` or another explicitly reported stream size.
- image hash is printed to test output.

Video RealTest:

- All still-image assertions pass first.
- one bounded video artifact or frame sequence is created.
- duration or frame count is non-zero.
- dimensions are known.
- byte count and hash are printed to test output.

## Implementation Checklist

- [x] Create this Phase 16 plan doc.
- [x] Decide whether to keep thin Java native stubs or attempt generated C#
      Android callable wrappers for the vendor JNI class names.
- [x] Move the Phase 15 PPCS/player orchestration from Java into C# where
      practical.
- [x] Keep Java, if any, limited to native declarations and callback shims.
- [x] Add missing `AppPlayerApi.screenshot`, `save`, `startDown`, `stopDown`,
      and `saveMP4` declarations to the probe wrapper.
- [x] Add an explicit capture-mode UI/intent flag; default remains
      metadata-only.
- [x] Add a phase-specific capture directory.
- [x] Implement Path A still-image capture through `AppPlayerApi.screenshot`.
- [x] Pull and verify one still image artifact.
- [x] Implement image metadata and hash reporting when the file is created.
- [x] Add hardware-gated still-image RealTest.
- [x] Implement bounded short video capture only after still image works.
- [x] Pull and verify one video artifact or bounded frame sequence.
- [x] Add hardware-gated video RealTest.
- [x] Update `realtests-log.md` with high-level outcomes only.
- [x] Update `realtests-report-2026-05-28.md` or a new dated report with
      artifact paths and verification.

## Current Implementation Status

- The current capture implementation is in the Android C# runner
  `Vue990PpcsSession`.
- It keeps metadata-only behavior as the default.
- It only attempts a still image when the app is launched with
  `capture_image=true`.
- The image path is app-private:
  `files/captures/phase-16/a9-capture-*.jpg`.
- The report records existence, bytes, dimensions, and SHA-256 if a file is
  created.
- `A9PhonePpcsPlayer_CapturesStillImage` is gated by `A9_E2E=1` and
  `A9_PHONE_CAPTURE_E2E=1`; it starts capture mode, asserts image metadata in
  the report, and pulls the JPEG with `adb exec-out run-as`.
- Live capture succeeded after the phone stayed on `@MC-0025644`.
- Short-video capture is implemented by Phase 21's C# MJPEG AVI fallback:
  the vendor `startDown(...)` API returned `False`, so the runner captures a
  bounded screenshot sequence and packages it with a pure C# AVI writer.

## Still Image Outcome

Manual capture:

- Local artifact:
  `.my/plan/m38-a9-camera/captures/phase-16/a9-capture-2026-05-28-222832.jpg`
- Bytes: `37244`
- Dimensions: `640x480`
- SHA-256:
  `3B7610B415D7748C1D28117B2FDEDF87E2FAFD45B53A54AC8EBE56BF36866C4E`
- Matching report:
  `.my/plan/m38-a9-camera/captures/a9-phone-capture-success-2026-05-28-222832.txt`

Hardware-gated RealTest capture:

- Local artifact:
  `.my/plan/m38-a9-camera/captures/phase-16/a9-capture-2026-05-28-223037.jpg`
- Bytes: `29678`
- Dimensions: `640x480`
- SHA-256:
  `42EF85EF58C7EF9CA788AB7BB65E5FD999493CF9183B115E654BD76C9E0A40F7`
- Test result: `A9PhonePpcsPlayer_CapturesStillImage` passed.

## Short Video Outcome

Initial vendor-download attempt:

- `AppPlayerApi.StartDown(...)` was generated and callable from C#.
- On the proven live player path it returned `False` and created no `.ts` file.
- Matching report:
  `.my/plan/m38-a9-camera/captures/a9-phone-video-attempt-startdown-false-2026-05-28-225654.txt`

C# MJPEG AVI fallback:

- The runner captured six verified `640x480` JPEG frames at `2` fps.
- It packaged them into an MJPEG AVI using the pure C# `MjpegAviWriter`.
- Manual local artifact:
  `.my/plan/m38-a9-camera/captures/phase-16/a9-video-2026-05-28-230038-mjpeg.avi`
- Bytes: `436722`
- SHA-256:
  `A92E3C3B79A9CEE92166E9B92506DB320BCE6D9E19F3FD527CCA2823F01F3504`

Hardware-gated RealTest video capture:

- Test result: `A9PhonePpcsPlayer_CapturesShortVideoArtifact` passed with
  `A9_E2E=1`, `A9_PHONE_VIDEO_E2E=1`, and
  `A9_CAMERA_IP=192.168.168.1`.
- Local artifact:
  `.my/plan/m38-a9-camera/captures/phase-16/a9-video-2026-05-28-230235-mjpeg.avi`
- Bytes: `420638`
- Header: `RIFF ... AVI`
- SHA-256:
  `E898807A7F7F9B9325057C69A453DB64B5EFEC8404C6DBD8CAB99C654A129130`

## Acceptance Criteria

- Metadata-only mode still works and remains the default.
- Visual capture only runs when explicitly requested.
- A still image is saved locally from the camera stream and verified by file
  size, dimensions, and hash.
- A short video or bounded frame sequence is saved locally and verified by file
  size, dimensions, duration/frame count, and hash.
- Captures are stored only under the phase capture directory.
- RealTests skip by default and only capture media when the explicit capture
  gate is set.

## Open Questions

- Can .NET Android generated wrappers fully replace the Java class names needed
  by `libOKSMARTPPCS.so` and `libOKSMARTPLAY.so`?
- Does `libOKSMARTPLAY.so` expose a safe snapshot API, or is render-surface
  readback the cleanest path?
- Does the player expose already-decoded frames, or only render callbacks?
- Should the first video artifact be MP4, or is a bounded PNG/JPEG sequence
  more reliable for the first proof?
