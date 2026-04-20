using BodyCam.Models;
using BodyCam.Services.QrCode;
using BodyCam.Services.Vision;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace BodyCam.Tests.Services;

public class QrScanStageTests
{
    [Fact]
    public async Task ProcessAsync_QrFound_ReturnsResult()
    {
        var scanner = Substitute.For<IQrCodeScanner>();
        scanner.ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new QrScanResult("https://example.com", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));

        var history = new QrCodeService();
        var resolver = new QrContentResolver([
            new BodyCam.Services.QrCode.Handlers.UrlContentHandler(),
            new BodyCam.Services.QrCode.Handlers.PlainTextContentHandler(),
        ]);

        var stage = new QrScanStage(scanner, history, resolver);
        var result = await stage.ProcessAsync([0xFF], null, CancellationToken.None);

        result.Should().NotBeNull();
        result!.StageName.Should().Be("QR Scan");
        result.Details["found_type"].Should().Be("qr_barcode");
        result.Details["content"].Should().Be("https://example.com");
        result.Details["content_type"].Should().Be("url");
    }

    [Fact]
    public async Task ProcessAsync_NoQr_ReturnsNull()
    {
        var scanner = Substitute.For<IQrCodeScanner>();
        scanner.ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns((QrScanResult?)null);

        var history = new QrCodeService();
        var resolver = new QrContentResolver([
            new BodyCam.Services.QrCode.Handlers.PlainTextContentHandler(),
        ]);

        var stage = new QrScanStage(scanner, history, resolver);
        var result = await stage.ProcessAsync([0xFF], null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_QrFound_AddsToHistory()
    {
        var scanner = Substitute.For<IQrCodeScanner>();
        scanner.ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new QrScanResult("tel:+15551234567", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));

        var history = new QrCodeService();
        var resolver = new QrContentResolver([
            new BodyCam.Services.QrCode.Handlers.PhoneContentHandler(),
            new BodyCam.Services.QrCode.Handlers.PlainTextContentHandler(),
        ]);

        var stage = new QrScanStage(scanner, history, resolver);
        await stage.ProcessAsync([0xFF], null, CancellationToken.None);

        history.LastResult.Should().NotBeNull();
        history.LastResult!.Content.Should().Be("tel:+15551234567");
    }

    [Fact]
    public void Cost_IsZero()
    {
        var stage = new QrScanStage(
            Substitute.For<IQrCodeScanner>(),
            new QrCodeService(),
            new QrContentResolver([new BodyCam.Services.QrCode.Handlers.PlainTextContentHandler()]));

        stage.Cost.Should().Be(0);
        stage.Name.Should().Be("QR Scan");
    }
}
