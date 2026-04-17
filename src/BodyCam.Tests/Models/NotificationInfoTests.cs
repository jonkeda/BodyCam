using BodyCam.Models;
using FluentAssertions;

namespace BodyCam.Tests.Models;

public class NotificationInfoTests
{
    [Fact]
    public void Defaults_AreReasonable()
    {
        var info = new NotificationInfo();
        info.App.Should().BeEmpty();
        info.Title.Should().BeNull();
        info.Text.Should().BeNull();
        info.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Properties_SetCorrectly()
    {
        var ts = DateTimeOffset.UtcNow.AddMinutes(-5);
        var info = new NotificationInfo
        {
            App = "com.whatsapp",
            Title = "John",
            Text = "Hey there!",
            Timestamp = ts
        };

        info.App.Should().Be("com.whatsapp");
        info.Title.Should().Be("John");
        info.Text.Should().Be("Hey there!");
        info.Timestamp.Should().Be(ts);
    }
}
