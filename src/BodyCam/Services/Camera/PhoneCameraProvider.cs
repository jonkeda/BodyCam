using System.Runtime.CompilerServices;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;

namespace BodyCam.Services.Camera;

/// <summary>
/// Camera provider that wraps CommunityToolkit.Maui's CameraView for on-device capture.
/// </summary>
public sealed class PhoneCameraProvider : ICameraProvider
{
    private CameraView? _cameraView;
    private bool _started;

    public string DisplayName => "Phone Camera";
    public string ProviderId => "phone";
    public bool IsAvailable => _cameraView is not null;

    public event EventHandler? Disconnected;

    /// <summary>
    /// Sets the CameraView reference. Called from MainPage.xaml.cs.
    /// </summary>
    public void SetCameraView(CameraView view)
    {
        _cameraView = view;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_started || _cameraView is null) return;
        await MainThread.InvokeOnMainThreadAsync(async () =>
            await _cameraView.StartCameraPreview(ct));
        _started = true;
    }

    public async Task StopAsync()
    {
        if (!_started || _cameraView is null) return;
        await MainThread.InvokeOnMainThreadAsync(() =>
            _cameraView.StopCameraPreview());
        _started = false;
    }

    public async Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        if (_cameraView is null) return null;

        try
        {
            bool needsHeadlessCapture = !_started;
            if (needsHeadlessCapture)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await _cameraView.StartCameraPreview(ct));
                _started = true;
                await Task.Delay(500, ct); // camera warm-up
            }

            try
            {
                return await CaptureViaEventAsync(ct);
            }
            finally
            {
                if (needsHeadlessCapture)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        _cameraView.StopCameraPreview());
                    _started = false;
                }
            }
        }
        catch
        {
            return null;
        }
    }

    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = await CaptureFrameAsync(ct);
            if (frame is not null)
            {
                yield return frame;
            }

            await Task.Delay(100, ct);
        }
    }

    public ValueTask DisposeAsync()
    {
        _cameraView = null;
        _started = false;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Subscribes to MediaCaptured, calls CaptureImage, and awaits the result with a 5s timeout.
    /// </summary>
    private async Task<byte[]?> CaptureViaEventAsync(CancellationToken ct)
    {
        var view = _cameraView;
        if (view is null) return null;

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

        await MainThread.InvokeOnMainThreadAsync(() => view.MediaCaptured += OnMediaCaptured);
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => view.CaptureImage(ct));

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            using var registration = timeoutCts.Token.Register(() => tcs.TrySetResult(null));

            return await tcs.Task;
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => view.MediaCaptured -= OnMediaCaptured);
        }
    }
}
