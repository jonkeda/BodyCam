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
        var expected = Color.FromArgb("#4CAF50");

        entry.RoleColor.Red.Should().BeApproximately(expected.Red, 0.01f);
        entry.RoleColor.Green.Should().BeApproximately(expected.Green, 0.01f);
        entry.RoleColor.Blue.Should().BeApproximately(expected.Blue, 0.01f);
    }

    [Fact]
    public void RoleColor_AI_IsBlue()
    {
        var entry = new TranscriptEntry { Role = "AI" };
        var expected = Color.FromArgb("#2196F3");

        entry.RoleColor.Red.Should().BeApproximately(expected.Red, 0.01f);
        entry.RoleColor.Green.Should().BeApproximately(expected.Green, 0.01f);
        entry.RoleColor.Blue.Should().BeApproximately(expected.Blue, 0.01f);
    }

    [Fact]
    public void RoleColor_System_IsGray()
    {
        var entry = new TranscriptEntry { Role = "System" };
        var expected = Color.FromArgb("#999999");

        entry.RoleColor.Red.Should().BeApproximately(expected.Red, 0.01f);
        entry.RoleColor.Green.Should().BeApproximately(expected.Green, 0.01f);
        entry.RoleColor.Blue.Should().BeApproximately(expected.Blue, 0.01f);
    }
}
