using BodyCam.RealTests.Fixtures;
using BodyCam.Services.QrCode;
using FluentAssertions;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;
using ZXing;
using ZXing.Common;

namespace BodyCam.RealTests.Pipeline;

/// <summary>
/// Real API tests for QR code scanning through the full orchestrator pipeline.
/// Validates that asking to scan triggers the scan_qr_code tool and returns decoded content.
/// </summary>
[Trait("Category", "RealAPI")]
public class QrCodeScanTests : IClassFixture<OrchestratorFixture>, IAsyncLifetime
{
    private readonly OrchestratorFixture _f;
    private readonly ITestOutputHelper _output;

    public QrCodeScanTests(OrchestratorFixture fixture, ITestOutputHelper output)
    {
        _f = fixture;
        _output = output;
    }

    public Task InitializeAsync()
    {
        _f.SetOutput(_output);
        _f.Reset();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AskToScan_WithQrCode_TriggersScanTool()
    {
        // Set camera frame to a QR code image containing a URL
        _f.FrameProvider.CurrentFrame = CreateQrCodeJpeg("https://example.com");

        await _f.Orchestrator.SendTextInputAsync(
            "Scan that QR code for me");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(45));

        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");
        _output.WriteLine($"Completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        _f.TranscriptCompletions.Should().NotBeEmpty(
            "model should produce a spoken response with the scan result");

        var completion = string.Join(" ", _f.TranscriptCompletions);
        completion.Should().ContainEquivalentOf("example.com",
            "response should mention the decoded URL content");
    }

    [Fact]
    public async Task AskToScan_WithQrCode_AddsToHistory()
    {
        _f.FrameProvider.CurrentFrame = CreateQrCodeJpeg("https://test.example.org/page");

        await _f.Orchestrator.SendTextInputAsync(
            "Scan the QR code I'm looking at");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(45));

        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");

        // QR content should be in history (via scan_qr_code or look tool)
        _f.QrHistory.LastResult.Should().NotBeNull(
            "successful scan should be recorded in QR history");

        _f.QrHistory.LastResult!.Content.Should().Be("https://test.example.org/page");
    }

    [Fact]
    public async Task AskToScan_NoQrCode_RespondGracefully()
    {
        // Default 1x1 white frame — no QR code present
        _f.FrameProvider.CurrentFrame = CreateBlankJpeg();

        await _f.Orchestrator.SendTextInputAsync(
            "Scan for any QR codes or barcodes");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(45));

        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");
        _output.WriteLine($"Completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        // Model may route to scan_qr_code, look, or another vision tool
        _f.TranscriptCompletions.Should().NotBeEmpty(
            "model should respond even when no QR code is detected");

        _f.Orchestrator.IsRunning.Should().BeTrue(
            "orchestrator should remain running after a failed scan");
    }

    [Fact]
    public async Task RecallLastScan_AfterScan_ReturnsContent()
    {
        // Turn 1: scan a QR code
        _f.FrameProvider.CurrentFrame = CreateQrCodeJpeg("https://recall-test.example.com");

        await _f.Orchestrator.SendTextInputAsync(
            "Scan this QR code");
        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(45));

        _output.WriteLine($"Turn 1 logs: {string.Join(" | ", _f.DebugLogs)}");

        _f.Reset();

        // Turn 2: ask to recall
        await _f.Orchestrator.SendTextInputAsync(
            "What was that QR code we just scanned?");
        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(30));

        _output.WriteLine($"Turn 2 logs: {string.Join(" | ", _f.DebugLogs)}");
        _output.WriteLine($"Turn 2 completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        var completion = string.Join(" ", _f.TranscriptCompletions);
        completion.Should().ContainEquivalentOf("recall-test",
            "model should recall the previously scanned URL");
    }

    [Fact]
    public async Task ScanWifiQrCode_ParsesCredentials()
    {
        _f.FrameProvider.CurrentFrame = CreateQrCodeJpeg("WIFI:S:MyNetwork;T:WPA;P:secret123;;");

        await _f.Orchestrator.SendTextInputAsync(
            "Scan the QR code in front of me");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(45));

        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");
        _output.WriteLine($"Completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        var completion = string.Join(" ", _f.TranscriptCompletions);
        completion.Should().ContainEquivalentOf("MyNetwork",
            "response should mention the WiFi network name");
    }

    [Fact]
    public async Task ScanQrCode_CameraUnavailable_HandledGracefully()
    {
        _f.Orchestrator.FrameCaptureFunc = _ => Task.FromResult<byte[]?>(null);

        await _f.Orchestrator.SendTextInputAsync(
            "Scan for QR codes please");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(30));

        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");
        _output.WriteLine($"Completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        _f.Orchestrator.IsRunning.Should().BeTrue(
            "orchestrator should remain running when camera is unavailable");

        _f.TranscriptCompletions.Should().NotBeEmpty(
            "model should respond gracefully when camera is unavailable");
    }

    // ── Helpers ──

    private static byte[] CreateQrCodeJpeg(string content)
    {
        var writer = new BarcodeWriterGeneric
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 400, Height = 400, Margin = 10 }
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
        using var bitmap = new SKBitmap(200, 200);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return data.ToArray();
    }
}
