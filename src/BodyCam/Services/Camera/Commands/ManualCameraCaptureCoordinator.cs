namespace BodyCam.Services.Camera.Commands;

public sealed class ManualCameraCaptureRequestedEventArgs : EventArgs
{
    public ManualCameraCaptureRequestedEventArgs(CameraCommandRequest request)
    {
        Request = request;
    }

    public CameraCommandRequest Request { get; }
}

public interface IManualCameraCaptureCoordinator
{
    bool IsCapturePending { get; }
    event EventHandler<ManualCameraCaptureRequestedEventArgs>? CaptureRequested;

    Task<byte[]?> WaitForCaptureAsync(
        CameraCommandRequest request,
        Func<CancellationToken, Task<byte[]?>> captureFrame,
        CancellationToken ct);

    Task<bool> CompletePendingCaptureAsync(CancellationToken ct = default);
    void CancelPendingCapture();
}

public sealed class ManualCameraCaptureCoordinator : IManualCameraCaptureCoordinator
{
    private readonly object _lock = new();
    private PendingCapture? _pending;

    public event EventHandler<ManualCameraCaptureRequestedEventArgs>? CaptureRequested;

    public bool IsCapturePending
    {
        get
        {
            lock (_lock)
                return _pending is not null;
        }
    }

    public Task<byte[]?> WaitForCaptureAsync(
        CameraCommandRequest request,
        Func<CancellationToken, Task<byte[]?>> captureFrame,
        CancellationToken ct)
    {
        PendingCapture pending;
        lock (_lock)
        {
            _pending?.Completion.TrySetCanceled();
            pending = new PendingCapture(request, captureFrame);
            _pending = pending;
        }

        CaptureRequested?.Invoke(this, new ManualCameraCaptureRequestedEventArgs(request));

        if (ct.CanBeCanceled)
        {
            ct.Register(() => CancelPendingCapture(pending));
        }

        return pending.Completion.Task;
    }

    public async Task<bool> CompletePendingCaptureAsync(CancellationToken ct = default)
    {
        PendingCapture? pending;
        lock (_lock)
            pending = _pending;

        if (pending is null)
            return false;

        try
        {
            var frame = await pending.CaptureFrame(ct).ConfigureAwait(false);
            pending.Completion.TrySetResult(frame);
            return true;
        }
        catch (OperationCanceledException)
        {
            pending.Completion.TrySetCanceled(ct);
            return true;
        }
        catch (Exception ex)
        {
            pending.Completion.TrySetException(ex);
            return true;
        }
        finally
        {
            ClearIfCurrent(pending);
        }
    }

    public void CancelPendingCapture()
    {
        PendingCapture? pending;
        lock (_lock)
            pending = _pending;

        if (pending is not null)
            CancelPendingCapture(pending);
    }

    private void CancelPendingCapture(PendingCapture pending)
    {
        pending.Completion.TrySetCanceled();
        ClearIfCurrent(pending);
    }

    private void ClearIfCurrent(PendingCapture pending)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_pending, pending))
                _pending = null;
        }
    }

    private sealed record PendingCapture(
        CameraCommandRequest Request,
        Func<CancellationToken, Task<byte[]?>> CaptureFrame)
    {
        public TaskCompletionSource<byte[]?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
