# Design: Camera Capture for Quick-Action Buttons

## Problem

The Look/Read/Find/Photo buttons need a camera frame to send to the Vision AI.
Currently all capture goes through `CameraView.CaptureImage()`, which requires:
- The CameraView to be **in the visual tree**
- The camera preview to be **started** (`StartCameraPreview`)

This means when the user is on the transcript tab (camera not visible) or the
session is not active (camera not started), frame capture silently returns null.

## Current Architecture

```
Button tap
  → CaptureFrameFromCameraViewAsync()
    → CameraView.CaptureImage(ct)          ← requires preview running
    → CameraView.MediaCaptured event       ← returns JPEG bytes
```

**Single path, single dependency:** everything goes through CommunityToolkit.Maui
`CameraView`, a UI control that must be visible and started.

## Available Alternatives

| Approach | How | Pros | Cons |
|----------|-----|------|------|
| **CameraView.CaptureImage** (current) | Call `CaptureImage()` on the toolkit control | Already implemented, returns JPEG bytes, zero extra deps | Requires preview started + control visible |
| **MAUI MediaPicker** | `MediaPicker.Default.CapturePhotoAsync()` | Built into MAUI, no preview needed, works headless | Opens system camera UI — **blocks the app with a full-screen camera**, not suitable for quick capture |
| **Platform MediaCapture (Windows)** | Use `Windows.Media.Capture.MediaCapture` directly | Full control, no preview needed, can capture in background | Windows-only, significant native code, must handle device lifecycle |
| **Platform CameraX (Android)** | Use `AndroidX.Camera.Core` with ImageCapture use case | No preview needed, modern API | Android-only, requires camera permission flow, complex setup |
| **Start preview off-screen** | Call `StartCameraPreview` even when CameraView is hidden (IsVisible=false) | Minimal code change | CommunityToolkit may skip initialization for invisible views; unreliable |

### Ruling out MediaPicker

`MediaPicker.CapturePhotoAsync()` launches the **system camera activity/window**.
This is a full-screen, user-interactive capture — the user must frame and tap a
shutter button. This defeats the purpose of a quick "Look" action.

### Ruling out full native implementations (for now)

Platform-specific `MediaCapture` (Windows) and `CameraX` (Android) would give
headless capture but require substantial native code per platform. The existing
`ICameraService` stubs are placeholders for this, but implementing them is a
larger effort better suited for a dedicated milestone.

## Recommended Design

**Dual-path capture based on camera state:**

```
Button tap
  ├─ Camera preview IS running?
  │   → (a) Use CameraView.CaptureImage() — fast, already works
  │
  └─ Camera preview NOT running?
      → (b) Start preview, capture, then stop preview
```

### Path A: Preview is running (camera tab shown OR active session)

Use the existing `CaptureFrameFromCameraViewAsync()` directly. The CameraView is
already streaming frames, so `CaptureImage()` returns immediately.

### Path B: Preview is not running (transcript tab, no session)

1. Call `_cameraView.StartCameraPreview()` — spins up the hardware
2. Small delay (`Task.Delay(500ms)`) — camera sensors need warm-up time to
   produce a valid first frame (otherwise the capture returns a black/corrupt image)
3. Call `CaptureFrameFromCameraViewAsync()` — grab the frame
4. Call `_cameraView.StopCameraPreview()` — release the hardware

**Why this works:** The CameraView is always in the visual tree (it's in the Grid
on Row 1 of MainPage.xaml). It's just toggled via `IsVisible`. The
CommunityToolkit CameraView initializes its native handler when added to the
tree, not when made visible. So `StartCameraPreview()` should work even when
the parent Grid has `IsVisible=false`.

**Risk:** Some platforms may skip native initialization for invisible views. If
this is the case, we can set `Opacity="0"` instead of `IsVisible="false"` as a
workaround (the view remains in layout but is transparent).

### Implementation

```csharp
private async Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
{
    if (_cameraView is null) return null;

    var wasPreviewRunning = _cameraView.IsAvailable; // or track via field

    if (!wasPreviewRunning)
    {
        await _cameraView.StartCameraPreview(ct);
        await Task.Delay(500, ct); // warm-up
    }

    try
    {
        return await CaptureFrameFromCameraViewAsync(ct);
    }
    finally
    {
        if (!wasPreviewRunning)
            _cameraView.StopCameraPreview();
    }
}
```

### Full Button Flow

```
Button tap (Look/Read/Find/Photo)
  │
  ├─ Session IS running (IsRunning == true)?
  │   → Send text through Realtime API (Option B from RCA)
  │   → AI calls describe_scene tool → captures frame → speaks response
  │
  └─ Session NOT running?
      → CaptureFrameAsync() — starts preview if needed
      → Show captured image in transcript (TranscriptEntry.Image)
      → VisionAgent.DescribeFrameAsync(frame, prompt)
      → Show AI description in transcript (text only, no voice)
      → Switch to transcript tab
```

## Future Work

- **ICameraService**: Implement the existing `ICameraService` stubs with native
  `MediaCapture` (Windows) / `CameraX` (Android) for true headless capture.
  This removes the CameraView dependency entirely for non-preview captures.
- **Frame caching**: Cache the last N frames from the preview stream so
  "Look" can respond instantly without waiting for a new capture.
