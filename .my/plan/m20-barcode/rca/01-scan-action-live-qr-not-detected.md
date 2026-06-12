# RCA - Scan Action Live QR Not Detected

**Date:** 2026-06-12
**Area:** M20 barcode / Phase 6 live auto-scan
**Status:** Open

---

## Incident

The user opened the camera scan flow and presented a QR code image in front of
the laptop camera. The top-level Scan action did not detect the QR code
automatically. When the user clicked the Scan sub-action button, the app took a
still image and recognized the QR code successfully.

---

## Reproduction

1. Click the Scan action.
2. Show an image with a QR code in front of the laptop camera.
3. Observe that the live camera scan does not pick it up.
4. Click the Scan sub-action button.
5. Observe that the app captures an image and recognizes the QR code.

---

## Expected Behavior

After clicking Scan and opening the inline camera preview, the camera should
actively look for visible QR codes or barcodes and automatically process a stable
detection. The user should not need to click the Scan sub-action when the code is
already visible in the preview.

---

## Actual Behavior

The live preview did not auto-detect the QR code. Manual still capture through
the Scan sub-action did detect the same QR code.

---

## Likely Root Cause

The live auto-scan loop is started too early in the preview lifecycle.

Current flow:

1. `ActivateCameraActionAsync` calls `RevealInlineCameraPreviewAsync`.
2. `RevealInlineCameraPreviewAsync` sets `ShowInlineCameraPreview = true`.
3. The `ShowInlineCameraPreview` setter immediately calls `StartLiveBarcodeScan`.
4. `StartLiveBarcodeScan` checks `_cameraManager.Active`.
5. If the active camera provider is null or not yet available, it returns.
6. The camera preview/provider is started after that.
7. The live scanner is not retried, so no background frame decoding runs.

The manual Scan sub-action follows a different path:

1. `ExecuteCameraActionVariantAsync` calls `RevealInlineCameraPreviewAsync`.
2. It then calls `CaptureFrameAndCloseCameraActionSurfaceAsync`.
3. Capture uses `CaptureFrameForCameraActionAsync`.
4. If the inline camera preview is visible, it captures directly from
   `CameraView` via `CaptureFrameFromCameraViewAsync`.
5. The still frame is passed to `ScanCommand`, and QR recognition succeeds.

This explains why manual still capture works while live auto-scan does not.

---

## Contributing Factors

- `StartLiveBarcodeScan` depends on `CameraManager.Active` already being
  initialized and available.
- `RevealInlineCameraPreviewAsync` flips the UI state before starting the
  provider or native preview.
- There is no retry after `StartLiveBarcodeScan` returns because no active
  provider is available.
- The phone camera live path uses provider frame streaming, while the successful
  manual path uses direct `CameraView` still capture.
- There is no visible "auto-scan running" / "auto-scan unavailable" state, so
  the user cannot tell whether the background scanner is active.

---

## Recommended Fix

Start live auto-scan only after the camera provider or `CameraView` preview is
ready.

Recommended implementation:

1. Remove the direct `StartLiveBarcodeScan` call from the
   `ShowInlineCameraPreview` setter, or make it only schedule startup.
2. Call `StartLiveBarcodeScan` at the end of `RevealInlineCameraPreviewAsync`
   after the provider/native preview has started.
3. Let `CameraManager.StreamFramesAsync` perform fallback-to-phone if no active
   provider exists, instead of pre-checking `_cameraManager.Active` in
   `StartLiveBarcodeScan`.
4. If stream startup fails because the camera is not ready, retry briefly or
   surface a debug status.
5. Add a test that clicking/activating Scan starts live detection even when the
   active provider is selected only after preview reveal.

Safer shape:

```csharp
private void StartLiveBarcodeScan()
{
    if (_liveBarcodeScanner is null || _liveBarcodeScanCts is not null)
        return;

    _liveBarcodeScanCts = new CancellationTokenSource();
    _ = WatchLiveBarcodesAsync(_liveBarcodeScanCts.Token);
}
```

Then rely on:

```csharp
var frames = _cameraManager.StreamFramesAsync(ct);
```

to select/fallback the active camera provider.

---

## Verification Plan

- Unit test: live scan starts when Scan action reveals preview and active camera
  provider is initially null.
- Unit test: `StartLiveBarcodeScan` does not permanently give up when provider
  availability is delayed.
- Manual test: click Scan, hold QR image in front of laptop camera, verify
  transcript/scan result appears without clicking the sub-action.
- Regression test: click Scan sub-action, verify still capture scan continues to
  recognize the QR code.

---

## Current Workaround

Click the Scan sub-action button. This forces a still-frame capture and routes
the image through the existing scan command path, which recognizes the QR code.
