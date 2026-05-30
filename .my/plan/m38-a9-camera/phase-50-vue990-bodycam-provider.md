# Phase 50 - Vue990 BodyCam Camera Provider

**Status:** Implemented, app build verified; live app capture not yet rerun

## Goal

Hook the working Vue990 managed-direct C# capture path into the BodyCam app as a
real camera provider, while keeping the older cam-reverse/iLnkP2P
`A9CameraProvider` intact.

This phase creates a separate `Vue990CameraProvider` next to the existing A9
provider. The two camera families are related in the real world, but they use
different protocols in this repo:

- `A9CameraProvider`: older cam-reverse/iLnkP2P UDP `32108` path.
- `Vue990CameraProvider`: proven Vue990/BK7252N compact HLP2P/direct C# path.

## Starting Point

The direct Vue990 capture path already works from the CLI:

```powershell
dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- vue990-direct-capture --host 192.168.168.1 --output-dir .my\plan\m38-a9-camera\captures\vue990-direct-capture-latest --stream-seconds 18 --max-frames 12
```

Latest proof is documented in:

- `phase-49-final-csharp-hardening.md`
- `vue990-csharp-capture-solution.md`
- `vue990-capture-journey-report.md`

## Implementation Plan

1. Add `Vue990CameraProvider`.
   - Implement `ICameraProvider`.
   - Use provider id `vue990-camera`.
   - Use display name `Vue990 Camera`.
   - Use `A9Vue990DirectCaptureClient` to perform a bounded managed-direct
     capture.
   - Return the captured `managed-direct-still.jpg` bytes from
     `CaptureFrameAsync`.
   - Keep raw diagnostic capture artifacts in the app cache directory, not the
     repo.

2. Keep the older A9 provider unchanged.
   - Do not merge Vue990 into `A9CameraProvider`.
   - Do not alter `A9Session` or the cam-reverse protocol path.

3. Add separate Vue990 settings.
   - Persist `Vue990CameraIp`.
   - Default the setup page to `192.168.168.1`.
   - Test connection through `A9Vue990StatusClient`.

4. Register the provider in BodyCam.
   - Register the direct capture client in DI.
   - Register `Vue990CameraProvider` as an `ICameraProvider`.
   - Add a Vue990 add-device card and settings route.
   - Make the provider selectable from the existing custom camera source picker.

5. Add focused tests.
   - Provider returns JPEG bytes when direct capture produces a still artifact.
   - Provider returns `null` when unconfigured or capture fails.
   - Vue990 settings page view model persists and tests host settings.
   - Add Devices includes the separate Vue990 card.

## Acceptance Criteria

- `A9CameraProvider` remains the older iLnkP2P provider.
- `Vue990CameraProvider` is a separate provider with provider id
  `vue990-camera`.
- Selecting `Vue990 Camera` in the BodyCam camera source can call
  `CaptureFrameAsync` and return a JPEG frame.
- The Vue990 add-device flow is separate from the classic A9 flow.
- Unit tests cover the provider and settings view model.
- Build and focused tests pass.

## Non-Goals

- Do not derive the four post-hole controls in this phase.
- Do not add live preview streaming UI.
- Do not implement a new video-recording UI command.
- Do not remove or rewrite the classic A9/iLnkP2P provider.

## Checklist

- [x] Add `Vue990CameraProvider`.
- [x] Add separate Vue990 settings property.
- [x] Add Vue990 settings page/view model.
- [x] Add Vue990 provider DI registration.
- [x] Add Vue990 add-device card.
- [x] Add provider and view model tests.
- [x] Run focused tests.
- [x] Update `realtests-log.md`.

## Outcome - 2026-05-30

Added a standalone app provider:

- `src/BodyCam/Services/Camera/A9/Vue990/Vue990CameraProvider.cs`
- Provider id: `vue990-camera`
- Display name: `Vue990 Camera`
- Keeps the classic `A9CameraProvider` untouched.
- Calls `A9Vue990DirectCaptureClient` with `MaxFrames = 1`,
  `CaptureImage = true`, and `CaptureVideo = false`.
- Reads `managed-direct-still.jpg` from the direct-capture result and returns
  those JPEG bytes from `CaptureFrameAsync`.
- Saves provider diagnostic artifacts under the app cache directory rather
  than under `.my`.

Added app integration:

- Registered `IA9Vue990DirectCaptureClient`.
- Registered `Vue990CameraProvider` as an `ICameraProvider`.
- Added separate persisted setting `Vue990CameraIp`.
- Added `Vue990CameraSettingsViewModel`.
- Added `Vue990CameraSettingsPage`.
- Added an **Add Vue990 Camera** card in Add Devices.
- Registered the Vue990 settings route in `AppShell`.

Verification:

- `dotnet test src\BodyCam.Tests\BodyCam.Tests.csproj --filter "Vue990CameraProvider|Vue990CameraSettings|AddDevices"`
  passed `18/18`.
- `dotnet build src\BodyCam\BodyCam.csproj -f net10.0-windows10.0.19041.0`
  passed.
- `dotnet build tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj` passed.
- `dotnet build src\BodyCam.RealTests\BodyCam.RealTests.csproj` did not run to
  compile because restore failed on an existing target-framework mismatch:
  RealTests requested `net10.0-ios26.2` while `BodyCam` supports
  `net10.0-ios26.5`.

Known remaining check:

- The provider is wired and unit-tested, but the BodyCam UI path has not yet
  been run against the live camera. The underlying direct capture path was
  already proven live in Phase 49.
