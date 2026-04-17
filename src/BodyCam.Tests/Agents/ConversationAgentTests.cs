using BodyCam.Agents;
using BodyCam.Models;
using FluentAssertions;

namespace BodyCam.Tests.Agents;

public class ConversationAgentTests
{
    [Fact]
    public void AddUserMessage_AddsToSession()
    {
        var agent = new ConversationAgent();
        var session = new SessionContext();

        agent.AddUserMessage("Hello", session);

        session.Messages.Should().ContainSingle(m => m.Role == "user" && m.Content == "Hello");
    }

    [Fact]
    public void AddAssistantMessage_AddsToSession()
    {
        var agent = new ConversationAgent();
        var session = new SessionContext();

        agent.AddAssistantMessage("Hi there", session);

        session.Messages.Should().ContainSingle(m => m.Role == "assistant" && m.Content == "Hi there");
    }

    [Fact]
    public void MultipleCalls_AccumulateMessages()
    {
        var agent = new ConversationAgent();
        var session = new SessionContext();

        agent.AddUserMessage("Q1", session);
        agent.AddAssistantMessage("A1", session);
        agent.AddUserMessage("Q2", session);

        session.Messages.Should().HaveCount(3);
    }
}
