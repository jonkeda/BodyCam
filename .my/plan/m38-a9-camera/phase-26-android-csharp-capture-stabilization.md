# Phase 26 - Android C# Capture Stabilization And Windows Packaging

**Status:** Complete - 2026-05-29

## Goal

Make the Android C# probe finish cleanly while the phone is connected to
`@MC-0025644`, then download a real still image and frame images. If video is
needed, assemble it on Windows using C# from the verified frame images.

This phase exists because the previous Android run captured the still and
frames but stalled before the final report completed.

## Outcome

- The Android C# probe completed and reached `JNIApi.destroy=done`.
- The phone was on `wlan0=192.168.168.101/24`.
- PPCS connect/login/live-open worked with the Vue990/VStarcam player path.
- `AppPlayerApi.Screenshot(...)` saved a fresh `640x480` JPEG still.
- `AppPlayerApi.StartDown(...)` still returned `False`, so vendor video
  download is not the working path for this camera.
- The fallback captured six verified `640x480` JPEG frame images.
- Android-side AVI assembly is skipped; the stable contract is frame download
  plus Windows C# MJPEG AVI assembly.
- The Windows C# `BodyCam.A9Probe mjpeg-avi` command assembled the pulled
  frame sequence into a verified `RIFF AVI` artifact.
- The hardware-gated video RealTest passed with the same contract.

## Artifacts

Artifact root:

`.my/plan/m38-a9-camera/captures/phase-26-android-csharp-capture-2026-05-29-122420/a9probe-phase-26-2026-05-29-122420/`

- Still image:
  `a9-capture-2026-05-29-122420.jpg`
  - Bytes: `25605`
  - Dimensions: `640x480`
  - SHA-256:
    `1B41A829588F085F984DE3CEDADFDBD91F184EBA5259B92E298392B90C6B52B7`
- Frame manifest:
  `a9-video-2026-05-29-122420-mjpeg-frames.txt`
- Frame directory:
  `a9-video-2026-05-29-122420-mjpeg-frames/`
  - Frames: `6`
  - Dimensions: `640x480`
- Manual Windows C# AVI:
  `a9-video-2026-05-29-122420-mjpeg.avi`
  - Bytes: `153334`
  - Header: `RIFF ... AVI`
  - SHA-256:
    `00777EBE8E2CE141ECF6D59DBCA3A328B382D68C4E67363E17979DC828DAF64C`
- Hardware RealTest Windows C# AVI:
  `a9-video-2026-05-29-122701-mjpeg.avi`
  - Bytes: `158698`
  - Header: `RIFF ... AVI`
  - SHA-256:
    `78BD16FB1FE8803E9AE0D9054DF9AFD6475AF178727083260F883A4F6312FD5B`
- Android report:
  `latest-a9-phone-probe.txt`
- Filtered logcat:
  `a9-phone-csharp-capture-logcat-2026-05-29-122420.txt`

## Code Changes

- Added a timeout around Android main-thread native calls in
  `Vue990PpcsSession`.
- Changed the Android fallback video path to write a frame-sequence manifest
  after verified frame captures.
- Kept AVI assembly in Windows C# with `BodyCam.A9Probe mjpeg-avi`.
- Updated `A9PhonePpcsPlayer_CapturesShortVideoArtifact` so the RealTest pulls
  frame images and assembles the local AVI with `A9MjpegAviWriter`.

## Verification

- `dotnet build tools/BodyCam.A9PhoneProbe/BodyCam.A9PhoneProbe.csproj -f net10.0-android`
  passed.
- `dotnet build tools/BodyCam.A9Probe/BodyCam.A9Probe.csproj -f net10.0-windows10.0.19041.0`
  passed with existing unrelated warnings.
- Targeted `A9PhonePpcsPlayerRealTests` passed in skipped mode when hardware
  gates were not set.
- Hardware-gated
  `A9PhonePpcsPlayer_CapturesShortVideoArtifact` passed with:
  - `A9_E2E=1`
  - `A9_PHONE_VIDEO_E2E=1`
  - `A9_CAMERA_IP=192.168.168.1`

## Notes

- This is C# orchestration and C# artifact packaging. The vendor JNI libraries
  and minimal Android native declarations remain until Phase 18 replaces the
  VStarcam/VeePai transport in managed C#.
- This phase proves the Android helper can reliably download picture/frame
  artifacts. It does not solve direct Windows-native camera streaming.
- Resume Phase 25 next: managed HLP2P relay hello/session-open against decoded
  TCP `65527` relay hosts.
