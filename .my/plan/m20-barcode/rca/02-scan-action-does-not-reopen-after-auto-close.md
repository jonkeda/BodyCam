# RCA - Scan Action Does Not Reopen After Auto Close

**Date:** 2026-06-12
**Area:** M20 barcode / Phase 6 live auto-scan
**Status:** Implemented

---

## Incident

After the first live barcode/QR detection, the app closes the camera action
surface as intended. On the next attempt, clicking Scan again does not reliably
show the camera preview. Clicking the action again can leave the Scan sub-action
button unavailable or visually disabled.

---

## Reproduction

1. Click the Scan action.
2. Present a QR code or barcode to the laptop camera.
3. Wait for live auto-scan to detect it.
4. Observe that the camera preview/action surface closes.
5. Click Scan again.
6. Observe that the preview does not show reliably.
7. Click the action again.
8. Observe that the Scan sub-action button is not enabled or not usable.

---

## Expected Behavior

After a successful auto-scan closes the camera action surface, the next Scan
click should start from a clean state:

1. Scan action becomes active.
2. Inline camera preview becomes visible.
3. Native camera preview/provider is started.
4. Scan sub-action is visible and executable.
5. Live auto-scan loop is started again.

---

## Actual Behavior

The first auto-scan closes the visible surface, but the next Scan activation can
land in a partially reset state. The UI no longer has a clear single source of
truth for whether the camera action surface is closed, opening, or open.

---

## Likely Root Cause

The camera action flow has split lifecycle ownership.

There are several related states that should transition together:

- `ShowInlineCameraPreview`
- native `CameraView` preview state
- `CameraManager.Active` provider state
- `_liveBarcodeScanCts`
- `ActiveCameraAction`
- `ActiveCameraActionVariants`
- each `CameraActionItemViewModel.IsActive`
- `_isHandlingLiveBarcodeDetection`
- `_isExecutingCameraActionVariant`

Today those states are changed from multiple paths:

- `ActivateCameraActionAsync`
- `RevealInlineCameraPreviewAsync`
- `HandleLiveBarcodeDetectionAsync`
- `CloseCameraActionSurface`
- `HideInlineCameraPreview`
- `HideInlineCameraPreviewAfterCameraActionCaptureAsync`
- `StopLiveBarcodeScan`
- `StopInlineCameraPreview`
- `ClearCameraActionSelection`

The new auto-close path calls:

```csharp
CloseCameraActionSurface();
```

which clears the selected action and hides the inline preview. Hiding the preview
then cancels live scan through the `ShowInlineCameraPreview` setter. That means
a successful live scan is closing UI state, camera state, and scan-loop state
through cascading side effects rather than through one explicit lifecycle
transition.

The next click calls:

```csharp
ActivateCameraActionAsync(action)
```

which immediately repopulates `ActiveCameraActionVariants` and then calls:

```csharp
RevealInlineCameraPreviewAsync()
```

However, that reopen path assumes the previous close fully stopped and reset the
native preview/provider and live scan loop. If any part of the previous close is
still in progress, skipped, or only scheduled onto the main thread, the second
open can be blocked by stale state. The visible result is a surface where the
preview and the variant buttons disagree about whether Scan is active.

---

## Contributing Factors

- `CloseCameraActionSurface` is synchronous, but stopping native preview can be
  deferred through `MainThread.BeginInvokeOnMainThread`.
- `StopLiveBarcodeScan` cancels the live scan loop but does not await loop
  completion before the next activation can start.
- `RevealInlineCameraPreviewAsync` starts the preview, provider, and live scan,
  but it does not first force a known closed baseline.
- `ClearCameraActionSelection` and `HideInlineCameraPreview` are separate
  operations, so callers can accidentally perform only part of the close.
- The XAML uses `ShowCameraActionsSection`, `ShowCameraActionRail`, and
  `HasActiveCameraActionVariants` separately. If view-model state is partially
  reset, the preview panel, action rail, and variant rail can disagree.
- Tests currently verify that auto-scan closes the surface, but not that the
  Scan action can be opened again immediately afterward.

---

## Recommended Fix

Create one explicit camera action lifecycle instead of spreading the transition
across property setters and helper methods.

Recommended implementation:

1. Add a single async close method, for example
   `CloseCameraActionSurfaceAsync`, that:
   - cancels and awaits the live scan loop,
   - stops the native preview/provider,
   - clears action selection,
   - hides preview/snapshot state,
   - raises all dependent property changes once.
2. Use that method for both manual sub-action completion and live auto-scan
   completion.
3. Make `ActivateCameraActionAsync` begin by ensuring any previous close has
   completed, then set the selected action and reveal preview.
4. Keep `ShowInlineCameraPreview` as a UI state property only; avoid making it
   own scan-loop cancellation as a hidden side effect.
5. Add regression coverage for open -> auto-scan close -> open again.

Target regression test:

```csharp
await vm.ActivateCameraActionAsync(scanAction);
await WaitUntilAsync(() => !vm.ShowInlineCameraPreview);

await vm.ActivateCameraActionAsync(scanAction);

vm.ShowInlineCameraPreview.Should().BeTrue();
vm.ActiveCameraAction.Should().Be(scanAction);
vm.ActiveCameraActionVariants.Should().ContainSingle(v => v.Label == "Scan");
vm.HasActiveCameraActionVariants.Should().BeTrue();
```

---

## Verification Plan

- Unit test: Scan action can be activated again after live auto-scan closes the
  surface.
- Unit test: live scan cancellation does not prevent immediate restart.
- Manual test: scan a QR code, let the surface close, then press Scan again and
  verify the preview and Scan sub-action appear.
- Manual test: repeat the cycle several times to catch race conditions between
  close and reopen.

---

## Current Workaround

Restarting the app should reset the stale camera action state. If the UI is only
partially stuck, switching away from the camera tab and back may also force part
of the state to refresh, but this is not reliable.

---

## Implementation

Implemented on 2026-06-12.

- Added a camera action lifecycle lock around action activation and close.
- Tracked the live barcode scan task so reopening Scan can wait for the previous
  live scan loop to cancel before starting a new one.
- Routed live auto-scan completion through an async close path that clears the
  selected action, hides preview state, and cancels live scanning together.
- Kept the live auto-scan close path from waiting on its own scanner task.
- Added regression coverage for Scan -> auto-scan close -> Scan again.

Verification:

```powershell
dotnet test src\BodyCam.Tests\BodyCam.Tests.csproj --no-restore --filter "FullyQualifiedName~MainViewModelCameraButtonsTests|FullyQualifiedName~LiveBarcodeScannerTests"
dotnet build BodyCam.sln --no-restore
```
