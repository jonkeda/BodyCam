namespace BodyCam.Services;

/// <summary>
/// Camera lifecycle management. Preview is handled natively by CameraView.
/// </summary>
public interface ICameraService
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    bool IsCapturing { get; }
}
