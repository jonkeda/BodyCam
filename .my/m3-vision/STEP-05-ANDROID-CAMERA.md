# Step 5: Android Camera Service

Implement `ICameraService` for Android using `Android.Hardware.Camera2` (CameraManager). Requires `CAMERA` permission.

## Files Created

### 1. `src/BodyCam/Platforms/Android/AndroidCameraService.cs`

Android-specific `ICameraService` using Camera2 API with `ImageReader` for JPEG capture.

```csharp
using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Media;
using Android.OS;
using Android.Views;
using BodyCam.Services;
using System.Runtime.CompilerServices;

namespace BodyCam.Platforms.Android;

public class AndroidCameraService : ICameraService
{
    private CameraDevice? _camera;
    private CameraCaptureSession? _session;
    private ImageReader? _imageReader;
    private bool _initialized;

    public bool IsCapturing { get; private set; }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return;

        var context = Platform.CurrentActivity
            ?? throw new InvalidOperationException("No current activity");
        var manager = (CameraManager)context.GetSystemService(Context.CameraService)!;

        // Find rear-facing camera
        var cameraId = manager.GetCameraIdList()
            .FirstOrDefault(id =>
            {
                var chars = manager.GetCameraCharacteristics(id);
                var facing = (int)(chars.Get(CameraCharacteristics.LensFacing)
                    ?? (Java.Lang.Integer)0);
                return facing == (int)LensFacing.Back;
            }) ?? manager.GetCameraIdList()[0];

        _imageReader = ImageReader.NewInstance(512, 512, ImageFormatType.Jpeg, 2);

        var tcs = new TaskCompletionSource<CameraDevice>();
        manager.OpenCamera(cameraId, new CameraStateCallback(tcs), new Handler(Looper.MainLooper!));
        _camera = await tcs.Task;

        // Create capture session
        var surfaces = new List<Surface> { _imageReader.Surface! };
        var sessionTcs = new TaskCompletionSource<CameraCaptureSession>();
        _camera.CreateCaptureSession(surfaces,
            new SessionStateCallback(sessionTcs), new Handler(Looper.MainLooper!));
        _session = await sessionTcs.Task;

        _initialized = true;
        IsCapturing = true;
    }

    public Task StopAsync()
    {
        if (!IsCapturing) return Task.CompletedTask;
        IsCapturing = false;

        _session?.Close();
        _camera?.Close();
        _imageReader?.Close();
        _session = null;
        _camera = null;
        _imageReader = null;
        _initialized = false;
        return Task.CompletedTask;
    }

    public async Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        if (!_initialized || _camera is null || _session is null || _imageReader is null)
            return null;

        var builder = _camera.CreateCaptureRequest(CameraTemplate.StillCapture)!;
        builder.AddTarget(_imageReader.Surface!);

        var captureTcs = new TaskCompletionSource<bool>();
        _session.Capture(builder.Build()!,
            new CaptureCallback(captureTcs), new Handler(Looper.MainLooper!));
        await captureTcs.Task;

        using var image = _imageReader.AcquireLatestImage();
        if (image is null) return null;

        var buffer = image.GetPlanes()![0].Buffer!;
        var bytes = new byte[buffer.Remaining()];
        buffer.Get(bytes);
        return bytes;
    }

    public async IAsyncEnumerable<byte[]> GetFramesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested && IsCapturing)
        {
            var frame = await CaptureFrameAsync(ct);
            if (frame is not null) yield return frame;
            await Task.Delay(200, ct); // ~5fps on mobile
        }
    }

    // --- Callbacks ---

    private class CameraStateCallback(TaskCompletionSource<CameraDevice> tcs)
        : CameraDevice.StateCallback
    {
        public override void OnOpened(CameraDevice camera) => tcs.TrySetResult(camera);
        public override void OnDisconnected(CameraDevice camera) => camera.Close();
        public override void OnError(CameraDevice camera, CameraError error)
            => tcs.TrySetException(new Exception($"Camera error: {error}"));
    }

    private class SessionStateCallback(TaskCompletionSource<CameraCaptureSession> tcs)
        : CameraCaptureSession.StateCallback
    {
        public override void OnConfigured(CameraCaptureSession session) => tcs.TrySetResult(session);
        public override void OnConfigureFailed(CameraCaptureSession session)
            => tcs.TrySetException(new Exception("Camera session configuration failed"));
    }

    private class CaptureCallback(TaskCompletionSource<bool> tcs)
        : CameraCaptureSession.CaptureCallback
    {
        public override void OnCaptureCompleted(CameraCaptureSession session,
            CaptureRequest request, TotalCaptureResult result)
            => tcs.TrySetResult(true);

        public override void OnCaptureFailed(CameraCaptureSession session,
            CaptureRequest request, CaptureFailure failure)
            => tcs.TrySetException(new Exception($"Capture failed: {failure.Reason}"));
    }
}
```

**Key decisions:**
- Uses Camera2 API (lower-level than CameraX but no extra dependencies)
- Captures at 512×512 JPEG directly from `ImageReader` — no separate resize step needed
- Back camera preferred; falls back to first available camera
- `GetFramesAsync` at 5fps (200ms delay — lower than Windows due to mobile battery)
- Callbacks wrapped as inner classes using primary constructors

### 2. `src/BodyCam/Platforms/Android/AndroidManifest.xml`

**Add** camera permission (if not already present):

```xml
<uses-permission android:name="android.permission.CAMERA" />
<uses-feature android:name="android.hardware.camera" android:required="false" />
```

### 3. `src/BodyCam/Platforms/Android/MainActivity.cs`

**Add** runtime permission request for camera on startup (Android 6.0+):

```csharp
// In OnCreate, after base.OnCreate:
if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.M)
{
    if (CheckSelfPermission(Android.Manifest.Permission.Camera)
        != Android.Content.PM.Permission.Granted)
    {
        RequestPermissions(new[] { Android.Manifest.Permission.Camera }, 1001);
    }
}
```

## Files Modified

### 4. `src/BodyCam/MauiProgram.cs`

**Update** camera service registration (from Step 1):

```csharp
// BEFORE (from Step 1):
#if WINDOWS
        builder.Services.AddSingleton<ICameraService, BodyCam.Platforms.Windows.WindowsCameraService>();
#elif ANDROID
        builder.Services.AddSingleton<ICameraService, CameraService>(); // placeholder
#else
        builder.Services.AddSingleton<ICameraService, CameraService>();
#endif

// AFTER:
#if WINDOWS
        builder.Services.AddSingleton<ICameraService, BodyCam.Platforms.Windows.WindowsCameraService>();
#elif ANDROID
        builder.Services.AddSingleton<ICameraService, BodyCam.Platforms.Android.AndroidCameraService>();
#else
        builder.Services.AddSingleton<ICameraService, CameraService>();
#endif
```

## Verification

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android -v q
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -v q
```

Manual (Android device/emulator): Run app → Start → verify camera permission prompt appears → grant → verify debug log shows "Camera started." → ask "what do you see?" → verify function call completes.
