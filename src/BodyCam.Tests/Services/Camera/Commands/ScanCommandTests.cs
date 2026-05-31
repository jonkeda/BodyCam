using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Services.Camera.Commands;
using BodyCam.Services.QrCode;
using BodyCam.Services.QrCode.Handlers;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Services.Camera.Commands;

public class ScanCommandTests
{
    [Fact]
    public async Task ExecuteAsync_WithQrFrame_ReturnsClassifiedContent()
    {
        var scanner = Substitute.For<IQrCodeScanner>();
        scanner.ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new QrScanResult("https://example.com", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));
        var history = new QrCodeService();
        var command = new ScanCommand(scanner, history, CreateResolver());

        var result = await command.ExecuteAsync(CreateContext(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.TranscriptText.Should().Contain("Website");
        result.Data.Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>();
        var data = (IReadOnlyDictionary<string, object?>)result.Data!;
        data["found"].Should().Be(true);
        data["content_type"].Should().Be("url");
        history.LastResult.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_NoCode_ReturnsNotFound()
    {
        var scanner = Substitute.For<IQrCodeScanner>();
        scanner.ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns((QrScanResult?)null);
        var command = new ScanCommand(scanner, new QrCodeService(), CreateResolver());

        var result = await command.ExecuteAsync(CreateContext(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.TranscriptText.Should().Contain("No QR code");
        var data = (IReadOnlyDictionary<string, object?>)result.Data!;
        data["found"].Should().Be(false);
    }

    private static QrContentResolver CreateResolver() =>
        new([
            new UrlContentHandler(),
            new WifiContentHandler(),
            new PlainTextContentHandler(),
        ]);

    private static CameraCommandContext CreateContext() =>
        new(
            new CameraCommandRequest("scan", CameraCommandMode.FullAuto, CommandTriggerOrigin.Automation, null, null),
            CameraCommandMode.FullAuto,
            null!,
            CreateSettings(),
            _ => Task.FromResult<byte[]?>([0xFF, 0xD8]),
            _ => Task.FromResult<byte[]?>([0xFF, 0xD8]));

    private static ISettingsService CreateSettings()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.DefaultTouchCommandMode.Returns(CameraCommandMode.ManualAim);
        settings.DefaultLookDetailLevel.Returns(LookDetailLevel.Summary);
        settings.DefaultReadDetailLevel.Returns(ReadDetailLevel.Full);
        settings.ConfirmExternalScanActions.Returns(true);
        return settings;
    }
}
