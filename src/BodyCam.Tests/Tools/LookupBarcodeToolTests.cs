using BodyCam.Models;
using BodyCam.Services.Barcode;
using BodyCam.Services.QrCode;
using BodyCam.Tools;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class LookupBarcodeToolTests
{
    private static (LookupBarcodeTool tool, IQrCodeScanner scanner, IBarcodeLookupService lookup) CreateTool()
    {
        var scanner = Substitute.For<IQrCodeScanner>();
        var lookup = Substitute.For<IBarcodeLookupService>();
        return (new LookupBarcodeTool(scanner, lookup), scanner, lookup);
    }

    private static ToolContext CreateContext(byte[]? frame = null) => new()
    {
        CaptureFrame = _ => Task.FromResult(frame),
        Session = new SessionContext(),
        Log = _ => { },
    };

    [Fact]
    public async Task Execute_ScansAndLooksUp_WhenBarcodeFound()
    {
        var (tool, scanner, lookup) = CreateTool();
        scanner.ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new QrScanResult("4006420012345", QrCodeFormat.Ean13, DateTimeOffset.UtcNow));
        lookup.LookupAsync("4006420012345", Arg.Any<CancellationToken>())
            .Returns(new ProductInfo
            {
                Barcode = "4006420012345",
                Source = "openfoodfacts",
                Name = "Mineral Water",
                Brand = "TestBrand",
                NutriScoreGrade = "a",
                EnergyKcal = 0,
            });

        var result = await tool.ExecuteAsync(null, CreateContext(new byte[] { 0xFF }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("\"found\":true");
        result.Json.Should().Contain("Mineral Water");
        result.Json.Should().Contain("TestBrand");
        result.Json.Should().Contain("\"nutri_score\":\"A\"");
    }

    [Fact]
    public async Task Execute_UsesProvidedBarcode_SkipsScan()
    {
        var (tool, scanner, lookup) = CreateTool();
        lookup.LookupAsync("999", Arg.Any<CancellationToken>())
            .Returns(new ProductInfo { Barcode = "999", Source = "test", Name = "Provided" });

        var args = System.Text.Json.JsonSerializer.SerializeToElement(new { barcode = "999" });
        var result = await tool.ExecuteAsync(args, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("Provided");
        await scanner.DidNotReceive().ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_NoBarcodeDetected_Fails()
    {
        var (tool, scanner, _) = CreateTool();
        scanner.ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns((QrScanResult?)null);

        var result = await tool.ExecuteAsync(null, CreateContext(new byte[] { 0xFF }), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("No barcode detected");
    }

    [Fact]
    public async Task Execute_QrCodeFormat_RejectsWithMessage()
    {
        var (tool, scanner, _) = CreateTool();
        scanner.ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new QrScanResult("https://example.com", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));

        var result = await tool.ExecuteAsync(null, CreateContext(new byte[] { 0xFF }), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("not a product barcode");
    }

    [Fact]
    public async Task Execute_CameraUnavailable_Fails()
    {
        var (tool, _, _) = CreateTool();
        var result = await tool.ExecuteAsync(null, CreateContext(frame: null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("Camera not available");
    }

    [Fact]
    public async Task Execute_LookupReturnsNull_ReturnsNotFound()
    {
        var (tool, scanner, lookup) = CreateTool();
        scanner.ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new QrScanResult("4006420012345", QrCodeFormat.Ean13, DateTimeOffset.UtcNow));
        lookup.LookupAsync("4006420012345", Arg.Any<CancellationToken>())
            .Returns((ProductInfo?)null);

        var result = await tool.ExecuteAsync(null, CreateContext(new byte[] { 0xFF }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("\"found\":false");
        result.Json.Should().Contain("4006420012345");
    }

    [Fact]
    public async Task Execute_WithPricing_IncludesPriceRange()
    {
        var (tool, _, lookup) = CreateTool();
        lookup.LookupAsync("999", Arg.Any<CancellationToken>())
            .Returns(new ProductInfo
            {
                Barcode = "999",
                Source = "upcitemdb",
                Name = "Gadget",
                LowestPrice = 12.99m,
                HighestPrice = 19.99m,
                Currency = "USD",
            });

        var args = System.Text.Json.JsonSerializer.SerializeToElement(new { barcode = "999" });
        var result = await tool.ExecuteAsync(args, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("price_range");
        result.Json.Should().Contain("USD");
    }

    [Fact]
    public void Name_IsLookupBarcode()
    {
        var (tool, _, _) = CreateTool();
        tool.Name.Should().Be("lookup_barcode");
    }
}
