using BodyCam.Models;
using BodyCam.Services.QrCode;
using FluentAssertions;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace BodyCam.Tests.Services;

public class ZXingQrScannerTests
{
    private readonly ZXingQrScanner _scanner = new();

    private static byte[] CreateQrCodeJpeg(string content)
    {
        var writer = new BarcodeWriterGeneric
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 300, Height = 300, Margin = 2 }
        };
        var matrix = writer.Encode(content);

        using var bitmap = new SKBitmap(matrix.Width, matrix.Height);
        for (int y = 0; y < matrix.Height; y++)
        for (int x = 0; x < matrix.Width; x++)
            bitmap.SetPixel(x, y, matrix[x, y] ? SKColors.Black : SKColors.White);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    private static byte[] CreateBarcodeJpeg(string content, BarcodeFormat format)
    {
        var writer = new BarcodeWriterGeneric
        {
            Format = format,
            Options = new EncodingOptions { Width = 600, Height = 300, Margin = 10 }
        };
        var matrix = writer.Encode(content);

        using var bitmap = new SKBitmap(matrix.Width, matrix.Height);
        for (int y = 0; y < matrix.Height; y++)
        for (int x = 0; x < matrix.Width; x++)
            bitmap.SetPixel(x, y, matrix[x, y] ? SKColors.Black : SKColors.White);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return data.ToArray();
    }

    private static byte[] CreateBlankJpeg()
    {
        using var bitmap = new SKBitmap(100, 100);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    [Fact]
    public async Task ScanAsync_WithQrCode_ReturnsContent()
    {
        var jpeg = CreateQrCodeJpeg("Hello World");
        var result = await _scanner.ScanAsync(jpeg);

        result.Should().NotBeNull();
        result!.Content.Should().Be("Hello World");
        result.Format.Should().Be(QrCodeFormat.QrCode);
    }

    [Fact]
    public async Task ScanAsync_WithUrl_ReturnsUrl()
    {
        var jpeg = CreateQrCodeJpeg("https://example.com");
        var result = await _scanner.ScanAsync(jpeg);

        result.Should().NotBeNull();
        result!.Content.Should().Be("https://example.com");
    }

    [Fact]
    public async Task ScanAsync_NoQrCode_ReturnsNull()
    {
        var jpeg = CreateBlankJpeg();
        var result = await _scanner.ScanAsync(jpeg);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ScanAsync_InvalidJpeg_ReturnsNull()
    {
        var result = await _scanner.ScanAsync([0x00, 0x01, 0x02]);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ScanAsync_EmptyArray_ReturnsNull()
    {
        var result = await _scanner.ScanAsync([]);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ScanAsync_Ean13_ReturnsContent()
    {
        var jpeg = CreateBarcodeJpeg("5901234123457", BarcodeFormat.EAN_13);
        var result = await _scanner.ScanAsync(jpeg);

        result.Should().NotBeNull();
        result!.Content.Should().Be("5901234123457");
        result.Format.Should().Be(QrCodeFormat.Ean13);
    }

    [Fact]
    public async Task ScanAsync_Code128_ReturnsContent()
    {
        var jpeg = CreateBarcodeJpeg("ABC-12345", BarcodeFormat.CODE_128);
        var result = await _scanner.ScanAsync(jpeg);

        result.Should().NotBeNull();
        result!.Content.Should().Be("ABC-12345");
        result.Format.Should().Be(QrCodeFormat.Code128);
    }

    [Fact]
    public async Task ScanAsync_SetsScannedAt()
    {
        var before = DateTimeOffset.UtcNow;
        var jpeg = CreateQrCodeJpeg("test");
        var result = await _scanner.ScanAsync(jpeg);

        result.Should().NotBeNull();
        result!.ScannedAt.Should().BeOnOrAfter(before);
        result.ScannedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }
}
