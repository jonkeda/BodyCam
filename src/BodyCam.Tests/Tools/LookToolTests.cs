using BodyCam.Services.Vision;
using BodyCam.Tools;
using FluentAssertions;
using Xunit;

namespace BodyCam.Tests.Tools;

public class LookToolTests
{
    [Fact]
    public async Task ExecuteAsync_QrFound_ReturnsQrResult()
    {
        var qrResult = new VisionPipelineResult("QR Scan", "https://example.com", new()
        {
            ["found_type"] = "qr_barcode",
            ["content"] = "https://example.com",
        });

        var stage = new FakeStage("QR Scan", 0, qrResult);
        var pipeline = new VisionPipeline([stage]);
        var tool = new LookTool(pipeline);

        var frame = new byte[] { 0xFF, 0xD8 };
        var context = TestToolContext.Create(frame);

        var result = await tool.ExecuteAsync(null, context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("qr_barcode");
        result.Json.Should().Contain("example.com");
    }

    [Fact]
    public async Task ExecuteAsync_CameraUnavailable_Fails()
    {
        var pipeline = new VisionPipeline([]);
        var tool = new LookTool(pipeline);

        var context = TestToolContext.Create(null);

        var result = await tool.ExecuteAsync(null, context, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("Camera not available");
    }

    [Fact]
    public void Name_IsLook()
    {
        var pipeline = new VisionPipeline([]);
        var tool = new LookTool(pipeline);

        tool.Name.Should().Be("look");
    }

    [Fact]
    public void WakeWord_IsLook()
    {
        var pipeline = new VisionPipeline([]);
        var tool = new LookTool(pipeline);

        tool.WakeWord.Should().NotBeNull();
        tool.WakeWord!.KeywordPath.Should().Contain("look");
    }

    private sealed class FakeStage : IVisionPipelineStage
    {
        private readonly VisionPipelineResult? _result;
        public string Name { get; }
        public int Cost { get; }

        public FakeStage(string name, int cost, VisionPipelineResult? result)
        {
            Name = name;
            Cost = cost;
            _result = result;
        }

        public Task<VisionPipelineResult?> ProcessAsync(byte[] jpegFrame, string? query, CancellationToken ct)
            => Task.FromResult(_result);
    }
}

internal static class TestToolContext
{
    public static ToolContext Create(byte[]? frame) => new()
    {
        CaptureFrame = _ => Task.FromResult(frame),
        Session = new BodyCam.Models.SessionContext(),
        Log = _ => { },
    };
}
