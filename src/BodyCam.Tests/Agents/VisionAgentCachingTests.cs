using BodyCam.Agents;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace BodyCam.Tests.Agents;

public class VisionAgentCachingTests
{
    private readonly IChatClient _chatClient = Substitute.For<IChatClient>();
    private readonly AppSettings _settings = new();

    private VisionAgent CreateAgent() => new(_chatClient, _settings);

    [Fact]
    public async Task DescribeFrameAsync_PassesCustomPrompt()
    {
        _chatClient.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "It says EXIT.")));

        var agent = CreateAgent();
        var result = await agent.DescribeFrameAsync(
            [0xFF, 0xD8], "What text is on the sign?");

        result.Should().Be("It says EXIT.");

        // Verify the custom prompt was sent
        await _chatClient.Received(1).GetResponseAsync(
            Arg.Is<IList<ChatMessage>>(msgs =>
                msgs.Any(m => m.Contents.OfType<TextContent>()
                    .Any(t => t.Text == "What text is on the sign?"))),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LastDescription_UpdatedAfterDescribe()
    {
        _chatClient.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "A cat on a mat.")));

        var agent = CreateAgent();
        agent.LastDescription.Should().BeNull();

        await agent.DescribeFrameAsync([0xFF, 0xD8]);

        agent.LastDescription.Should().Be("A cat on a mat.");
    }

    [Fact]
    public async Task LastCaptureTime_UpdatedAfterDescribe()
    {
        _chatClient.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "A desk.")));

        var agent = CreateAgent();
        agent.LastCaptureTime.Should().Be(DateTimeOffset.MinValue);

        await agent.DescribeFrameAsync([0xFF, 0xD8]);

        agent.LastCaptureTime.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }
}
