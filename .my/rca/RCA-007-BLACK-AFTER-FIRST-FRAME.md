# RCA-007: Camera Preview Black After First Frame

**Status:** ANALYZED  
**Severity:** Critical — camera preview shows 1 frame then goes permanently black  
**Reported:** Image appears for ~1 second then goes completely dark  
**Supersedes:** RCA-006 (fixes from RCA-006 were necessary but insufficient)

---

## Symptoms

1. Camera preview in the 160×120 overlay shows one valid frame
2. After ~1 second, preview goes permanently black
3. Vision API receives null or black JPEG data from that point forward

---

## Root Causes

### RC-1: Wrong capture API for repeated frame grabs

**File:** `src/BodyCam/Platforms/Windows/WindowsCameraService.cs` — `CaptureFrameAsync`

`CapturePhotoToStreamAsync` is designed for **single-shot photo capture**, not repeated frame grabs. Internally it:
- Activates the full photo pipeline (exposure, focus, flash evaluation)
- Captures one high-res still
- Tears down internal state after the capture completes

When called in a tight loop (every 500ms from preview + every 5s from vision agent), the pipeline enters a broken state after the first capture. Subsequent calls either return 0 bytes or throw silently, caught by the `catch { /* non-fatal */ }` in `RefreshPreviewLoopAsync`.

**The correct API** is `MediaCapture.StartPreviewAsync()` + `MediaCapture.GetPreviewFrameAsync()`:
- `StartPreviewAsync()` starts continuous video streaming from the sensor
- `GetPreviewFrameAsync()` grabs a single `VideoFrame` from the live stream — lightweight, no pipeline teardown

### RC-2: No native preview surface — entire approach is wrong for live video

**File:** `src/BodyCam/MainPage.xaml` + `MainViewModel.cs`

The current approach:
```
loop every 500ms:
    CapturePhotoToStreamAsync → byte[] → ImageSource.FromStream → bind to Image
```

This is fundamentally wrong for live video because:
1. `ImageSource.FromStream` creates a new image source object each iteration
2. MAUI's Image control must decode JPEG → bitmap on the UI thread each time
3. The old `ImageSource` is not deterministically disposed — GDI/bitmap handle leak
4. At 2fps this works briefly; by frame 2-3 the accumulated handles or the MediaCapture pipeline state cause failure

**The correct approach** for MAUI is `CommunityToolkit.Maui.Camera`:
- `CameraView` provides a **native preview surface** (CaptureElement on WinUI, Camera2 SurfaceView on Android)
- Zero-copy rendering — frames go directly from the camera sensor to the GPU surface
- `CameraView.CaptureImage()` grabs a single still for vision API use
- Cross-platform (Windows, Android, iOS)

### RC-3: Concurrent capture race condition

**File:** `src/BodyCam/Platforms/Windows/WindowsCameraService.cs`

The semaphore added in the last fix helps, but `CapturePhotoToStreamAsync` is not designed to be called concurrently even with serialization. The internal MediaCapture state machine expects: Initialize → CapturePhoto → *done*. Rapid sequential calls violate this contract.

---

## Recommended Fix: Replace with CommunityToolkit.Maui.Camera

### Why CommunityToolkit.Maui.Camera

| Concern | Current approach | CameraView |
|---|---|---|
| Live preview | Manual JPEG decode loop | Native GPU surface (zero-copy) |
| Frame capture | `CapturePhotoToStreamAsync` (breaks) | `CaptureImage()` (designed for repeat use) |
| Platform support | Separate WindowsCameraService + AndroidCameraService | Single `CameraView`, cross-platform handlers built-in |
| Frame rate | ~2fps (JPEG encode+decode+bind) | 30fps native preview |
| Memory | Leaks ImageSource/bitmap handles | Native surface, no managed allocations |
| Code complexity | ~200 lines across 2 platform services + preview loop | ~20 lines of XAML + capture call |

### Implementation Plan

1. **Add NuGet:** `CommunityToolkit.Maui.Camera`
2. **Register in MauiProgram.cs:** `.UseMauiCommunityToolkitCamera()`
3. **Replace XAML preview:** Replace `Image` bound to `CameraPreview` with `<toolkit:CameraView x:Name="Camera" />`
4. **Simplify ICameraService:** Reduce to `CaptureFrameAsync` only (preview handled natively). Or expose the `CameraView` directly to the orchestrator for frame grabs.
5. **VisionAgent frame capture:** Use `CameraView.CaptureImage()` → `Stream` → `byte[]` for vision API calls
6. **Delete:** `WindowsCameraService.cs`, `AndroidCameraService.cs`, `RefreshPreviewLoopAsync`, `CameraPreview` property
7. **Update tests:** Mock-based tests unchanged (they mock `ICameraService`); remove platform-specific camera tests

### Alternative: Raw MediaCapture with GetPreviewFrameAsync (Windows-only)

If avoiding the NuGet dependency is preferred:

```csharp
// In StartAsync — start continuous video preview (no CaptureElement needed)
await _capture.StartPreviewAsync();

// In CaptureFrameAsync — grab a lightweight preview frame
var videoFrame = new VideoFrame(BitmapPixelFormat.Bgra8, 512, 512);
await _capture.GetPreviewFrameAsync(videoFrame);
// Convert SoftwareBitmap to JPEG bytes
```

This fixes the pipeline breakage but still requires the manual `ImageSource` loop for UI preview (suboptimal). The Android side would need a parallel Camera2 fix.

---

## Impact

- Preview: Permanent fix — native surface never goes black
- Vision: Reliable frame capture via `CaptureImage()`
- Performance: 30fps native preview vs 2fps JPEG loop
- Memory: Eliminates ImageSource handle leak
- Code: Net deletion of ~200 lines of platform-specific camera code
