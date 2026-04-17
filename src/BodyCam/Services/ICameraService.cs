namespace BodyCam.Services;

/// <summary>
/// Provides camera frames as async stream.
/// </summary>
public interface ICameraService
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    bool IsCapturing { get; }

    /// <summary>Async stream of camera frames (JPEG bytes).</summary>
    IAsyncEnumerable<byte[]> GetFramesAsync(CancellationToken ct = default);

    /// <summary>Capture a single frame on demand.</summary>
    Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default);
}
