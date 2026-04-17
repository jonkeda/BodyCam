using BodyCam.Agents;
using BodyCam.Services;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Agents;

public class VisionAgentTests
{
    [Fact]
    public async Task DescribeFrameAsync_ReturnsStubDescription()
    {
        var camera = Substitute.For<ICameraService>();
        var settings = new AppSettings();
        var agent = new VisionAgent(camera, settings);

        var result = await agent.DescribeFrameAsync([1, 2, 3]);

        result.Should().Contain("stub");
    }

    [Fact]
    public async Task CaptureAndDescribeAsync_ReturnsNull_WhenNoFrame()
    {
        var camera = Substitute.For<ICameraService>();
        camera.CaptureFrameAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null));
        var settings = new AppSettings();
        var agent = new VisionAgent(camera, settings);

        var result = await agent.CaptureAndDescribeAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task CaptureAndDescribeAsync_ReturnsDescription_WhenFrameAvailable()
    {
        var camera = Substitute.For<ICameraService>();
        camera.CaptureFrameAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(new byte[] { 0xFF, 0xD8 }));
        var settings = new AppSettings();
        var agent = new VisionAgent(camera, settings);

        var result = await agent.CaptureAndDescribeAsync();

        result.Should().NotBeNull();
        result.Should().Contain("stub");
    }
}
