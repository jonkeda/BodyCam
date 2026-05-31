using BodyCam.Models;
using BodyCam.Services.Camera.Commands;
using BodyCam.Tools;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class ScanQrCodeToolTests
{
    private static ToolContext CreateContext() => new()
    {
        CaptureFrame = _ => Task.FromResult<byte[]?>(null),
        Session = new SessionContext(),
        Log = _ => { },
    };

    [Fact]
    public async Task ExecuteAsync_DelegatesToScanCommand()
    {
        var commands = Substitute.For<ICameraCommandService>();
        commands.ExecuteAsync(Arg.Any<CameraCommandRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CameraCommandResult(
                "scan",
                true,
                "Website: example.com",
                new Dictionary<string, object?>
                {
                    ["found"] = true,
                    ["content"] = "https://example.com",
                    ["content_type"] = "url",
                },
                null));

        var tool = new ScanQrCodeTool(commands);
        var result = await tool.ExecuteAsync(null, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("\"found\":true");
        result.Json.Should().Contain("https://example.com");
        await commands.Received(1).ExecuteAsync(
            Arg.Is<CameraCommandRequest>(r =>
                r.CommandId == "scan"
                && r.Origin == CommandTriggerOrigin.LlmToolCall),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NoCode_ReturnsNotFoundPayload()
    {
        var commands = Substitute.For<ICameraCommandService>();
        commands.ExecuteAsync(Arg.Any<CameraCommandRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CameraCommandResult(
                "scan",
                true,
                "No QR code or barcode detected.",
                new Dictionary<string, object?>
                {
                    ["found"] = false,
                    ["message"] = "No QR code or barcode detected in the image.",
                },
                null));

        var tool = new ScanQrCodeTool(commands);
        var result = await tool.ExecuteAsync(null, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("\"found\":false");
    }

    [Fact]
    public async Task ExecuteAsync_CommandFailure_ReturnsFail()
    {
        var commands = Substitute.For<ICameraCommandService>();
        commands.ExecuteAsync(Arg.Any<CameraCommandRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CameraCommandResult("scan", false, "Camera not available.", null, "Camera not available."));

        var tool = new ScanQrCodeTool(commands);
        var result = await tool.ExecuteAsync(null, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("Camera not available");
    }

    [Fact]
    public void Name_IsScanQrCode()
    {
        var tool = new ScanQrCodeTool(Substitute.For<ICameraCommandService>());

        tool.Name.Should().Be("scan_qr_code");
    }
}
