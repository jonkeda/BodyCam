using BodyCam.Services;

namespace BodyCam.Platforms.Windows;

public class WindowsCameraService : ICameraService
{
    public bool IsCapturing { get; private set; }

    public Task StartAsync(CancellationToken ct = default)
    {
        IsCapturing = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsCapturing = false;
        return Task.CompletedTask;
    }
}
