using BodyCam.Agents;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace BodyCam.Tests.Agents;

public class VisionAgentTests
{
    [Fact]
    public async Task DescribeFrameAsync_ReturnsModelDescription()
    {
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "A desk with a laptop")));
        var settings = new AppSettings();
        var agent = new VisionAgent(chatClient, settings);

        var result = await agent.DescribeFrameAsync([0xFF, 0xD8]);

        result.Should().Be("A desk with a laptop");
    }

    [Fact]
    public async Task DescribeFrameAsync_UpdatesLastDescription()
    {
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "A park scene")));
        var settings = new AppSettings();
        var agent = new VisionAgent(chatClient, settings);

        agent.LastDescription.Should().BeNull();

        await agent.DescribeFrameAsync([0xFF, 0xD8]);

        agent.LastDescription.Should().Be("A park scene");
    }
}
