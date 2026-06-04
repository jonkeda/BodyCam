# RCA-04: Close Camera Surface After Sub-Button Click

## Problem

After the user taps a camera action sub-button such as `Overview`, `Summary`,
`Detail`, `Full`, or `Scan`, the camera preview and sub-button panel remain
open.

Expected behavior: after a sub-button is clicked, the app should immediately
remove the camera action buttons. The selected sub-button should still capture
the frame, close the camera preview when capture is finished, add the captured
still to the transcript, and continue running the command/result flow in the
transcript.

## Previous Implementation

`ExecuteCameraActionVariantAsync(...)` previously did this:

1. Ensures the inline camera preview is visible with
   `RevealInlineCameraPreviewAsync()`.
2. Captures a frame.
3. Adds the captured still transcript entry.
4. Adds an AI busy row.
5. Runs the selected command with the captured frame.
6. Applies the command result.

It did not clear the camera UI state after the sub-button is selected:

- `ShowInlineCameraPreview` remains true.
- `ActiveCameraAction` remains set.
- `ActiveCameraActionVariants` remains populated.
- `HasActiveCameraActionVariants` remains true.

As a result, the camera preview and sub-button row continue to render while the
command is running and after the command completes.

## Root Cause

The M50 sub-button flow was implemented as an execution path, but it does not
include a matching UI cleanup step.

There is already a `HideInlineCameraPreview()` helper, but it only hides the
preview/snapshot state and stops the camera preview. It does not clear the
selected camera action or its variant list.

## Fix Direction

After a sub-button click starts frame capture, close the camera action buttons
without unmounting the native camera view:

1. Start frame capture while the preview is still available.
2. Immediately clear the visible camera action button state.
3. Clear camera action selection state:
   - `ActiveCameraAction = null`
   - `ActiveCameraActionVariants.Clear()`
   - notify `HasActiveCameraActionVariants`
4. Keep `ShowInlineCameraPreview = true` while the capture task finishes so
   the MAUI camera control keeps its `PlatformView`.
5. Stop the native preview after the capture task completes, before hiding the
   inline camera preview.
6. Add the captured still to the transcript.
7. Continue command execution with the already-captured frame.

This should happen before waiting for a successful frame capture. If frame
capture fails, the action buttons are already closed and the app still shows
the "Camera not available or no frame captured" transcript message.

## Suggested Helper

Add one helper that clears the M50 camera action selection, and keep the
preview close as a separate step:

```csharp
private void ClearCameraActionSelection()
{
    foreach (var action in CameraActions)
        action.IsActive = false;

    ActiveCameraAction = null;
    ActiveCameraActionVariants.Clear();
    OnPropertyChanged(nameof(HasActiveCameraActionVariants));
}

private void CloseCameraActionSurface()
{
    ClearCameraActionSelection();
    HideInlineCameraPreview();
}
```

Use this from `ExecuteCameraActionVariantAsync(...)` after the captured frame
task completes. Before awaiting the captured frame, clear only the action
selection state so neither button row remains visible while the camera control
stays mounted.

## Implemented Fix

`ExecuteCameraActionVariantAsync(...)` now requests frame capture, clears the
camera action selection immediately, then awaits the frame. This hides both
the top-level action row and the variant row while leaving the native
`CameraView` mounted until capture finishes. The selected command continues
with the captured frame, and the camera preview closes after capture settles.

This was corrected after the immediate preview hide caused
`PlatformView cannot be null here` from the MAUI camera control.

The sub-button flow now hides the button rows before capture work begins, keeps
the inline preview visible during capture, and prefers capturing from the
visible inline `CameraView` when it exists. If there is no visible inline
preview, it falls back to `CameraManager`.

For the active phone provider path, starting and stopping the inline preview
now goes through the provider when possible so the provider's started state does
not drift away from the native camera state. The direct `CameraView` capture
helper also waits for the MAUI platform view before subscribing to
`MediaCaptured` and calling `CaptureImage`.

The sub-button exception handler now logs the raw exception but does not put
internal exception text into the transcript. Capture-time exceptions show
`Camera capture failed.` instead of `Command error: {ex.Message}`.

When frame capture fails, the surface is already closed before the
`Camera not available or no frame captured.` transcript message is added.

`CloseCameraActionSurface()` clears active state from all top-level camera
actions, clears `ActiveCameraAction`, empties `ActiveCameraActionVariants`,
notifies the variant state, and hides/stops the inline camera preview.

Coverage was added for hiding the action rows while frame capture is still
pending, successful sub-button execution, failed frame capture, and preventing
raw platform exceptions from leaking into the transcript.

## Files to Check

- `src/BodyCam/ViewModels/MainViewModel.cs`
- `src/BodyCam.Tests/ViewModels/MainViewModelCameraButtonsTests.cs`

## Verification

- Open the camera action surface.
- Select `Look`.
- Tap `Overview`.
- Confirm the captured still appears in the transcript.
- Confirm the top-level and sub-button rows close immediately.
- Confirm the camera preview closes after capture finishes, without a
  `PlatformView cannot be null here` error.
- Confirm the AI busy/result row continues in the transcript.
- Repeat for `Read`, `Find`, and `Scan`.
