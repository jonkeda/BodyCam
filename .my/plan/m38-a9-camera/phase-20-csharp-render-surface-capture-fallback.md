# Phase 20 - C# Render Surface Capture Fallback

**Status:** Planned

## Goal

Create a second still-image path if the Phase 16 vendor screenshot API fails.

The alternate idea is to let the VeePai player keep decoding the stream, but
render into a C# owned Android surface and copy one visible frame with Android
render APIs.

## Why This Phase Exists

Phase 19 made the first capture attempt buildable in C#:

- C# owns the PPCS/player run sequence.
- `AppPlayerApi.screenshot(...)` is exposed through generated bindings.
- Capture mode is explicit via the `capture_image` intent flag.

That should be the first live attempt because it is closest to Vue990's own
path. If it returns `false`, creates no file, or creates an invalid file, the
next safest route is not to reverse-engineer the raw channel yet. It is to read
back the rendered output that the vendor player already produces.

## Approach

Use the same proven session:

1. Fetch camera status from `192.168.168.1:81`.
2. Create/connect/login the VStarcam PPCS client.
3. Open live channel `1` with
   `livestream.cgi?streamid=10&substream=0&`.
4. Start `AppPlayerApi` with a C# owned render target.
5. Wait for draw metadata, currently `640x480`.
6. Copy one rendered frame from the surface.
7. Save it as a JPEG or PNG in the phase capture directory.
8. Pull it by ADB and record bytes, dimensions, and SHA-256.

## Candidate Implementations

### Option A - TextureView Bitmap

- Add a small hidden or visible `TextureView` to `MainActivity`.
- Create the VeePai player with the `Surface` from the `TextureView`.
- After draw metadata, call `TextureView.GetBitmap(width, height)` from C#.
- Save the bitmap through `Bitmap.Compress(...)`.

Pros:

- Simple C# ownership of the saved image.
- Does not depend on the vendor screenshot helper.

Risks:

- The player may require a visible or attached surface.
- Timing must wait until the surface is available and a frame has rendered.

### Option B - PixelCopy

- Render the player into a `SurfaceView` or window-backed surface.
- Use Android `PixelCopy.Request(...)` to copy the frame into a `Bitmap`.
- Save the bitmap from C#.

Pros:

- More reliable for hardware-rendered surfaces on modern Android.

Risks:

- Requires API-level-specific callback handling and a live attached surface.
- Slightly more UI complexity than `TextureView.GetBitmap`.

### Option C - ImageReader Surface

- Create an `ImageReader` surface and pass it to the VeePai player.
- Read one image buffer from C# and encode it.

Pros:

- Keeps the render target off-screen.

Risks:

- Many native players do not support rendering to `ImageReader` surfaces.
- Pixel format negotiation may fail silently.

## Preferred Order

1. Try Phase 16 / Phase 19 `AppPlayerApi.Screenshot(...)`.
2. If that fails, try `TextureView.GetBitmap(...)`.
3. If the texture path is blank or unsupported, try `PixelCopy`.
4. Keep raw channel parsing for Phase 18 unless the render paths both fail.

## Implementation Checklist

- [ ] Run Phase 19 capture while the phone is on `@MC-0025644`.
- [ ] If the vendor screenshot path fails, record the exact return value and
      file result.
- [ ] Add a C# owned `TextureView` capture surface to the Android probe.
- [ ] Start the existing C# PPCS/player session against that surface.
- [ ] Wait for draw metadata and one render tick.
- [ ] Save one bitmap from `TextureView.GetBitmap(...)`.
- [ ] Verify bytes, dimensions, and SHA-256.
- [ ] Pull the image over ADB into `.my/plan/m38-a9-camera/captures/phase-20/`.
- [ ] If TextureView fails, implement the PixelCopy variant.
- [ ] Add a hardware-gated RealTest for the fallback capture path.
- [ ] Update the log and report with the high-level outcome.

## Acceptance Criteria

- Capture remains explicit and opt-in.
- No feed bridging, background recording, or continuous capture is added.
- The fallback can save one still image from the live camera stream without
  relying on `AppPlayerApi.Screenshot(...)`.
- The image is verified by byte count, dimensions, and SHA-256.

## Current Blocker

The Samsung phone must be connected to the camera AP `@MC-0025644` with a
`192.168.168.x` address before either the Phase 19 screenshot path or this
fallback path can be run against the real camera.
