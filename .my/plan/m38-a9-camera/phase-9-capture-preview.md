# Phase 9 - A9 Capture Preview

**Status:** Planned

## Goal

Add a lightweight preview/test-capture experience to the A9 setup page so users can
confirm framing before returning to the main camera workflow.

This is not a full live viewer. It is a settings-page confidence check.

## UX

```markui
# A9 Camera

v------------------------------------------------------v
| Preview                                              |
|                                                      |
|                     !==IMG==!                        |
|                                                      |
| Last capture 45,231 bytes in 420 ms                  |
|                                                      |
| [ Capture Preview ]                                  |
v------------------------------------------------------v
```

## Implementation

1. Add `CapturePreviewCommand` to `A9CameraSettingsViewModel`.
2. Use the configured/active `A9CameraProvider` to capture one frame.
3. Store preview bytes in a view-model property suitable for MAUI image binding.
4. Show capture size and latency.
5. Show clear status text when no IP is configured or capture times out.
6. Keep preview capture independent from the main `Take Picture` workflow.

## Files

- `src/BodyCam/ViewModels/Settings/A9CameraSettingsViewModel.cs`
- `src/BodyCam/Pages/Settings/A9CameraSettingsPage.xaml`
- `src/BodyCam.Tests/ViewModels/Settings/A9CameraSettingsViewModelTests.cs`
- `src/BodyCam.UITests/Pages/A9CameraSettingsPage.cs`
- `src/BodyCam.UITests/Tests/SettingsPage/A9CameraSettingsTests.cs`

## Automation IDs

- `A9CameraPreviewImage`
- `A9CameraCapturePreviewButton`
- `A9CameraPreviewStatusLabel`

## Acceptance Criteria

- Capture Preview attempts one still frame from the A9 provider.
- Successful capture renders the frame and shows byte count plus latency.
- Timeout/failure shows an in-page status without crashing.
- Preview does not change the selected global Camera Source unless explicitly
  needed by provider design.
- Unit tests cover success, timeout/failure, and blank-settings behavior.
