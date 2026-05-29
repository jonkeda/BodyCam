# Phase 19 - Generated Binding Screenshot Spike

**Status:** Completed

## Goal

Get the first explicit camera image artifact with the smallest reliable step
toward the final C# goal.

Instead of waiting for the full pure-C# VStarcam/PPCS replacement, use .NET
Android's generated C# bindings for the existing exact Java JNI declaration
classes:

- `Com.Vstarcam.JNIApi`
- `Com.Veepai.AppPlayerApi`
- `Com.Vstarcam.App_p2p_api.*`

This keeps the exact vendor-required Java class names but moves the workflow
and capture policy into C#.

## Why This New Tactic

The previous plan was too sequential:

1. Move everything into C#.
2. Then capture an image.
3. Then remove vendor libraries.

The better route is:

1. Keep the thin Java JNI declarations.
2. Move the session/capture state machine into C# now.
3. Use `AppPlayerApi.screenshot(...)` to save one controlled JPEG.
4. Use that artifact to validate the stream and guide the pure-C# replacement.

## Implementation Checklist

- [x] Create this Phase 19 plan doc.
- [x] Add missing `AppPlayerApi.screenshot(...)` declaration.
- [x] Create a C# `Vue990PpcsSession` runner using generated bindings.
- [x] Implement C# callbacks for PPCS state/command/release and player
      progress.
- [x] Add explicit `capture_image=true` intent mode.
- [x] Implement one-JPEG save attempt under app-private `captures/phase-16/`.
- [x] Implement report output for path, byte count, dimensions, and SHA-256.
- [x] Pull the JPEG over ADB only during the explicit capture run.
- [x] Update RealTests to exercise the C# runner.

## Implementation Outcome

- Added screenshot/save/download native declarations to
  `tools/BodyCam.A9PhoneProbe/Java/com/veepai/AppPlayerApi.java`.
- Added `tools/BodyCam.A9PhoneProbe/Vue990PpcsSession.cs`.
- Updated `MainActivity` so PPCS mode uses the C# generated-binding session
  instead of `PpcsProbeBridge.runProbe`.
- Added explicit autorun capture mode with `--ez capture_image true`.
- Android build passed and the APK installed on the Samsung phone.
- Added `A9PhonePpcsPlayer_CapturesStillImage`, gated by
  `A9_PHONE_CAPTURE_E2E=1`, to run the C# capture path and pull the JPEG when
  hardware is connected.

Capture command prepared:

```powershell
adb shell am start -n com.bodycam.a9phoneprobe/crc64ae57c528e26a7b15.MainActivity `
  --ez autorun true --ez ppcs true --ez capture_image true --es host 192.168.168.1
```

## Acceptance Criteria

- Metadata-only PPCS/player mode still works.
- Image capture mode is off by default.
- Capture mode saves exactly one JPEG during a bounded run.
- The report includes local path, bytes, dimensions, and hash.
- The runner uses C# orchestration, not `PpcsProbeBridge.runProbe`.

## Current Blocker

The Samsung phone is currently on `192.168.1.67/24`, not the camera AP subnet.
`adb shell ping -c 1 -W 1 192.168.168.1` returned 100% packet loss.
An ADB shell Wi-Fi connect attempt to the saved open `@MC-0025644` network did
not find the AP in scan results; Wi-Fi was restored to `jobaboe` afterward.
On the next attempt, the phone briefly showed `@MC-0025644` and
`192.168.168.100/24`, but Android fell back to `jobaboe` before the capture app
started. The capture report failed at status fetch, not at PPCS or screenshot.

The successful run happened after Wi-Fi was re-enabled and the phone stayed on
`@MC-0025644`.

Result:

- `JNIApi.connect=3`
- `JNIApi.login=True`
- live `writeCgi` returned `True`
- `checkBuffer[90]` showed live buffered bytes
- player draw metadata reported `640x480`
- `AppPlayerApi.screenshot(...)` returned `True`
- one JPEG was pulled over ADB and verified locally

Local image:
`.my/plan/m38-a9-camera/captures/phase-16/a9-capture-2026-05-28-222832.jpg`

Verified image metadata:

- Bytes: `37244`
- Dimensions: `640x480`
- SHA-256:
  `3B7610B415D7748C1D28117B2FDEDF87E2FAFD45B53A54AC8EBE56BF36866C4E`
