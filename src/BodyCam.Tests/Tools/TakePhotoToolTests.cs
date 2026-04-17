using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tools;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class TakePhotoToolTests
{
    private static ToolContext CreateContext(byte[]? frame = null) => new()
    {
        CaptureFrame = ct => Task.FromResult(frame),
        Session = new SessionContext(),
        Log = _ => { },
        RealtimeClient = Substitute.For<IRealtimeClient>()
    };

    [Fact]
    public async Task ExecuteAsync_NoFrame_ReturnsFail()
    {
        var tool = new TakePhotoTool();
        var result = await tool.ExecuteAsync(null, CreateContext(frame: null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("Camera not available");
    }

    [Fact]
    public void Name_IsTakePhoto()
    {
        var tool = new TakePhotoTool();
        tool.Name.Should().Be("take_photo");
    }
}
