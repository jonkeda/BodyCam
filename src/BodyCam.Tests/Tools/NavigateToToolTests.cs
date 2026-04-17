using BodyCam.Services;
using BodyCam.Tools;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class NavigateToToolTests
{
    private static ToolContext CreateContext() => new()
    {
        CaptureFrame = ct => Task.FromResult<byte[]?>(null),
        Session = new BodyCam.Models.SessionContext(),
        Log = _ => { },
        RealtimeClient = Substitute.For<IRealtimeClient>()
    };

    [Fact]
    public async Task ExecuteAsync_EmptyDestination_ReturnsFail()
    {
        var tool = new NavigateToTool();
        var result = await tool.ExecuteAsync(null, CreateContext(), CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Name_IsNavigateTo()
    {
        var tool = new NavigateToTool();
        tool.Name.Should().Be("navigate_to");
    }

    [Fact]
    public void HasWakeWord()
    {
        var tool = new NavigateToTool();
        tool.WakeWord.Should().NotBeNull();
        tool.WakeWord!.Mode.Should().Be(WakeWordMode.FullSession);
    }
}
