# RCA-06: Camera Button Regression Review

## Scope

This is a review of the M50 camera button changes from RCA-01 through RCA-05.
It is not another implementation patch.

The user-visible symptoms were:

- Camera action buttons appeared at startup when they should not.
- Selecting `Look`, `Find`, `Read`, or `Scan` did not consistently show the
  expected second-level buttons.
- Both the top-level and second-level button rows were visible at the same time.
- After tapping a second-level button, the camera panel did not close.
- A later attempt produced `PlatformView cannot be null here`.
- The raw exception was added to the transcript.
- After changing the transcript handling, the app still reported
  `Camera capture failed.`

## Review Findings

### 1. High: `CameraView` has more than one lifecycle owner

`MainViewModel` still starts, stops, and captures from the MAUI `CameraView`
directly:

- `RevealInlineCameraPreviewAsync()` calls `_cameraView.StartCameraPreview(...)`.
- `HideInlineCameraPreview()` calls `StopInlineCameraPreview()`.
- `CaptureFrameFromCameraViewAsync()` subscribes to `MediaCaptured` and calls
  `_cameraView.CaptureImage(...)`.

At the same time, `PhoneCameraProvider` also owns the same `CameraView`:

- `StartAsync()` starts preview.
- `StopAsync()` stops preview.
- `CaptureFrameAsync()` may start preview, capture, and stop preview.

This means the view model and provider can disagree about whether the native
camera view is mounted, started, stopping, or ready. The current sub-button path
now captures through `CameraManager`, but the visible preview is still started
directly by the view model. That makes `_started` inside `PhoneCameraProvider`
an unreliable source of truth.

This is the most important regression source.

### 2. High: UI cleanup is interleaved with native capture

`ExecuteCameraActionVariantAsync(...)` currently calls:

```csharp
var frame = await CaptureFrameAndCloseCameraActionSurfaceAsync(ct);
```

That helper starts capture, clears selection immediately, then hides/stops the
preview in `finally`:

```csharp
var frameTask = CaptureFrameForCameraActionAsync(ct);
ClearCameraActionSelection();

try
{
    return await frameTask;
}
finally
{
    HideInlineCameraPreview();
}
```

This is better than hiding the preview before capture starts, but it still has
the wrong shape for a native camera control. The UI state and the native camera
lifecycle are being updated from the same method without an explicit state
machine. With MAUI, changing visibility or stopping preview at the wrong moment
can detach or invalidate the native `PlatformView` while capture is still being
prepared or completed.

The first `PlatformView cannot be null here` failure was almost certainly caused
by this lifecycle race: the app changed or closed the camera surface while the
camera control still needed its native platform view for capture or stop.

### 3. High: the tests do not cover the failing platform behavior

The added unit tests cover the view-model state contract:

- startup button visibility
- selecting actions
- showing variants
- hiding rows while capture is pending
- successful frame reuse
- friendly transcript text on a simulated exception

They do not run a MAUI `CameraView` with a real `Handler.PlatformView`. The test
that mentions `PlatformView cannot be null here` injects an exception through a
delegate, so it only proves that the transcript hides raw exception text. It
does not prove that the native handler race is fixed.

This is why tests could pass while the app still failed.

### 4. Medium: `WaitForPlatformViewAsync` masks the race instead of fixing it

`PhoneCameraProvider.WaitForPlatformViewAsync(...)` polls the handler for up to
500 ms:

```csharp
const int attempts = 10;
await Task.Delay(50, ct);
```

If the platform view is not ready, capture returns `null` and the transcript can
show `Camera not available or no frame captured.` or `Camera capture failed.`

That avoids one raw exception path, but it does not solve ownership. It also
turns a lifecycle bug into a user-facing capture failure.

### 5. Medium: the transcript error is added by the view model

The error message did not come from the AI model. It was added by
`ExecuteCameraActionVariantAsync(...)`.

When capture fails before an AI busy row exists, `aiEntry` is null and the catch
block adds this transcript entry:

```csharp
new TranscriptEntry { Role = "AI", Text = CameraActionCaptureFailedMessage }
```

So `AI: Camera capture failed.` is application error handling rendered as an AI
transcript row. That is confusing because it looks like an AI response.

### 6. Medium: RCA-05 is a secondary issue, not the first failure

RCA-05 explains why pressing the sub-button twice can produce duplicate failure
rows. That can happen, but it is not why the first capture failed.

The first failure happened because capture itself was unstable. Double-tap
handling should still be fixed, but it should not distract from the camera
lifecycle problem.

### 7. Medium: the visible preview source and capture source may not match

The sub-button path now captures with:

```csharp
return await _cameraManager.CaptureFrameAsync(ct);
```

The visible preview, however, is still the page's phone `CameraView`. If the
active camera provider is not the phone provider, the captured frame may come
from a different source than the visible preview.

That is a design decision, but it must be explicit. Either the action surface
previews the active provider, or the capture source is always the visible phone
preview.

## Recommended Fix

Do not add another small patch to `ExecuteCameraActionVariantAsync(...)`.
Refactor the camera action flow around one lifecycle owner.

Recommended direction:

1. Create one camera surface coordinator, for example
   `ICameraActionCaptureCoordinator`.
2. Move all direct `CameraView` operations out of `MainViewModel`.
3. Let the coordinator/provider be the only code that starts preview, captures,
   waits for handler readiness, and stops preview.
4. Model explicit states:
   - `Closed`
   - `OpeningPreview`
   - `PreviewingTopLevelActions`
   - `PreviewingVariants`
   - `Capturing`
   - `Closing`
5. On sub-button tap:
   - claim the action immediately
   - hide or disable the variant buttons immediately
   - keep the native camera view mounted until the capture callback resolves
   - then stop preview and close the panel
   - add the captured still and run the command with that exact frame
6. Add a short tap debounce or command `CanExecute` guard for variants.
7. Keep transcript error messages user-friendly, but do not render app errors
   as ordinary AI responses without distinguishing them.

## Test Plan Needed

The next test pass should include:

- View-model state tests for the explicit state machine.
- A fake camera surface adapter that can simulate:
  - handler not ready
  - handler becoming ready
  - capture event arriving after UI buttons are hidden
  - stop requested while handler is null
  - double tap while capture is pending
- A manual or automated MAUI device test for the real `CameraView`:
  - open camera action surface
  - select each top-level action
  - tap each sub-button once
  - tap a sub-button twice
  - verify no raw platform exception appears
  - verify exactly one captured still and one command result appear

## Current Status

The current code is improved for button visibility and variant consistency, but
the capture/close behavior is not reliable enough yet.

The highest-priority fix is to remove split ownership of the `CameraView`
lifecycle. Until that is done, the app can keep producing `PlatformView` races
or friendly capture-failure transcript rows even when the UI state tests pass.
