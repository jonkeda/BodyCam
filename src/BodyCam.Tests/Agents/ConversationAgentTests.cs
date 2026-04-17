using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Services;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Agents;

public class ConversationAgentTests
{
    private readonly IChatCompletionsClient _chatClient = Substitute.For<IChatCompletionsClient>();
    private readonly AppSettings _settings = new();
    private readonly ConversationAgent _agent;

    public ConversationAgentTests()
    {
        _agent = new ConversationAgent(_chatClient, _settings);
    }

    [Fact]
    public void AddUserMessage_AppendsToSession()
    {
        var session = new SessionContext();

        _agent.AddUserMessage("Hello", session);

        session.Messages.Should().ContainSingle(m => m.Role == "user" && m.Content == "Hello");
    }

    [Fact]
    public void AddAssistantMessage_AppendsToSession()
    {
        var session = new SessionContext();

        _agent.AddAssistantMessage("Hi there", session);

        session.Messages.Should().ContainSingle(m => m.Role == "assistant" && m.Content == "Hi there");
    }

    [Fact]
    public async Task ProcessTranscriptAsync_StreamsTokens()
    {
        var session = new SessionContext();
        _chatClient.CompleteStreamingAsync(Arg.Any<IList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(AsyncYield("Hello", " world"));

        var tokens = new List<string>();
        await foreach (var token in _agent.ProcessTranscriptAsync("Hi", session))
        {
            tokens.Add(token);
        }

        tokens.Should().Equal("Hello", " world");
    }

    [Fact]
    public async Task ProcessTranscriptAsync_AddsUserAndAssistantMessages()
    {
        var session = new SessionContext();
        _chatClient.CompleteStreamingAsync(Arg.Any<IList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(AsyncYield("Hello", " world"));

        await foreach (var _ in _agent.ProcessTranscriptAsync("Hi", session)) { }

        session.Messages.Should().HaveCount(2);
        session.Messages[0].Role.Should().Be("user");
        session.Messages[0].Content.Should().Be("Hi");
        session.Messages[1].Role.Should().Be("assistant");
        session.Messages[1].Content.Should().Be("Hello world");
    }

    [Fact]
    public async Task ProcessTranscriptAsync_SetsSystemPromptIfEmpty()
    {
        var session = new SessionContext();
        session.SystemPrompt = "";
        _chatClient.CompleteStreamingAsync(Arg.Any<IList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(AsyncYield("ok"));

        await foreach (var _ in _agent.ProcessTranscriptAsync("test", session)) { }

        session.SystemPrompt.Should().Be(_settings.SystemInstructions);
    }

    [Fact]
    public async Task ProcessTranscriptFullAsync_ReturnsCompleteReply()
    {
        var session = new SessionContext();
        _chatClient.CompleteAsync(Arg.Any<IList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns("Full reply");

        var result = await _agent.ProcessTranscriptFullAsync("Hi", session);

        result.Should().Be("Full reply");
        session.Messages.Should().HaveCount(2);
        session.Messages[1].Role.Should().Be("assistant");
        session.Messages[1].Content.Should().Be("Full reply");
    }

    private static async IAsyncEnumerable<string> AsyncYield(params string[] tokens)
    {
        foreach (var t in tokens)
        {
            await Task.Yield();
            yield return t;
        }
    }
}
