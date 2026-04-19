using BodyCam.Agents;
using BodyCam.Services;
using BodyCam.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class StartSceneWatchToolTests
{
    private static ToolContext CreateContext() => new()
    {
        CaptureFrame = ct => Task.FromResult<byte[]?>(null),
        Session = new BodyCam.Models.SessionContext(),
        Log = _ => { },
    };

    [Fact]
    public async Task ExecuteAsync_EmptyCondition_ReturnsFail()
    {
        var chatClient = Substitute.For<IChatClient>();
        var vision = new VisionAgent(chatClient, new AppSettings());
        var tool = new StartSceneWatchTool(vision);

        var result = await tool.ExecuteAsync(null, CreateContext(), CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ValidCondition_ReturnsWatching()
    {
        var chatClient = Substitute.For<IChatClient>();
        var vision = new VisionAgent(chatClient, new AppSettings());
        var tool = new StartSceneWatchTool(vision);

        var result = await tool.ExecuteAsync(
            JsonHelper.ParseElement("{\"condition\":\"when the light turns green\"}"),
            CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("Watching");
    }

    [Fact]
    public void Name_IsStartSceneWatch()
    {
        var chatClient = Substitute.For<IChatClient>();
        var vision = new VisionAgent(chatClient, new AppSettings());
        var tool = new StartSceneWatchTool(vision);
        tool.Name.Should().Be("start_scene_watch");
    }
}
