# Phase 33 - Managed Android Local Stream

**Status:** Completed - local media surface exhausted

## Goal

Build and test a C#-only Android path that talks to the camera while the phone is
connected to `@MC-0025644`, downloads a still image and video frames if the
camera exposes them locally, and keeps the code portable enough to reuse on
Windows.

## Why This Phase Exists

The user clarified that Android relay/cloud probing is not the goal. The useful
target is a managed C# stream path on Android first; once that works, the same
transport and decoding code can be moved to Windows.

## Current Evidence

- Android phone is connected to the camera AP as `192.168.168.101/24`.
- Camera/AP is `192.168.168.1`.
- Direct HTTP on port `81` still exposes only status/control:
  - `get_status.cgi` returns `200` and the current device metadata.
  - direct `/livestream.cgi`, `/videostream.cgi`, `/snapshot.cgi`, and common
    credential variants return `404`.
- The native Vue990/PPCS app path still streams and captures successfully:
  - `JNIApi.connect=3`
  - `JNIApi.login=True`
  - `JNIApi.writeCgi("livestream.cgi?streamid=10&substream=0&", channel=1)=True`
  - player reports `640x480` draw frames.

## Work Plan

1. [x] Stop treating Android relay as the main path.
2. [x] Add a managed-direct Android probe mode that does not call Vue990 native
   PPCS/player libraries.
3. [x] Save any direct HTTP JPEG/MJPEG frames to app storage and assemble MJPEG
   AVI when possible.
4. [x] Add VStarcam/XQP2P LAN discovery payloads to the Android UDP probe.
5. [x] Run the managed-direct probe while the phone is on `@MC-0025644`.
6. [x] Document whether the local camera surface is enough for C#-only capture.
7. [x] If local HTTP/UDP is still control-only, move to managed PPCS transport
   replacement: implement the PPCS connection/channel layer in C# and feed the
   existing `A9Vue990CgiCommandBuilder` live-stream request over channel `1`.

## Live Outcomes

- Implemented `BodyCam.A9Probe vue990-android-managed-direct`.
- Implemented Android-side `ManagedDirectMediaProbe` with no `JNIApi` or
  `AppPlayerApi` calls.
- Added a shared managed C# PPCS packet layer:
  `A9Vue990PpcsPacket`, `A9Vue990PpcsEncryptionCodec`, and
  `A9Vue990VideoFrameAssembler`.
- Rebuilt the Android probe with the shared packet layer linked into the APK.
- Live managed-direct run:
  `.my/plan/m38-a9-camera/captures/phase-33-android-managed-direct-2026-05-29-150741.json`
- Phone was on `@MC-0025644` as `192.168.168.101/24`.
- Camera was reachable as `192.168.168.1`.
- Only local TCP port found open was `81`.
- HTTP/CGI media probing found only status responses from `get_status.cgi`;
  no JPEG, MJPEG, H264, or other video-like payload was returned.
- UDP discovery sent plain PPPP, XOR1 PPPP, extended LAN search, SHIX/A9, and
  JSON discovery payloads. The only received packets were phone self-echoes;
  no remote camera UDP response was observed.
- Result: `Captured image: False`, `Captured video: False`, `Artifacts: 0`.

## Expected Success Signal

- A managed C# Android probe saves at least one real `640x480` JPEG from the
  camera without calling `JNIApi` or `AppPlayerApi`.
- Preferably it also saves a short MJPEG AVI assembled from multiple managed
  frames.

## Blocker Signal

If the managed-direct probe sees only `get_status.cgi` and no media bytes, then
the camera stream is not exposed as normal local HTTP/MJPEG/RTSP. In that case,
the next phase must implement the lower PPCS/XQP2P/HLP2P channel in C# rather
than relying on URL probing.

That blocker signal was hit. The Android path is still useful, but the next code
must speak the same PPCS/DRW transport that the Vue990 native library speaks.
This keeps Android as the first test host while building code that can later run
unchanged on Windows.

## How The User Can Help

- Keep the phone connected to `@MC-0025644`.
- Keep USB debugging connected.
- Keep the camera powered by USB.
- Tell Codex if the Vue990 app still shows live video while the managed-direct
  probe reports no media; that confirms the stream is available only through the
  PPCS channel.
