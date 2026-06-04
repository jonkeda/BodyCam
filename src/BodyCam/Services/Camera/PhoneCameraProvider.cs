using System.Runtime.CompilerServices;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Camera;

/// <summary>
/// Camera provider that wraps CommunityToolkit.Maui's CameraView for on-device capture.
/// </summary>
public sealed class PhoneCameraProvider : ICameraProvider
{
    private readonly ILogger<PhoneCameraProvider> _log;
    private CameraView? _cameraView;
    private bool _started;
    private bool _cameraUnavailable;

    public PhoneCameraProvider(ILogger<PhoneCameraProvider> log)
    {
        _log = log;
    }

    public string DisplayName => "Phone Camera";
    public string ProviderId => "phone";
    public bool IsAvailable => _cameraView is not null && !_cameraUnavailable;
    public bool SupportsVideoRecording => true;
    public bool IsStarted => _started;

    public event EventHandler? Disconnected;

    /// <summary>
    /// Sets the CameraView reference. Called from MainPage.xaml.cs.
    /// </summary>
    public void SetCameraView(CameraView view)
    {
        _cameraView = view;
        _cameraUnavailable = false;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_started || _cameraView is null) return;

        try
        {
            if (!await WaitForPlatformViewAsync(_cameraView, ct).ConfigureAwait(false))
                return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
                await _cameraView.StartCameraPreview(ct));
            _started = true;
            _cameraUnavailable = false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (CameraException ex)
        {
            MarkUnavailable(ex, "Phone camera preview could not be started");
        }
    }

    public async Task StopAsync()
    {
        if (!_started || _cameraView is null) return;
        await StopPreviewIfReadyAsync(_cameraView);
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
                if (!await WaitForPlatformViewAsync(_cameraView, ct).ConfigureAwait(false))
                    return null;

                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await _cameraView.StartCameraPreview(ct));
                _started = true;
                _cameraUnavailable = false;
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
                    await StopPreviewIfReadyAsync(_cameraView);
                    _started = false;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (CameraException ex)
        {
            MarkUnavailable(ex, "Phone camera capture could not start preview");
            return null;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Phone camera capture failed");
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
        _cameraUnavailable = false;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Subscribes to MediaCaptured, calls CaptureImage, and awaits the result with a 5s timeout.
    /// </summary>
    private async Task<byte[]?> CaptureViaEventAsync(CancellationToken ct)
    {
        var view = _cameraView;
        if (view is null) return null;
        if (!await WaitForPlatformViewAsync(view, ct).ConfigureAwait(false))
            return null;

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

    private async Task<bool> WaitForPlatformViewAsync(CameraView view, CancellationToken ct)
    {
        const int attempts = 10;

        for (var i = 0; i < attempts; i++)
        {
            ct.ThrowIfCancellationRequested();

            var isReady = await MainThread.InvokeOnMainThreadAsync(() =>
                view.Handler?.PlatformView is not null);
            if (isReady)
                return true;

            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        _log.LogDebug("Phone camera view platform handler is not ready");
        return false;
    }

    private async Task StopPreviewIfReadyAsync(CameraView view)
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (view.Handler?.PlatformView is not null)
                    view.StopCameraPreview();
            });
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Phone camera preview stop failed");
        }
    }

    private void MarkUnavailable(CameraException ex, string message)
    {
        _started = false;
        _cameraUnavailable = true;
        _log.LogWarning(ex, "{Message}", message);
        Disconnected?.Invoke(this, EventArgs.Empty);
    }
}
