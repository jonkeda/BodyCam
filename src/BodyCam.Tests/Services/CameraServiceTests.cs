using BodyCam.Services;
using FluentAssertions;

namespace BodyCam.Tests.Services;

public class CameraServiceTests
{
    [Fact]
    public async Task StartAsync_SetsIsCapturing()
    {
        var svc = new CameraService();
        svc.IsCapturing.Should().BeFalse();

        await svc.StartAsync();
        svc.IsCapturing.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_ClearsIsCapturing()
    {
        var svc = new CameraService();
        await svc.StartAsync();
        await svc.StopAsync();
        svc.IsCapturing.Should().BeFalse();
    }
}
