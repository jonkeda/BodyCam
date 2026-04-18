namespace BodyCam.Services.Camera;

/// <summary>
/// A camera source that can capture JPEG frames.
/// Only one provider is active at a time, managed by CameraManager.
/// </summary>
public interface ICameraProvider : IAsyncDisposable
{
    /// <summary>Human-readable name for the camera source.</summary>
    string DisplayName { get; }

    /// <summary>Unique identifier for this provider type (e.g. "phone", "usb", "meta").</summary>
    string ProviderId { get; }

    /// <summary>Whether the camera hardware is currently connected and ready.</summary>
    bool IsAvailable { get; }

    /// <summary>Initialize the camera hardware. Idempotent.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Release the camera hardware. Idempotent.</summary>
    Task StopAsync();

    /// <summary>Capture a single JPEG frame. Returns null if capture fails.</summary>
    Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default);

    /// <summary>Stream continuous JPEG frames.</summary>
    IAsyncEnumerable<byte[]> StreamFramesAsync(CancellationToken ct);

    /// <summary>Raised when the camera disconnects unexpectedly.</summary>
    event EventHandler? Disconnected;
}
