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
}
