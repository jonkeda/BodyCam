using BodyCam.Models;
using BodyCam.Services.Barcode;
using BodyCam.Services.QrCode;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Services;

public sealed class ProductBarcodeLookupWorkflowTests
{
    [Fact]
    public async Task LookupAsync_WithDetectedProductBarcode_ReturnsProduct()
    {
        var scanner = Substitute.For<IQrCodeScanner>();
        var lookup = Substitute.For<IBarcodeLookupService>();
        var workflow = new ProductBarcodeLookupWorkflow(scanner, lookup);
        var product = new ProductInfo
        {
            Barcode = "4006420012345",
            Source = "test",
            Name = "Mineral Water"
        };

        scanner.ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new QrScanResult(product.Barcode, QrCodeFormat.Ean13, DateTimeOffset.UtcNow));
        lookup.LookupAsync(product.Barcode, Arg.Any<CancellationToken>())
            .Returns(product);

        var result = await workflow.LookupAsync(_ => Task.FromResult<byte[]?>([0xFF]));

        result.Status.Should().Be(ProductBarcodeLookupStatus.Found);
        result.Found.Should().BeTrue();
        result.Product.Should().Be(product);
        result.Message.Should().Be("Mineral Water");
    }

    [Fact]
    public async Task LookupAsync_WithProvidedBarcode_SkipsScan()
    {
        var scanner = Substitute.For<IQrCodeScanner>();
        var lookup = Substitute.For<IBarcodeLookupService>();
        var workflow = new ProductBarcodeLookupWorkflow(scanner, lookup);
        var product = new ProductInfo
        {
            Barcode = "999",
            Source = "test",
            Name = "Provided"
        };

        lookup.LookupAsync("999", Arg.Any<CancellationToken>())
            .Returns(product);

        var result = await workflow.LookupAsync(_ => throw new InvalidOperationException(), " 999 ");

        result.Found.Should().BeTrue();
        result.Product.Should().Be(product);
        await scanner.DidNotReceive().ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LookupAsync_WithQrCode_ReturnsUnsupportedFormat()
    {
        var scanner = Substitute.For<IQrCodeScanner>();
        var lookup = Substitute.For<IBarcodeLookupService>();
        var workflow = new ProductBarcodeLookupWorkflow(scanner, lookup);

        scanner.ScanAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new QrScanResult("https://example.com", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));

        var result = await workflow.LookupAsync(_ => Task.FromResult<byte[]?>([0xFF]));

        result.Status.Should().Be(ProductBarcodeLookupStatus.UnsupportedFormat);
        result.Format.Should().Be(QrCodeFormat.QrCode);
        result.Message.Should().Contain("not a product barcode");
        await lookup.DidNotReceive().LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LookupAsync_WhenLookupReturnsNull_ReturnsNotFound()
    {
        var scanner = Substitute.For<IQrCodeScanner>();
        var lookup = Substitute.For<IBarcodeLookupService>();
        var workflow = new ProductBarcodeLookupWorkflow(scanner, lookup);

        lookup.LookupAsync("123", Arg.Any<CancellationToken>())
            .Returns((ProductInfo?)null);

        var result = await workflow.LookupAsync(_ => throw new InvalidOperationException(), "123");

        result.Status.Should().Be(ProductBarcodeLookupStatus.NotFound);
        result.Found.Should().BeFalse();
        result.Barcode.Should().Be("123");
        result.Message.Should().Contain("Product not found");
    }

    [Fact]
    public async Task LookupAsync_WhenCameraUnavailable_ReturnsCameraUnavailable()
    {
        var workflow = new ProductBarcodeLookupWorkflow(
            Substitute.For<IQrCodeScanner>(),
            Substitute.For<IBarcodeLookupService>());

        var result = await workflow.LookupAsync(_ => Task.FromResult<byte[]?>(null));

        result.Status.Should().Be(ProductBarcodeLookupStatus.CameraUnavailable);
        result.Found.Should().BeFalse();
    }
}
