using BodyCam.Services;
using FluentAssertions;

namespace BodyCam.Tests.Services;

public class NullWakeWordServiceTests
{
    [Fact]
    public void IsListening_ReturnsFalse()
    {
        var service = new NullWakeWordService();
        service.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_DoesNotThrow()
    {
        var service = new NullWakeWordService();
        await service.StartAsync();
        service.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task StopAsync_DoesNotThrow()
    {
        var service = new NullWakeWordService();
        await service.StopAsync();
    }
}
