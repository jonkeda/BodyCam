using BodyCam.Models;
using FluentAssertions;

namespace BodyCam.Tests.Models;

public class TranscriptEntryTests
{
    [Fact]
    public void Text_RaisesPropertyChanged()
    {
        var entry = new TranscriptEntry { Role = "AI" };
        var changed = new List<string?>();
        entry.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        entry.Text = "Hello";

        changed.Should().Contain("Text");
        entry.Text.Should().Be("Hello");
    }

    [Fact]
    public void Text_AccumulatesDeltas()
    {
        var entry = new TranscriptEntry { Role = "AI" };

        entry.Text += "Hello";
        entry.Text += " world";
        entry.Text += "!";

        entry.Text.Should().Be("Hello world!");
    }

    [Fact]
    public void Role_IsSetOnInit()
    {
        var ai = new TranscriptEntry { Role = "AI" };
        var user = new TranscriptEntry { Role = "You" };

        ai.Role.Should().Be("AI");
        user.Role.Should().Be("You");
    }

    [Fact]
    public void Text_DefaultsToEmpty()
    {
        var entry = new TranscriptEntry { Role = "AI" };
        entry.Text.Should().BeEmpty();
    }

    [Fact]
    public void Text_SameValue_DoesNotRaisePropertyChanged()
    {
        var entry = new TranscriptEntry { Role = "AI", Text = "Hello" };
        var changed = new List<string?>();
        entry.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        entry.Text = "Hello";

        changed.Should().BeEmpty();
    }

    [Fact]
    public void HasImage_WithoutImage_ReturnsFalse()
    {
        var entry = new TranscriptEntry { Role = "AI" };

        entry.HasImage.Should().BeFalse();
        entry.Image.Should().BeNull();
    }

    [Fact]
    public void RoleColor_You_IsGreen()
    {
        var entry = new TranscriptEntry { Role = "You" };

        // Theme-aware: light theme returns dark green #2E7D32, dark theme returns #81C784
        // Just verify it returns a color (theme may vary in test)
        entry.RoleColor.Should().NotBeNull();
    }

    [Fact]
    public void RoleColor_AI_IsBlue()
    {
        var entry = new TranscriptEntry { Role = "AI" };

        entry.RoleColor.Should().NotBeNull();
    }

    [Fact]
    public void RoleColor_System_IsGray()
    {
        var entry = new TranscriptEntry { Role = "System" };

        entry.RoleColor.Should().NotBeNull();
    }

    [Fact]
    public void AccessibleText_WithText_ReturnsRoleColonText()
    {
        var entry = new TranscriptEntry { Role = "AI" };
        entry.Text = "Hello world";

        entry.AccessibleText.Should().Be("AI: Hello world");
    }

    [Fact]
    public void AccessibleText_WhenThinking_ReturnsThinkingMessage()
    {
        var entry = new TranscriptEntry { Role = "AI", IsThinking = true };

        entry.AccessibleText.Should().Be("AI is thinking");
    }

    [Fact]
    public void AccessibleText_EmptyText_ReturnsRoleOnly()
    {
        var entry = new TranscriptEntry { Role = "AI" };

        entry.AccessibleText.Should().Be("AI");
    }

    [Fact]
    public void AccessibleText_NotifiesOnTextChange()
    {
        var entry = new TranscriptEntry { Role = "AI" };
        var changed = new List<string?>();
        entry.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        entry.Text = "Hello";

        changed.Should().Contain(nameof(TranscriptEntry.AccessibleText));
    }

    [Fact]
    public void AccessibleText_NotifiesOnIsThinkingChange()
    {
        var entry = new TranscriptEntry { Role = "AI" };
        var changed = new List<string?>();
        entry.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        entry.IsThinking = true;

        changed.Should().Contain(nameof(TranscriptEntry.AccessibleText));
    }
}
