using BodyCam.RealTests.Fixtures;
using FluentAssertions;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;
using ZXing;
using ZXing.Common;

namespace BodyCam.RealTests.Pipeline;

/// <summary>
/// Real API tests for the cascading vision pipeline (look tool).
/// Validates that "look" cascades: QR scan → text detection → scene description.
/// </summary>
[Trait("Category", "RealAPI")]
public class LookPipelineTests : IClassFixture<OrchestratorFixture>, IAsyncLifetime
{
    private readonly OrchestratorFixture _f;
    private readonly ITestOutputHelper _output;

    public LookPipelineTests(OrchestratorFixture fixture, ITestOutputHelper output)
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
    public async Task Look_WithQrCode_ReturnsQrResult()
    {
        _f.FrameProvider.CurrentFrame = CreateQrCodeJpeg("https://example.com/menu");

        await _f.Orchestrator.SendTextInputAsync("What's that?");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(45));

        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");
        _output.WriteLine($"Completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        _f.TranscriptCompletions.Should().NotBeEmpty();

        var completion = string.Join(" ", _f.TranscriptCompletions);
        completion.Should().ContainEquivalentOf("example.com",
            "pipeline should detect QR code and return its content");
    }

    [Fact]
    public async Task Look_WithQrCode_AddsToHistory()
    {
        _f.FrameProvider.CurrentFrame = CreateQrCodeJpeg("https://pipeline-test.example.com");

        await _f.Orchestrator.SendTextInputAsync("Look at that for me");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(45));

        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");

        // The model may route to look or scan_qr_code — both process QR codes
        _f.DebugLogs.Should().Contain(
            l => l.StartsWith("Tool:", StringComparison.OrdinalIgnoreCase),
            "a vision tool should be invoked");

        // Either look (QrScanStage) or scan_qr_code should add to history
        _f.QrHistory.LastResult.Should().NotBeNull(
            "QR pipeline should record scan to history");

        _f.QrHistory.LastResult!.Content.Should().Be("https://pipeline-test.example.com");
    }

    [Fact]
    public async Task Look_NoQrCode_FallsToVision()
    {
        _f.FrameProvider.CurrentFrame = CreateBlankJpeg();

        await _f.Orchestrator.SendTextInputAsync("What do you see?");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(45));

        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");
        _output.WriteLine($"Completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        // The model may route to look, describe_scene, or find_object — all handle blank scenes
        _f.DebugLogs.Should().Contain(
            l => l.StartsWith("Tool:", StringComparison.OrdinalIgnoreCase),
            "a vision tool should be invoked");

        _f.TranscriptCompletions.Should().NotBeEmpty(
            "pipeline should produce a description");

        _f.Orchestrator.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task Look_CameraUnavailable_HandledGracefully()
    {
        _f.Orchestrator.FrameCaptureFunc = _ => Task.FromResult<byte[]?>(null);

        await _f.Orchestrator.SendTextInputAsync("Look at that");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(30));

        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");
        _output.WriteLine($"Completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        _f.Orchestrator.IsRunning.Should().BeTrue();
        _f.TranscriptCompletions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DescribeScene_ReturnsStructuredAnalysis()
    {
        _f.FrameProvider.CurrentFrame = CreateBlankJpeg();

        await _f.Orchestrator.SendTextInputAsync("Describe the scene in detail");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(45));

        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");
        _output.WriteLine($"Completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        _f.DebugLogs.Should().Contain(
            l => l.Contains("describe_scene", StringComparison.OrdinalIgnoreCase),
            "asking to 'describe the scene' should trigger describe_scene (not look)");

        _f.TranscriptCompletions.Should().NotBeEmpty();
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
