using BodyCam.Services.Vision;
using FluentAssertions;
using Xunit;

namespace BodyCam.Tests.Services;

public class VisionPipelineTests
{
    [Fact]
    public async Task ExecuteAsync_RunsStagesInCostOrder()
    {
        var executionOrder = new List<string>();

        var expensive = new FakeStage("Expensive", cost: 100, result: null, executionOrder);
        var cheap = new FakeStage("Cheap", cost: 0, result: null, executionOrder);
        var medium = new FakeStage("Medium", cost: 10, result: null, executionOrder);

        // Register in wrong order — pipeline should sort by cost
        var pipeline = new VisionPipeline([expensive, cheap, medium]);

        await pipeline.ExecuteAsync([], null, CancellationToken.None);

        executionOrder.Should().ContainInOrder("Cheap", "Medium", "Expensive");
    }

    [Fact]
    public async Task ExecuteAsync_FirstNonNullResultWins()
    {
        var executionOrder = new List<string>();

        var stageA = new FakeStage("A", cost: 0, result: null, executionOrder);
        var stageB = new FakeStage("B", cost: 10,
            result: new VisionPipelineResult("B", "Found text", new() { ["text"] = "hello" }),
            executionOrder);
        var stageC = new FakeStage("C", cost: 100,
            result: new VisionPipelineResult("C", "Scene desc", new()),
            executionOrder);

        var pipeline = new VisionPipeline([stageA, stageB, stageC]);
        var result = await pipeline.ExecuteAsync([], null, CancellationToken.None);

        result.StageName.Should().Be("B");
        result.Summary.Should().Be("Found text");

        // Stage C should NOT have been called
        executionOrder.Should().ContainInOrder("A", "B");
        executionOrder.Should().NotContain("C");
    }

    [Fact]
    public async Task ExecuteAsync_FallbackWhenAllStagesReturnNull()
    {
        var stageA = new FakeStage("A", cost: 0, result: null);
        var stageB = new FakeStage("B", cost: 10, result: null);

        var pipeline = new VisionPipeline([stageA, stageB]);
        var result = await pipeline.ExecuteAsync([], null, CancellationToken.None);

        result.StageName.Should().Be("fallback");
    }

    [Fact]
    public async Task ExecuteAsync_FirstStageWins_SkipsRest()
    {
        var executionOrder = new List<string>();

        var stageA = new FakeStage("QR", cost: 0,
            result: new VisionPipelineResult("QR", "URL found", new() { ["content"] = "https://example.com" }),
            executionOrder);
        var stageB = new FakeStage("Text", cost: 10, result: null, executionOrder);
        var stageC = new FakeStage("Vision", cost: 100, result: null, executionOrder);

        var pipeline = new VisionPipeline([stageA, stageB, stageC]);
        var result = await pipeline.ExecuteAsync([], null, CancellationToken.None);

        result.StageName.Should().Be("QR");
        executionOrder.Should().Equal("QR");
    }

    [Fact]
    public void Stages_AreSortedByCost()
    {
        var stageA = new FakeStage("Expensive", cost: 100, result: null);
        var stageB = new FakeStage("Cheap", cost: 0, result: null);
        var stageC = new FakeStage("Medium", cost: 10, result: null);

        var pipeline = new VisionPipeline([stageA, stageB, stageC]);

        pipeline.Stages.Select(s => s.Name)
            .Should().ContainInOrder("Cheap", "Medium", "Expensive");
    }

    [Fact]
    public async Task ExecuteAsync_PassesFrameAndQueryToStage()
    {
        byte[]? capturedFrame = null;
        string? capturedQuery = null;

        var stage = new DelegateStage("Test", 0, (frame, query, _) =>
        {
            capturedFrame = frame;
            capturedQuery = query;
            return Task.FromResult<VisionPipelineResult?>(
                new VisionPipelineResult("Test", "ok", new()));
        });

        var pipeline = new VisionPipeline([stage]);
        var testFrame = new byte[] { 1, 2, 3 };

        await pipeline.ExecuteAsync(testFrame, "what is this?", CancellationToken.None);

        capturedFrame.Should().BeSameAs(testFrame);
        capturedQuery.Should().Be("what is this?");
    }

    // ── Test helpers ──

    private sealed class FakeStage : IVisionPipelineStage
    {
        private readonly VisionPipelineResult? _result;
        private readonly List<string>? _executionOrder;

        public string Name { get; }
        public int Cost { get; }

        public FakeStage(string name, int cost, VisionPipelineResult? result, List<string>? executionOrder = null)
        {
            Name = name;
            Cost = cost;
            _result = result;
            _executionOrder = executionOrder;
        }

        public Task<VisionPipelineResult?> ProcessAsync(byte[] jpegFrame, string? query, CancellationToken ct)
        {
            _executionOrder?.Add(Name);
            return Task.FromResult(_result);
        }
    }

    private sealed class DelegateStage : IVisionPipelineStage
    {
        private readonly Func<byte[], string?, CancellationToken, Task<VisionPipelineResult?>> _process;

        public string Name { get; }
        public int Cost { get; }

        public DelegateStage(string name, int cost,
            Func<byte[], string?, CancellationToken, Task<VisionPipelineResult?>> process)
        {
            Name = name;
            Cost = cost;
            _process = process;
        }

        public Task<VisionPipelineResult?> ProcessAsync(byte[] jpegFrame, string? query, CancellationToken ct)
            => _process(jpegFrame, query, ct);
    }
}
