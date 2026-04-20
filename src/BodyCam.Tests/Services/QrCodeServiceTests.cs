using BodyCam.Models;
using BodyCam.Services.QrCode;
using FluentAssertions;

namespace BodyCam.Tests.Services;

public class QrCodeServiceTests
{
    [Fact]
    public void LastResult_Empty_ReturnsNull()
    {
        var service = new QrCodeService();
        service.LastResult.Should().BeNull();
    }

    [Fact]
    public void Add_ThenLastResult_ReturnsLatest()
    {
        var service = new QrCodeService();
        var r1 = new QrScanResult("first", QrCodeFormat.QrCode, DateTimeOffset.UtcNow);
        var r2 = new QrScanResult("second", QrCodeFormat.QrCode, DateTimeOffset.UtcNow);

        service.Add(r1);
        service.Add(r2);

        service.LastResult.Should().Be(r2);
    }

    [Fact]
    public void Add_BeyondMaxHistory_DropOldest()
    {
        var service = new QrCodeService();
        for (int i = 0; i < 25; i++)
            service.Add(new QrScanResult($"item-{i}", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));

        var history = service.GetHistory();
        history.Should().HaveCount(20);
        history[0].Content.Should().Be("item-5");
        history[^1].Content.Should().Be("item-24");
    }

    [Fact]
    public void SearchHistory_MatchesSubstring()
    {
        var service = new QrCodeService();
        service.Add(new QrScanResult("https://example.com", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));
        service.Add(new QrScanResult("Hello World", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));
        service.Add(new QrScanResult("https://test.com", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));

        var results = service.SearchHistory("https");
        results.Should().HaveCount(2);
    }

    [Fact]
    public void SearchHistory_CaseInsensitive()
    {
        var service = new QrCodeService();
        service.Add(new QrScanResult("HELLO", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));

        var results = service.SearchHistory("hello");
        results.Should().HaveCount(1);
    }

    [Fact]
    public void SearchHistory_NoMatch_ReturnsEmpty()
    {
        var service = new QrCodeService();
        service.Add(new QrScanResult("something", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));

        var results = service.SearchHistory("xyz");
        results.Should().BeEmpty();
    }
}
