# Phase 12 - H.264 Decoding

**Status:** Planned

## Goal

Decode raw H.264 from the TCP PPPP/iLnk session path into frames that BodyCam
can display or capture.

This phase depends on phase 10 confirming a real H.264 camera variant and phase
11 producing a stable raw H.264 stream.

## Decoder Strategy

Use an adapter boundary so the app is not coupled directly to one native
decoder package:

```csharp
public interface IA9VideoDecoder : IAsyncDisposable
{
    IAsyncEnumerable<VideoFrame> DecodeAsync(
        IAsyncEnumerable<ReadOnlyMemory<byte>> nalUnits,
        CancellationToken cancellationToken);
}
```

Preferred decoder:

- `FFmpeg.AutoGen`

Fallback to investigate:

- `LibVLCSharp`

Keep decoder dependencies optional until packaging, licensing, and app size are
reviewed.

## Implementation

1. Add a `VideoFrame` model with width, height, pixel format, timestamp, and
   frame bytes or buffer ownership rules.
2. Add an `IA9VideoDecoder` abstraction.
3. Implement `FfmpegA9VideoDecoder` if FFmpeg native deployment is acceptable.
4. Add a fallback design note for `LibVLCSharp` if FFmpeg packaging is too
   heavy.
5. Add tests using a tiny H.264 fixture stream.
6. Add build guards so devices without decoder binaries can still use the
   existing UDP/MJPEG path.

## Files

- `src/BodyCam/Services/Camera/A9/VideoFrame.cs`
- `src/BodyCam/Services/Camera/A9/IA9VideoDecoder.cs`
- `src/BodyCam/Services/Camera/A9/FfmpegA9VideoDecoder.cs`
- `src/BodyCam.Tests/Services/Camera/A9/A9VideoDecoderTests.cs`
- `src/BodyCam.Tests/Services/Camera/A9/Fixtures/*.h264`

## Acceptance Criteria

- Raw H.264 NAL units decode into frames through `IA9VideoDecoder`.
- Decoder errors are surfaced without crashing the camera provider.
- Decoder dependency choices are documented, including native binary handling.
- The UDP/MJPEG provider continues to build and run without FFmpeg or VLC.
