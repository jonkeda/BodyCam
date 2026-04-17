using System.Runtime.CompilerServices;

namespace BodyCam.Services;

/// <summary>
/// Stub camera service. Replace with platform webcam/camera implementation.
/// </summary>
public class CameraService : ICameraService
{
    public bool IsCapturing { get; private set; }

    public Task StartAsync(CancellationToken ct = default)
    {
        IsCapturing = true;
        // TODO: Implement platform camera capture (M3)
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsCapturing = false;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<byte[]> GetFramesAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        // TODO: Yield camera frames
        await Task.CompletedTask;
        yield break;
    }

    public Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        // TODO: Capture single frame
        return Task.FromResult<byte[]?>(null);
    }
}
