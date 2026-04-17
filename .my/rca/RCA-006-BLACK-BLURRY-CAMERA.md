# RCA-006: Black or Blurry Camera Preview

**Status:** ANALYZED
**Severity:** High — vision pipeline unusable if frames are black/blurry
**Reported:** Camera preview shows black image or blurry/soft frames

---

## Symptoms

1. **Black image:** Camera preview in the UI is completely black. Vision API receives a black JPEG and describes "a dark image" or "nothing visible."
2. **Blurry image:** Camera preview shows a recognizable but soft/unfocused image. Vision API descriptions are vague or inaccurate.

---

## Root Causes

### RC-1: No auto-exposure warm-up time (Black image)

**File:** `src/BodyCam/Platforms/Windows/WindowsCameraService.cs` — `StartAsync` + `CaptureFrameAsync`

`MediaCapture.InitializeAsync()` opens the camera hardware, but the sensor needs 300-500ms to adjust auto-exposure, auto-white-balance, and auto-gain. The first `CaptureFrameAsync` call can fire immediately after `StartAsync` (the preview loop starts right away at 500ms intervals, and a `describe_scene` function call has no delay).

```csharp
// StartAsync sets IsCapturing = true immediately
_initialized = true;
IsCapturing = true;
// No warm-up delay — next CaptureFrameAsync fires a black frame
```

**Evidence:** First frame from most webcams is black or very dark because the auto-exposure algorithm hasn't converged.

### RC-2: PrepareLowLagPhotoCaptureAsync called on every frame (Blurry)

**File:** `src/BodyCam/Platforms/Windows/WindowsCameraService.cs` — `CaptureFrameAsync`

`PrepareLowLagPhotoCaptureAsync` reinitializes the low-lag capture pipeline on every single call. This is expensive (~50-100ms overhead per frame) and resets internal buffering. The API docs say to prepare once and reuse for multiple captures.

```csharp
// Called EVERY time CaptureFrameAsync is invoked:
var lowLag = await _capture.PrepareLowLagPhotoCaptureAsync(
    ImageEncodingProperties.CreateJpeg());
// ... capture ...
await lowLag.FinishAsync(); // Tears down the pipeline
```

Each `FinishAsync` destroys the pipeline. The next `PrepareLowLagPhotoCaptureAsync` rebuilds it, losing the previous focus/exposure state. This causes:
- Autofocus reset between frames → blurry captures
- Increased capture latency → motion blur from delay

### RC-3: Wrong MediaCategory (Black/dim image)

**File:** `src/BodyCam/Platforms/Windows/WindowsCameraService.cs` — `StartAsync`

```csharp
MediaCategory = MediaCategory.Communications
```

`MediaCategory.Communications` tells the camera driver to optimize for video calling: lower resolution, fixed exposure, noise reduction. For still photo capture, `MediaCategory.Media` or the default is more appropriate — it allows the sensor to use its full dynamic range and resolution before downscaling.

### RC-4: Suboptimal downscale interpolation (Blurry)

**File:** `src/BodyCam/Platforms/Windows/WindowsCameraService.cs` — `CaptureFrameAsync`

```csharp
encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Linear;
```

`BitmapInterpolationMode.Linear` (bilinear) is fast but produces soft results when downscaling large images to 512×512. `BitmapInterpolationMode.Fant` is designed specifically for downscaling and produces sharper output.

---

## Fix

### 1. Prepare LowLagPhotoCapture once in StartAsync, reuse across frames

Move `PrepareLowLagPhotoCaptureAsync` to `StartAsync`. Store the `LowLagPhotoCapture` instance. Call `FinishAsync` only in `StopAsync`/`Dispose`.

```csharp
private LowLagPhotoCapture? _lowLag;

public async Task StartAsync(CancellationToken ct = default)
{
    // ... existing init ...
    _lowLag = await _capture.PrepareLowLagPhotoCaptureAsync(
        ImageEncodingProperties.CreateJpeg());

    // Warm-up: discard the first frame to let auto-exposure settle
    await _lowLag.CaptureAsync();
    await Task.Delay(300, ct);

    _initialized = true;
    IsCapturing = true;
}
```

### 2. Use Fant interpolation for downscaling

```csharp
encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
```

### 3. Change MediaCategory

```csharp
MediaCategory = MediaCategory.Media
```

### 4. Simplify CaptureFrameAsync to reuse prepared pipeline

```csharp
public async Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
{
    if (!_initialized || _lowLag is null) return null;

    var photo = await _lowLag.CaptureAsync();
    var frame = photo.Frame;
    // ... resize as before ...
}
```

---

## Impact

- **RC-1 fix:** Eliminates black first-frame by discarding warm-up capture + adding 300ms delay
- **RC-2 fix:** Eliminates per-frame autofocus reset, reduces capture latency from ~150ms to ~20ms
- **RC-3 fix:** Allows full dynamic range, better auto-exposure behavior
- **RC-4 fix:** Sharper downscaled images, better vision API accuracy
