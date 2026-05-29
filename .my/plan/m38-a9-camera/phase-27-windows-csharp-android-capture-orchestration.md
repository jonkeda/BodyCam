# Phase 27 - Windows C# Android Capture Orchestration

**Status:** Completed - Windows C# command downloaded a fresh image and video

## Goal

Create a repeatable C# command on Windows that uses the Samsung phone as the
camera-network probe host, downloads a still image and frame sequence, and
assembles a video artifact on Windows with C#.

This phase does not claim pure Windows PPCS replacement. The Android probe still
uses the Vue990 native PPCS/player libraries on the phone. The improvement is
that Windows now has a single C# command that drives the Android C# probe,
pulls the image/frame artifacts over ADB, and creates the final MJPEG AVI with
the shared C# writer.

## Implementation

- Added `A9AndroidPhoneCaptureClient`.
- Added `BodyCam.A9Probe vue990-android-capture`.
- The command verifies ADB, confirms the phone is on the camera subnet,
  installs the Android C# probe, starts PPCS image/video capture, pulls the
  JPEG artifacts, assembles an MJPEG AVI, and writes report/logcat/JSON files.

## Successful Run

Command:

```powershell
dotnet .\tools\BodyCam.A9Probe\bin\Debug\net10.0-windows10.0.19041.0\BodyCam.A9Probe.dll vue990-android-capture --output-dir .\.my\plan\m38-a9-camera\captures\phase-27-android-csharp-orchestrated-2026-05-29-131301 --output .\.my\plan\m38-a9-camera\captures\phase-27-android-csharp-orchestrated-2026-05-29-131301\a9-android-csharp-capture-result-2026-05-29-131301.json --timeout-seconds 140
```

Outcome:

- success: `True`
- phone package: `com.bodycam.a9phoneprobe`
- phone Wi-Fi: `192.168.168.101/24`
- camera host: `192.168.168.1`
- still image:
  `.my/plan/m38-a9-camera/captures/phase-27-android-csharp-orchestrated-2026-05-29-131301/a9-capture-2026-05-29-131309.jpg`
- video:
  `.my/plan/m38-a9-camera/captures/phase-27-android-csharp-orchestrated-2026-05-29-131301/a9-video-2026-05-29-131301-mjpeg.avi`
- JSON:
  `.my/plan/m38-a9-camera/captures/phase-27-android-csharp-orchestrated-2026-05-29-131301/a9-android-csharp-capture-result-2026-05-29-131301.json`

## Verification

- still JPEG: `25062` bytes, `640x480`
- still SHA-256:
  `E2073095B709B01ADF28230771ECFD33E26E4DC70C2FCAF88D04301EED92FB3F`
- frame count: `6`
- AVI: `150612` bytes, `RIFF ... AVI`
- AVI SHA-256:
  `D21CFBD55E001F6086D1C55498BDE66EFF9EED6E424DE9C1F80E7500E2680FC7`

## Follow-Up

The next pure Windows step remains Phase 25: recover the exact HLP2P/PPCS
session-open packet sequence. Corrected native-derived empty headers were tried
against decoded TCP `65527` relay hosts and still produced no response bytes,
so the missing piece is likely a larger second-stage packet with device ids,
endpoint material, flags, and/or crypto/checksum fields.
