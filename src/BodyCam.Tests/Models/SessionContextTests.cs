using BodyCam.Models;
using FluentAssertions;

namespace BodyCam.Tests.Models;

public class SessionContextTests
{
    [Fact]
    public void NewSession_HasUniqueId()
    {
        var session = new SessionContext();

        session.SessionId.Should().NotBeNullOrEmpty();
        session.SessionId.Should().HaveLength(32); // Guid "N" format
    }

    [Fact]
    public void NewSession_HasEmptyMessages()
    {
        var session = new SessionContext();

        session.Messages.Should().BeEmpty();
    }

    [Fact]
    public void NewSession_IsNotActive()
    {
        var session = new SessionContext();

        session.IsActive.Should().BeFalse();
    }

    [Fact]
    public void NewSession_HasStartedAtTimestamp()
    {
        var before = DateTime.UtcNow;
        var session = new SessionContext();
        var after = DateTime.UtcNow;

        session.StartedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void TwoSessions_HaveDifferentIds()
    {
        var s1 = new SessionContext();
        var s2 = new SessionContext();

        s1.SessionId.Should().NotBe(s2.SessionId);
    }

    [Fact]
    public void AddMessages_PreservesOrder()
    {
        var session = new SessionContext();

        session.Messages.Add(new ChatMessage { Role = "user", Content = "Hello" });
        session.Messages.Add(new ChatMessage { Role = "assistant", Content = "Hi" });
        session.Messages.Add(new ChatMessage { Role = "user", Content = "Bye" });

        session.Messages.Should().HaveCount(3);
        session.Messages[0].Content.Should().Be("Hello");
        session.Messages[1].Content.Should().Be("Hi");
        session.Messages[2].Content.Should().Be("Bye");
    }

    [Fact]
    public void ChatMessage_HasTimestamp()
    {
        var before = DateTime.UtcNow;
        var msg = new ChatMessage { Role = "user", Content = "test" };
        var after = DateTime.UtcNow;

        msg.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void GetTrimmedHistory_EmptyMessages_ReturnsOnlySystemPrompt()
    {
        var session = new SessionContext { SystemPrompt = "You are helpful." };

        var result = session.GetTrimmedHistory();

        result.Should().ContainSingle();
        result[0].Role.Should().Be("system");
        result[0].Content.Should().Be("You are helpful.");
    }

    [Fact]
    public void GetTrimmedHistory_WithinBudget_ReturnsAllMessages()
    {
        var session = new SessionContext
        {
            SystemPrompt = "Be concise.",
            MaxHistoryChars = 10_000
        };
        session.Messages.Add(new ChatMessage { Role = "user", Content = "Hello" });
        session.Messages.Add(new ChatMessage { Role = "assistant", Content = "Hi there!" });
        session.Messages.Add(new ChatMessage { Role = "user", Content = "Bye" });

        var result = session.GetTrimmedHistory();

        // system + 3 messages
        result.Should().HaveCount(4);
        result[0].Role.Should().Be("system");
        result[1].Content.Should().Be("Hello");
        result[2].Content.Should().Be("Hi there!");
        result[3].Content.Should().Be("Bye");
    }

    [Fact]
    public void GetTrimmedHistory_ExceedsBudget_TrimsOldestMessages()
    {
        var session = new SessionContext
        {
            SystemPrompt = "OK",
            MaxHistoryChars = 10 // very tight budget
        };
        session.Messages.Add(new ChatMessage { Role = "user", Content = "AAAAAAAAAA" }); // 10 chars
        session.Messages.Add(new ChatMessage { Role = "user", Content = "BB" });          // 2 chars

        var result = session.GetTrimmedHistory();

        // Should keep only the most recent message that fits
        result.Should().Contain(m => m.Content == "BB");
        result.Should().NotContain(m => m.Content == "AAAAAAAAAA");
    }

    [Fact]
    public void GetTrimmedHistory_WithVisionContext_InjectsVisionMessage()
    {
        var session = new SessionContext
        {
            SystemPrompt = "Be helpful.",
            LastVisionDescription = "a park with trees"
        };
        session.Messages.Add(new ChatMessage { Role = "user", Content = "What do you see?" });

        var result = session.GetTrimmedHistory();

        result.Should().HaveCount(3); // system + vision + user
        result[0].Role.Should().Be("system");
        result[0].Content.Should().Be("Be helpful.");
        result[1].Role.Should().Be("system");
        result[1].Content.Should().Contain("a park with trees");
        result[2].Content.Should().Be("What do you see?");
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var session = new SessionContext
        {
            SystemPrompt = "test",
            LastVisionDescription = "something",
            IsActive = true
        };
        session.Messages.Add(new ChatMessage { Role = "user", Content = "hi" });
        var oldId = session.SessionId;

        session.Reset();

        session.Messages.Should().BeEmpty();
        session.LastVisionDescription.Should().BeNull();
        session.IsActive.Should().BeFalse();
        session.SessionId.Should().NotBe(oldId);
    }
}
