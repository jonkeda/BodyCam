using BodyCam.Agents;
using BodyCam.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace BodyCam.Tests.Agents;

public class VisionAgentTests
{
    [Fact]
    public async Task DescribeFrameAsync_ReturnsModelDescription()
    {
        var camera = Substitute.For<ICameraService>();
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "A desk with a laptop")));
        var settings = new AppSettings();
        var agent = new VisionAgent(camera, chatClient, settings);

        var result = await agent.DescribeFrameAsync([0xFF, 0xD8]);

        result.Should().Be("A desk with a laptop");
    }

    [Fact]
    public async Task CaptureAndDescribeAsync_ReturnsNull_WhenNoFrame()
    {
        var camera = Substitute.For<ICameraService>();
        camera.CaptureFrameAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null));
        var chatClient = Substitute.For<IChatClient>();
        var settings = new AppSettings();
        var agent = new VisionAgent(camera, chatClient, settings);

        var result = await agent.CaptureAndDescribeAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task CaptureAndDescribeAsync_ReturnsDescription_WhenFrameAvailable()
    {
        var camera = Substitute.For<ICameraService>();
        camera.CaptureFrameAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(new byte[] { 0xFF, 0xD8 }));
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "A park scene")));
        var settings = new AppSettings();
        var agent = new VisionAgent(camera, chatClient, settings);

        var result = await agent.CaptureAndDescribeAsync();

        result.Should().NotBeNull();
        result.Should().Be("A park scene");
    }
}
