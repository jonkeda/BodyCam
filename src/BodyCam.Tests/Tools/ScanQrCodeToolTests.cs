using BodyCam.Models;
using BodyCam.Services.QrCode;
using BodyCam.Services.QrCode.Handlers;
using BodyCam.Tools;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class ScanQrCodeToolTests
{
    private static (ScanQrCodeTool tool, IQrCodeScanner scanner) CreateTool()
    {
        var scanner = Substitute.For<IQrCodeScanner>();
        var history = new QrCodeService();
        IQrContentHandler[] handlers =
        [
            new UrlContentHandler(),
            new WifiContentHandler(),
            new PlainTextContentHandler(),
        ];
        var resolver = new QrContentResolver(handlers);
        return (new ScanQrCodeTool(scanner, history, resolver), scanner);
    }

    private static ToolContext CreateContext(byte[]? frame = null) => new()
    {
        CaptureFrame = _ => Task.FromResult(frame),
        Session = new SessionContext(),
        Log = _ => { },
    };

    [Fact]
    public async Task Execute_WithQrFrame_ReturnsContent()
    {
        var (tool, scanner) = CreateTool();
        scanner.ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new QrScanResult("https://example.com", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));

        var ctx = CreateContext(new byte[] { 0xFF, 0xD8 });
        var result = await tool.ExecuteAsync(null, ctx, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("\"found\":true");
        result.Json.Should().Contain("https://example.com");
        result.Json.Should().Contain("\"content_type\":\"url\"");
        result.Json.Should().Contain("suggested_actions");
    }

    [Fact]
    public async Task Execute_NoQrCode_ReturnsNotFound()
    {
        var (tool, scanner) = CreateTool();
        scanner.ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns((QrScanResult?)null);

        var ctx = CreateContext(new byte[] { 0xFF, 0xD8 });
        var result = await tool.ExecuteAsync(null, ctx, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("\"found\":false");
    }

    [Fact]
    public async Task Execute_CameraUnavailable_ReturnsError()
    {
        var (tool, _) = CreateTool();
        var ctx = CreateContext(frame: null);
        var result = await tool.ExecuteAsync(null, ctx, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("Camera not available");
    }

    [Fact]
    public async Task Execute_WifiQr_IncludesDetails()
    {
        var (tool, scanner) = CreateTool();
        scanner.ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new QrScanResult("WIFI:S:TestNet;T:WPA;P:pass123;;", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));

        var ctx = CreateContext(new byte[] { 0xFF, 0xD8 });
        var result = await tool.ExecuteAsync(null, ctx, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("\"content_type\":\"wifi\"");
        result.Json.Should().Contain("TestNet");
    }

    [Fact]
    public void Name_IsScanQrCode()
    {
        var (tool, _) = CreateTool();
        tool.Name.Should().Be("scan_qr_code");
    }

    [Fact]
    public async Task Execute_AddsToHistory()
    {
        var scanner = Substitute.For<IQrCodeScanner>();
        var history = new QrCodeService();
        var resolver = new QrContentResolver([new PlainTextContentHandler()]);
        var tool = new ScanQrCodeTool(scanner, history, resolver);

        scanner.ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new QrScanResult("test content", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));

        var ctx = CreateContext(new byte[] { 0xFF, 0xD8 });
        await tool.ExecuteAsync(null, ctx, CancellationToken.None);

        history.LastResult.Should().NotBeNull();
        history.LastResult!.Content.Should().Be("test content");
    }
}
