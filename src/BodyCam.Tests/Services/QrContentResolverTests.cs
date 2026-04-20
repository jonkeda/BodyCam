using BodyCam.Services.QrCode;
using BodyCam.Services.QrCode.Handlers;
using FluentAssertions;

namespace BodyCam.Tests.Services;

public class QrContentResolverTests
{
    private static QrContentResolver CreateResolver()
    {
        IQrContentHandler[] handlers =
        [
            new UrlContentHandler(),
            new WifiContentHandler(),
            new VCardContentHandler(),
            new EmailContentHandler(),
            new PhoneContentHandler(),
            new PlainTextContentHandler(), // fallback — must be last
        ];
        return new QrContentResolver(handlers);
    }

    [Theory]
    [InlineData("https://example.com", "url")]
    [InlineData("http://example.com", "url")]
    [InlineData("WIFI:S:Net;T:WPA;P:pass;;", "wifi")]
    [InlineData("BEGIN:VCARD\nFN:Jane\nEND:VCARD", "vcard")]
    [InlineData("mailto:test@example.com", "email")]
    [InlineData("tel:+1555", "phone")]
    [InlineData("just some text", "text")]
    public void Resolve_ReturnsCorrectHandler(string content, string expectedType)
    {
        var resolver = CreateResolver();
        var handler = resolver.Resolve(content);
        handler.ContentType.Should().Be(expectedType);
    }

    [Fact]
    public void Resolve_FallsBackToPlainText()
    {
        var resolver = CreateResolver();
        var handler = resolver.Resolve("random content 12345");
        handler.ContentType.Should().Be("text");
    }

    [Fact]
    public void Resolve_NoHandlers_Throws()
    {
        var resolver = new QrContentResolver([]);
        var act = () => resolver.Resolve("test");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Resolve_UrlBeforePlainText()
    {
        // Ensures URL handler wins over PlainText even though PlainText matches everything
        var resolver = CreateResolver();
        var handler = resolver.Resolve("https://example.com");
        handler.ContentType.Should().Be("url");
    }
}
