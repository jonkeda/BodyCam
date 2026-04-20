using BodyCam.Models;
using BodyCam.Services.QrCode;
using BodyCam.Services.QrCode.Handlers;
using BodyCam.Tools;
using FluentAssertions;

namespace BodyCam.Tests.Tools;

public class RecallLastScanToolTests
{
    private static (RecallLastScanTool tool, QrCodeService history) CreateTool()
    {
        var history = new QrCodeService();
        IQrContentHandler[] handlers =
        [
            new UrlContentHandler(),
            new PlainTextContentHandler(),
        ];
        var resolver = new QrContentResolver(handlers);
        return (new RecallLastScanTool(history, resolver), history);
    }

    private static ToolContext CreateContext() => new()
    {
        CaptureFrame = _ => Task.FromResult<byte[]?>(null),
        Session = new SessionContext(),
        Log = _ => { },
    };

    [Fact]
    public async Task Execute_EmptyHistory_ReturnsNotFound()
    {
        var (tool, _) = CreateTool();
        var result = await tool.ExecuteAsync(null, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("\"found\":false");
    }

    [Fact]
    public async Task Execute_WithHistory_ReturnsLastScan()
    {
        var (tool, history) = CreateTool();
        history.Add(new QrScanResult("https://example.com", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));

        var result = await tool.ExecuteAsync(null, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("\"found\":true");
        result.Json.Should().Contain("https://example.com");
        result.Json.Should().Contain("\"content_type\":\"url\"");
    }

    [Fact]
    public async Task Execute_ReturnsLatestScan()
    {
        var (tool, history) = CreateTool();
        history.Add(new QrScanResult("first", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));
        history.Add(new QrScanResult("second", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));

        var result = await tool.ExecuteAsync(null, CreateContext(), CancellationToken.None);

        result.Json.Should().Contain("second");
        result.Json.Should().NotContain("first");
    }

    [Fact]
    public void Name_IsRecallLastScan()
    {
        var (tool, _) = CreateTool();
        tool.Name.Should().Be("recall_last_scan");
    }
}
