# Phase 13 - Public A9 Camera API

**Status:** Planned

## Goal

Expose a clean .NET API matching the shape requested in `pmpt.md` while keeping
BodyCam integrated through `ICameraProvider`.

The API should be usable from MAUI, WPF, console apps, tests, and the BodyCam
settings flow.

## Target Shape

```csharp
var devices = await A9Camera.DiscoverAsync();
var cam = await A9Camera.ConnectAsync(devices.First());

await foreach (var frame in cam.GetFramesAsync())
{
    // use decoded frames
}
```

For direct-stream and H.264-capable variants, expose stream access as well:

```csharp
await using var cam = await A9Camera.ConnectAsync(device);
var streamUri = cam.StreamUri;
await foreach (var nalUnit in cam.GetH264NalUnitsAsync())
{
    // pass to custom decoder
}
```

## API Surface

- `A9Camera.DiscoverAsync(...)`
- `A9Camera.ConnectAsync(...)`
- `A9CameraDevice`
- `IA9CameraConnection`
- `IA9CameraConnection.StreamUri` for RTSP/HTTP MJPEG variants
- `IA9CameraConnection.GetFramesAsync(...)`
- `IA9CameraConnection.GetH264NalUnitsAsync(...)` for H.264 variants
- `IA9CameraConnection.CaptureJpegAsync(...)` for MJPEG variants

## Implementation

1. Wrap the existing discovery/session/provider pieces in a small facade.
2. Normalize discovered devices across direct RTSP, direct HTTP MJPEG,
   V720/Naxclow, UDP/MJPEG, and TCP/H.264 variants.
3. Keep advanced settings available through options objects rather than long
   parameter lists.
4. Preserve `ICameraProvider` as BodyCam's runtime abstraction.
5. Add sample usage to the plan docs or test fixtures.
6. Add tests proving the facade can connect to fake RTSP, HTTP MJPEG,
   V720/Naxclow, UDP/MJPEG, and H.264 variants.

## Files

- `src/BodyCam/Services/Camera/A9/A9Camera.cs`
- `src/BodyCam/Services/Camera/A9/A9CameraDevice.cs`
- `src/BodyCam/Services/Camera/A9/IA9CameraConnection.cs`
- `src/BodyCam.Tests/Services/Camera/A9/A9CameraFacadeTests.cs`

## Acceptance Criteria

- `A9Camera.DiscoverAsync()` returns devices with UID, IP address, port, stream
  URL where available, and protocol variant.
- `A9Camera.ConnectAsync(device)` selects the correct session implementation.
- RTSP devices can expose the selected stream URI.
- V720/Naxclow devices can expose JPEG frames through the shared frame API.
- MJPEG devices can still provide JPEG captures to `ICameraProvider`.
- H.264 devices can expose raw NAL units and decoded frames when decoder support
  is installed.
- The public API has focused tests and at least one runnable sample path.
