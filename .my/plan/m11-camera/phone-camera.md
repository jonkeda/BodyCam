# M11 Phase 1 — Phone Camera Provider

## Goal

Wrap the existing CommunityToolkit.Maui `CameraView` capture into the
`ICameraProvider` abstraction. Add headless capture (capture without the
preview being visible to the user).

---

## PhoneCameraProvider

```csharp
namespace BodyCam.Services.Camera;

/// <summary>
/// Camera provider wrapping CommunityToolkit.Maui CameraView.
/// Falls back to headless capture when the preview is not visible.
/// </summary>
public class PhoneCameraProvider : ICameraProvider
{
    private CameraView? _cameraView;
    private bool _started;
    private bool _ownedStart; // true if we started the preview ourselves

    public string DisplayName => "Phone Camera";
    public string ProviderId => "phone";
    public bool IsAvailable => _cameraView is not null;

    public event EventHandler? Disconnected;

    /// <summary>
    /// Set by MainPage.xaml.cs when the CameraView is loaded.
    /// </summary>
    public void SetCameraView(CameraView view) => _cameraView = view;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_started || _cameraView is null) return;

        await _cameraView.StartCameraPreview(ct);
        _started = true;
    }

    public Task StopAsync()
    {
        if (!_started || _cameraView is null) return Task.CompletedTask;

        _cameraView.StopCameraPreview();
        _started = false;
        return Task.CompletedTask;
    }

    public async Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        if (_cameraView is null) return null;

        // If preview not running, start it temporarily
        _ownedStart = !_started;
        if (_ownedStart)
        {
            await _cameraView.StartCameraPreview(ct);
            _started = true;

            // Camera sensors need warm-up time for valid first frame
            await Task.Delay(500, ct);
        }

        try
        {
            return await CaptureViaEventAsync(ct);
        }
        finally
        {
            // Stop if we started it ourselves and nobody else is using it
            if (_ownedStart)
            {
                _cameraView.StopCameraPreview();
                _started = false;
                _ownedStart = false;
            }
        }
    }

    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = await CaptureFrameAsync(ct);
            if (frame is not null)
                yield return frame;

            await Task.Delay(100, ct); // ~10 fps max
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_started) _cameraView?.StopCameraPreview();
        _started = false;
        _cameraView = null;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Captures a single JPEG frame via the CameraView.MediaCaptured event.
    /// This is the existing pattern extracted from MainViewModel.
    /// </summary>
    private async Task<byte[]?> CaptureViaEventAsync(CancellationToken ct)
    {
        if (_cameraView is null) return null;

        var tcs = new TaskCompletionSource<byte[]?>();

        void OnMediaCaptured(object? s, MediaCapturedEventArgs e)
        {
            try
            {
                if (e.Media is null || e.Media.Length == 0)
                {
                    tcs.TrySetResult(null);
                    return;
                }

                using var ms = new MemoryStream();
                e.Media.CopyTo(ms);
                tcs.TrySetResult(ms.ToArray());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        _cameraView.MediaCaptured += OnMediaCaptured;
        try
        {
            await _cameraView.CaptureImage(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            timeoutCts.Token.Register(() => tcs.TrySetResult(null));

            return await tcs.Task;
        }
        finally
        {
            _cameraView.MediaCaptured -= OnMediaCaptured;
        }
    }
}
```

---

## Migration Steps

1. **Create** `Services/Camera/ICameraProvider.cs` and `Services/Camera/CameraManager.cs`
2. **Create** `Services/Camera/PhoneCameraProvider.cs` — extract logic from
   `MainViewModel.CaptureFrameFromCameraViewAsync`
3. **Update** `MainPage.xaml.cs` — call `PhoneCameraProvider.SetCameraView(CameraPreview)`
   instead of `viewModel.SetCameraView(CameraPreview)`
4. **Update** `MainViewModel` — inject `CameraManager`, remove `_cameraView` field and
   `CaptureFrameFromCameraViewAsync` method
5. **Update** `AgentOrchestrator` — inject `CameraManager`, set
   `FrameCaptureFunc = _cameraManager.CaptureFrameAsync` (or use directly)
6. **Register** in `MauiProgram.cs`
7. **Update** Settings page — add camera picker (dropdown of `CameraManager.Providers`)

---

## Headless Capture — Key Concerns

### CameraView visibility

The CameraView is in a Grid with `IsVisible="{Binding ShowCameraTab}"`. When
the transcript tab is shown, the Grid is invisible.

**Behavior of StartCameraPreview on an invisible CameraView:**
- The CameraView's native handler is created when the control is added to the
  visual tree (which happens at page load, not when made visible)
- `StartCameraPreview()` initializes the native camera pipeline via the handler
- On Windows (WinUI3), this works because the native `MediaPlayerElement` is
  allocated regardless of visibility
- On Android, the `CameraView` uses `SurfaceView` which may not initialize
  without a visible surface

**Mitigation:** If headless capture fails on a platform, swap `IsVisible="false"`
for `Opacity="0" HeightRequest="1"` so the CameraView remains in layout but
is invisible to the user. This is a known MAUI pattern (documented in user
memory for Brinell.Maui).

### Camera warm-up

After `StartCameraPreview()`, the first frame may be black or corrupted.
The 500ms delay in `CaptureFrameAsync` handles this. This delay only applies
when we start the preview ourselves (headless path).

### Thread safety

`CameraView` is a UI control — `StartCameraPreview` and `CaptureImage` must
be called on the main thread. `PhoneCameraProvider` methods should dispatch
to `MainThread.InvokeOnMainThreadAsync()` when called from background threads.
