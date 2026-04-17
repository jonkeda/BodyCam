# Step 1: Windows Camera Service (MediaCapture)

Replace the stub `CameraService` with a real Windows implementation using WinRT `MediaCapture`. No third-party NuGet packages needed — `MediaCapture` is available directly in WinUI3/MAUI Windows apps.

## Files Created

### 1. `src/BodyCam/Platforms/Windows/WindowsCameraService.cs`

Windows-specific `ICameraService` implementation using `Windows.Media.Capture.MediaCapture`.

```csharp
using BodyCam.Services;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;

namespace BodyCam.Platforms.Windows;

public class WindowsCameraService : ICameraService, IDisposable
{
    private MediaCapture? _capture;
    private bool _initialized;

    public bool IsCapturing { get; private set; }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return;

        _capture = new MediaCapture();
        await _capture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            StreamingCaptureMode = StreamingCaptureMode.Video,
            MediaCategory = MediaCategory.Communications
        });

        _initialized = true;
        IsCapturing = true;
    }

    public Task StopAsync()
    {
        if (!IsCapturing) return Task.CompletedTask;

        IsCapturing = false;
        _capture?.Dispose();
        _capture = null;
        _initialized = false;
        return Task.CompletedTask;
    }

    public async Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        if (!_initialized || _capture is null) return null;

        var lowLag = await _capture.PrepareLowLagPhotoCaptureAsync(
            ImageEncodingProperties.CreateJpeg());

        try
        {
            var photo = await lowLag.CaptureAsync();
            var frame = photo.Frame;

            // Resize to 512×512 for cost reduction
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
            var decoder = await BitmapDecoder.CreateAsync(frame.AsStream().AsRandomAccessStream());

            var originalWidth = decoder.PixelWidth;
            var originalHeight = decoder.PixelHeight;
            var scale = Math.Min(512.0 / originalWidth, 512.0 / originalHeight);
            var newWidth = (uint)(originalWidth * scale);
            var newHeight = (uint)(originalHeight * scale);

            var pixelData = await decoder.GetPixelDataAsync();
            encoder.SetPixelData(
                decoder.BitmapPixelFormat,
                decoder.BitmapAlphaMode,
                originalWidth, originalHeight,
                decoder.DpiX, decoder.DpiY,
                pixelData.DetachPixelData());
            encoder.BitmapTransform.ScaledWidth = newWidth;
            encoder.BitmapTransform.ScaledHeight = newHeight;
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Linear;
            await encoder.FlushAsync();

            stream.Seek(0);
            var bytes = new byte[stream.Size];
            await stream.ReadAsync(bytes.AsBuffer(), (uint)stream.Size, InputStreamOptions.None);
            return bytes;
        }
        finally
        {
            await lowLag.FinishAsync();
        }
    }

    public async IAsyncEnumerable<byte[]> GetFramesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested && IsCapturing)
        {
            var frame = await CaptureFrameAsync(ct);
            if (frame is not null)
                yield return frame;

            await Task.Delay(100, ct); // ~10fps max
        }
    }

    public void Dispose()
    {
        _capture?.Dispose();
        _capture = null;
    }
}
```

**Key decisions:**
- Uses `PrepareLowLagPhotoCaptureAsync` for fast on-demand capture (faster than `CapturePhotoToStreamAsync`)
- Resizes to 512×512 to reduce vision API token cost (per DESIGN.md)
- JPEG output matches what `VisionAgent.DescribeFrameAsync` expects
- `GetFramesAsync` yields at ~10fps (100ms delay) — callers control the rate

## Files Modified

### 2. `src/BodyCam/MauiProgram.cs`

**Replace** stub camera registration with Windows-specific implementation:

```csharp
// BEFORE:
builder.Services.AddSingleton<ICameraService, CameraService>();

// AFTER:
#if WINDOWS
        builder.Services.AddSingleton<ICameraService, BodyCam.Platforms.Windows.WindowsCameraService>();
#elif ANDROID
        builder.Services.AddSingleton<ICameraService, CameraService>(); // placeholder until Step 5
#else
        builder.Services.AddSingleton<ICameraService, CameraService>();
#endif
```

Follows the same platform-conditional pattern already used for `IAudioInputService` and `IAudioOutputService`.

## Files Unchanged

- `src/BodyCam/Services/ICameraService.cs` — existing interface already defines `StartAsync`, `StopAsync`, `CaptureFrameAsync`, `GetFramesAsync`. No changes needed.
- `src/BodyCam/Services/CameraService.cs` — stub kept as fallback for non-Windows/non-Android platforms.

## Dependencies

No new NuGet packages. `Windows.Media.Capture` is available via the WinRT projection already included in the `net10.0-windows10.0.19041.0` target.

## Verification

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None -v q
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -v q
```

Manual: Run app on Windows laptop → camera should initialize without errors (even if UI preview isn't wired yet — that's Step 4).
