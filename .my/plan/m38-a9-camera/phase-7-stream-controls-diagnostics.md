# Phase 7 - Stream Controls & Diagnostics

**Status:** Planned

## Goal

Make the A9 setup screen useful after a camera is configured by adding resolution
selection, stream status, and reliability diagnostics.

This phase should improve confidence without changing the main camera abstraction.

## UX

```markui
# A9 Camera

v------------------------------------------------------v
| Stream                                               |
| Resolution    < 640 x 480                       v >  |
|                                                      |
| Status        Streaming                              |
| Last frame    128 ms ago                             |
| Dropped       3 frames                               |
| Reconnects    1                                     |
|                                                      |
| [ Restart Stream ]                 [ Test Capture ]  |
v------------------------------------------------------v
```

## Implementation

1. Add A9 resolution setting:
   - `A9CameraResolution`
   - Supported values: `320x240`, `640x480`
2. Replace hardcoded resolution id `2` in `A9Session` with a configurable value.
3. Add stream diagnostics from `A9Session`/`A9CameraProvider`:
   - last packet time
   - last frame time
   - frames received
   - corrupt frames dropped
   - reconnect attempts
   - current stream state
4. Add `RestartStreamCommand` and `TestCaptureCommand` to the A9 setup view model.
5. Show diagnostics in a compact settings card.
6. Add structured logs for stream starts, stops, reconnects, and frame drops.
7. Keep diagnostics read-only on the main `DeviceSettingsPage`; full controls stay
   in the A9 setup page.

## Files

- `src/BodyCam/Services/ISettingsService.cs`
- `src/BodyCam/Services/SettingsService.cs`
- `src/BodyCam/Services/Camera/A9/A9Session.cs`
- `src/BodyCam/Services/Camera/A9/A9CameraProvider.cs`
- `src/BodyCam/Services/Camera/A9/A9CameraDiagnostics.cs`
- `src/BodyCam/ViewModels/Settings/A9CameraSettingsViewModel.cs`
- `src/BodyCam/Pages/Settings/A9CameraSettingsPage.xaml`
- `src/BodyCam.Tests/Services/Camera/A9/A9SessionTests.cs`
- `src/BodyCam.Tests/ViewModels/Settings/A9CameraSettingsViewModelTests.cs`

## Acceptance Criteria

- Resolution can be selected and persisted.
- A9 session uses the selected resolution when starting video.
- Restart Stream stops and restarts the provider cleanly.
- Test Capture reports frame size and success/failure.
- Diagnostics update after frames, drops, disconnects, and reconnects.
- Corrupt frames are still never surfaced to callers.
- Tests cover resolution mapping, diagnostics counters, restart, and test capture.
