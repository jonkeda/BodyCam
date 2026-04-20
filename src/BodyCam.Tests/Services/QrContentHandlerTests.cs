using BodyCam.Services.QrCode;
using BodyCam.Services.QrCode.Handlers;
using FluentAssertions;

namespace BodyCam.Tests.Services;

public class QrContentHandlerTests
{
    // --- UrlContentHandler ---

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("HTTP://EXAMPLE.COM")]
    public void UrlHandler_CanHandle_MatchesUrls(string content)
    {
        new UrlContentHandler().CanHandle(content).Should().BeTrue();
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("not a url")]
    public void UrlHandler_CanHandle_RejectsNonUrls(string content)
    {
        new UrlContentHandler().CanHandle(content).Should().BeFalse();
    }

    [Fact]
    public void UrlHandler_Summarize_ReturnsUrl()
    {
        var handler = new UrlContentHandler();
        var parsed = handler.Parse("https://example.com/menu");
        handler.Summarize(parsed).Should().Be("https://example.com/menu");
    }

    // --- WifiContentHandler ---

    [Fact]
    public void WifiHandler_CanHandle_MatchesWifi()
    {
        new WifiContentHandler().CanHandle("WIFI:S:MyNet;T:WPA;P:pass123;;").Should().BeTrue();
    }

    [Fact]
    public void WifiHandler_CanHandle_CaseInsensitive()
    {
        new WifiContentHandler().CanHandle("wifi:S:MyNet;;").Should().BeTrue();
    }

    [Fact]
    public void WifiHandler_CanHandle_RejectsOther()
    {
        new WifiContentHandler().CanHandle("https://example.com").Should().BeFalse();
    }

    [Fact]
    public void WifiHandler_Parse_ExtractsSsidAndPassword()
    {
        var handler = new WifiContentHandler();
        var parsed = handler.Parse("WIFI:S:CoffeeShop;T:WPA;P:latte123;;");

        parsed["ssid"].Should().Be("CoffeeShop");
        parsed["security"].Should().Be("WPA");
        parsed["password"].Should().Be("latte123");
    }

    [Fact]
    public void WifiHandler_Summarize_ReturnsSsidWithSecurity()
    {
        var handler = new WifiContentHandler();
        var parsed = handler.Parse("WIFI:S:CoffeeShop;T:WPA;P:latte123;;");
        handler.Summarize(parsed).Should().Be("CoffeeShop (WPA)");
    }

    [Fact]
    public void WifiHandler_Summarize_SsidOnlyIfNoSecurity()
    {
        var handler = new WifiContentHandler();
        var parsed = handler.Parse("WIFI:S:OpenNet;;");
        handler.Summarize(parsed).Should().Be("OpenNet");
    }

    // --- VCardContentHandler ---

    [Fact]
    public void VCardHandler_CanHandle_MatchesVCard()
    {
        new VCardContentHandler().CanHandle("BEGIN:VCARD\nFN:Jane").Should().BeTrue();
    }

    [Fact]
    public void VCardHandler_CanHandle_RejectsOther()
    {
        new VCardContentHandler().CanHandle("Hello World").Should().BeFalse();
    }

    [Fact]
    public void VCardHandler_Parse_ExtractsNameAndOrg()
    {
        var handler = new VCardContentHandler();
        var parsed = handler.Parse("BEGIN:VCARD\nFN:Jane Doe\nORG:Acme Corp\nTEL:+1234\nEMAIL:jane@acme.com\nEND:VCARD");

        parsed["name"].Should().Be("Jane Doe");
        parsed["organization"].Should().Be("Acme Corp");
        parsed["phone"].Should().Be("+1234");
        parsed["email"].Should().Be("jane@acme.com");
    }

    [Fact]
    public void VCardHandler_Summarize_NameWithOrg()
    {
        var handler = new VCardContentHandler();
        var parsed = handler.Parse("BEGIN:VCARD\nFN:Jane Doe\nORG:Acme Corp\nEND:VCARD");
        handler.Summarize(parsed).Should().Be("Jane Doe \u2014 Acme Corp");
    }

    // --- EmailContentHandler ---

    [Fact]
    public void EmailHandler_CanHandle_MatchesMailto()
    {
        new EmailContentHandler().CanHandle("mailto:test@example.com").Should().BeTrue();
    }

    [Fact]
    public void EmailHandler_CanHandle_RejectsOther()
    {
        new EmailContentHandler().CanHandle("test@example.com").Should().BeFalse();
    }

    [Fact]
    public void EmailHandler_Summarize_ReturnsAddress()
    {
        var handler = new EmailContentHandler();
        var parsed = handler.Parse("mailto:hello@example.com");
        handler.Summarize(parsed).Should().Be("hello@example.com");
    }

    // --- PhoneContentHandler ---

    [Fact]
    public void PhoneHandler_CanHandle_MatchesTel()
    {
        new PhoneContentHandler().CanHandle("tel:+15551234567").Should().BeTrue();
    }

    [Fact]
    public void PhoneHandler_CanHandle_RejectsOther()
    {
        new PhoneContentHandler().CanHandle("+15551234567").Should().BeFalse();
    }

    [Fact]
    public void PhoneHandler_Summarize_ReturnsNumber()
    {
        var handler = new PhoneContentHandler();
        var parsed = handler.Parse("tel:+15551234567");
        handler.Summarize(parsed).Should().Be("+15551234567");
    }

    // --- PlainTextContentHandler ---

    [Fact]
    public void PlainTextHandler_CanHandle_MatchesEverything()
    {
        new PlainTextContentHandler().CanHandle("anything at all").Should().BeTrue();
        new PlainTextContentHandler().CanHandle("https://url").Should().BeTrue();
    }

    [Fact]
    public void PlainTextHandler_Summarize_TruncatesLongText()
    {
        var handler = new PlainTextContentHandler();
        var longText = new string('x', 100);
        var parsed = handler.Parse(longText);
        var summary = handler.Summarize(parsed);
        summary.Should().HaveLength(81); // 80 + ellipsis
        summary.Should().EndWith("\u2026");
    }

    [Fact]
    public void PlainTextHandler_Summarize_ShortTextUnchanged()
    {
        var handler = new PlainTextContentHandler();
        var parsed = handler.Parse("short text");
        handler.Summarize(parsed).Should().Be("short text");
    }
}
