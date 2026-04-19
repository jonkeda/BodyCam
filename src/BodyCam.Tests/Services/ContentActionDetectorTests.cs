using BodyCam.Services;
using FluentAssertions;

namespace BodyCam.Tests.Services;

public class ContentActionDetectorTests
{
    [Fact]
    public void Detect_Url_ReturnsOpenLinkAction()
    {
        var actions = ContentActionDetector.Detect("Check out https://example.com for details.");

        actions.Should().ContainSingle();
        actions[0].Label.Should().Be("Open Link");
        actions[0].Url.Should().Be("https://example.com");
    }

    [Fact]
    public void Detect_MultipleUrls_ReturnsMultipleActions()
    {
        var actions = ContentActionDetector.Detect(
            "Visit https://one.com and https://two.com for more info.");

        actions.Should().HaveCount(2);
        actions[0].Url.Should().Be("https://one.com");
        actions[1].Url.Should().Be("https://two.com");
    }

    [Fact]
    public void Detect_DuplicateUrls_DeduplicatesActions()
    {
        var actions = ContentActionDetector.Detect(
            "Go to https://example.com — that's https://example.com again.");

        actions.Should().ContainSingle();
    }

    [Fact]
    public void Detect_Email_ReturnsEmailAction()
    {
        var actions = ContentActionDetector.Detect("Contact us at hello@example.com for help.");

        actions.Should().ContainSingle();
        actions[0].Label.Should().Be("Email");
        actions[0].Url.Should().Be("hello@example.com");
    }

    [Fact]
    public void Detect_MailtoLink_ReturnsEmailAction()
    {
        var actions = ContentActionDetector.Detect("Send email to mailto:info@test.com now.");

        actions.Should().ContainSingle();
        actions[0].Label.Should().Be("Email");
        actions[0].Url.Should().Be("info@test.com");
    }

    [Fact]
    public void Detect_PhoneNumber_ReturnsCallAction()
    {
        var actions = ContentActionDetector.Detect("Call us at +1-555-123-4567.");

        actions.Should().ContainSingle();
        actions[0].Label.Should().Be("Call");
        actions[0].Url.Should().Contain("555");
    }

    [Fact]
    public void Detect_TelLink_ReturnsCallAction()
    {
        var actions = ContentActionDetector.Detect("Dial tel:+18005551234 for support.");

        actions.Should().ContainSingle();
        actions[0].Label.Should().Be("Call");
    }

    [Fact]
    public void Detect_PlainText_ReturnsNoActions()
    {
        var actions = ContentActionDetector.Detect("The weather is nice today.");

        actions.Should().BeEmpty();
    }

    [Fact]
    public void Detect_MixedContent_ReturnsMultipleActionTypes()
    {
        var actions = ContentActionDetector.Detect(
            "Visit https://shop.com or email sales@shop.com or call +1-800-555-0199.");

        actions.Should().HaveCount(3);
        actions.Should().Contain(a => a.Label == "Open Link");
        actions.Should().Contain(a => a.Label == "Email");
        actions.Should().Contain(a => a.Label == "Call");
    }

    [Fact]
    public void Detect_UrlWithTrailingPunctuation_TrimsCorrectly()
    {
        var actions = ContentActionDetector.Detect("See https://example.com/path.");

        actions.Should().ContainSingle();
        actions[0].Url.Should().Be("https://example.com/path");
    }

    [Fact]
    public void Detect_AllActionsHaveCommands()
    {
        var actions = ContentActionDetector.Detect(
            "https://a.com mailto:b@c.com tel:+1234567890");

        foreach (var action in actions)
            action.Command.Should().NotBeNull();
    }

    [Fact]
    public void Detect_HttpUrl_Works()
    {
        var actions = ContentActionDetector.Detect("Go to http://insecure.example.com now.");

        actions.Should().ContainSingle();
        actions[0].Url.Should().Be("http://insecure.example.com");
    }
}
