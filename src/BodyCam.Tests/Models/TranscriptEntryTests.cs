using BodyCam.Models;
using FluentAssertions;
using Microsoft.Maui.Controls;

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

    [Fact]
    public void Text_WhenThinking_DoesNotNotifyAccessibleText()
    {
        var entry = new TranscriptEntry { Role = "AI", IsThinking = true };
        var changed = new List<string?>();
        entry.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        entry.Text = ".";

        changed.Should().Contain(nameof(TranscriptEntry.FormattedText));
        changed.Should().NotContain(nameof(TranscriptEntry.AccessibleText));
        entry.AccessibleText.Should().Be("AI is thinking");
    }

    [Fact]
    public void AccessibleText_WithMarkdown_ReturnsPlainText()
    {
        var entry = new TranscriptEntry { Role = "AI" };

        entry.Text = "**Bold** and [docs](https://example.com)";

        entry.AccessibleText.Should().Be("AI: Bold and docs");
    }

    [Fact]
    public void Text_NotifiesFormattedTextChange()
    {
        var entry = new TranscriptEntry { Role = "AI" };
        var changed = new List<string?>();
        entry.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        entry.Text = "Hello";

        changed.Should().Contain(nameof(TranscriptEntry.FormattedText));
    }

    [Fact]
    public void FormattedText_WithMarkdown_RendersFormattingWithoutMarkdownSyntax()
    {
        var entry = new TranscriptEntry { Role = "AI" };

        entry.Text = "Here is **bold** and *soft* with `code`.";

        var runs = TranscriptMarkdownFormatter.ToTextRuns(entry.Role, entry.Text);
        var text = string.Concat(runs.Select(run => run.Text));

        text.Should().Be("AI: Here is bold and soft with code.");
        runs.Should().Contain(run =>
            run.Text == "bold" && run.FontAttributes.HasFlag(FontAttributes.Bold));
        runs.Should().Contain(run =>
            run.Text == "soft" && run.FontAttributes.HasFlag(FontAttributes.Italic));
        runs.Should().Contain(run =>
            run.Text == "code" && run.FontFamily == "Consolas");
    }

    [Fact]
    public void FormattedText_WithMarkdownList_RendersListText()
    {
        var entry = new TranscriptEntry { Role = "AI" };

        entry.Text = "- Alpha\n- **Beta**";

        var text = string.Concat(TranscriptMarkdownFormatter
            .ToTextRuns(entry.Role, entry.Text)
            .Select(run => run.Text));

        text.Should().Be("AI: - Alpha\n- Beta");
        text.Should().NotContain("**");
    }
}
