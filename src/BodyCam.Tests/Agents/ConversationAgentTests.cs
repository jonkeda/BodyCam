using BodyCam.Agents;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace BodyCam.Tests.Agents;

public class ConversationAgentTests
{
    private readonly IChatClient _chatClient = Substitute.For<IChatClient>();
    private readonly AppSettings _settings = new();
    private readonly ConversationAgent _agent;

    public ConversationAgentTests()
    {
        _agent = new ConversationAgent(_chatClient, _settings);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsResponseText()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Deep analysis result"));
        _chatClient.GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _agent.AnalyzeAsync("What is this?");

        result.Should().Be("Deep analysis result");
    }

    [Fact]
    public async Task AnalyzeAsync_IncludesContextWhenProvided()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
        IList<ChatMessage>? capturedMessages = null;
        _chatClient.GetResponseAsync(
            Arg.Do<IList<ChatMessage>>(m => capturedMessages = m),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(response);

        await _agent.AnalyzeAsync("query", context: "some context");

        capturedMessages.Should().NotBeNull();
        capturedMessages!.Should().HaveCount(3);
        capturedMessages[1].Text.Should().Contain("some context");
    }

    [Fact]
    public async Task AnalyzeAsync_OmitsContextWhenNull()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
        IList<ChatMessage>? capturedMessages = null;
        _chatClient.GetResponseAsync(
            Arg.Do<IList<ChatMessage>>(m => capturedMessages = m),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(response);

        await _agent.AnalyzeAsync("query");

        capturedMessages.Should().NotBeNull();
        capturedMessages!.Should().HaveCount(2); // system + user only
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsEmptyStringWhenTextIsNull()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, (string?)null));
        _chatClient.GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _agent.AnalyzeAsync("test");

        result.Should().BeEmpty();
    }
}
