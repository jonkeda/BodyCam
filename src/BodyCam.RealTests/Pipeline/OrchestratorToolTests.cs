using BodyCam.RealTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Pipeline;

/// <summary>
/// Step 3 — Tool invocation tests through the full orchestrator pipeline.
/// Validates that natural-language prompts trigger the correct tools and produce responses.
/// </summary>
[Trait("Category", "RealAPI")]
public class OrchestratorToolTests : IClassFixture<OrchestratorFixture>, IAsyncLifetime
{
    private readonly OrchestratorFixture _f;
    private readonly ITestOutputHelper _output;

    public OrchestratorToolTests(OrchestratorFixture fixture, ITestOutputHelper output)
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
    public async Task AskWhatYouSee_VisionAgentExecutes()
    {
        // FrameProvider.CurrentFrame has a default minimal frame — no setup needed
        await _f.Orchestrator.SendTextInputAsync("What do you see right now?");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(45));

        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");
        _output.WriteLine($"Completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        _f.DebugLogs.Should().Contain(
            l => l.Contains("describe_scene", StringComparison.OrdinalIgnoreCase),
            "asking 'what do you see' should trigger the describe_scene tool");

        _f.TranscriptCompletions.Should().NotBeEmpty(
            "model should produce a spoken response after vision analysis");
    }

    [Fact]
    public async Task SaveAndRecallMemory_Persists()
    {
        // Turn 1: save a memory
        await _f.Orchestrator.SendTextInputAsync(
            "Remember this: the suspect vehicle plate is XYZ-789");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(30));

        _output.WriteLine($"Save turn logs: {string.Join(" | ", _f.DebugLogs)}");
        _output.WriteLine($"Save turn completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        _f.DebugLogs.Should().Contain(
            l => l.Contains("save_memory", StringComparison.OrdinalIgnoreCase),
            "asking to remember should trigger the save_memory tool");

        // Reset captured events, keep session + memory store alive
        _f.Reset();

        // Turn 2: recall the memory
        await _f.Orchestrator.SendTextInputAsync(
            "What was the license plate number I told you about?");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(30));

        _output.WriteLine($"Recall turn logs: {string.Join(" | ", _f.DebugLogs)}");
        _output.WriteLine($"Recall turn completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        _f.DebugLogs.Should().Contain(
            l => l.Contains("recall_memory", StringComparison.OrdinalIgnoreCase),
            "asking to recall should trigger the recall_memory tool");

        var completion = string.Join(" ", _f.TranscriptCompletions);
        completion.Should().ContainEquivalentOf("XYZ-789",
            "model should recall the license plate from memory");
    }

    [Fact]
    public async Task ReadText_VisionFocusesOnText()
    {
        await _f.Orchestrator.SendTextInputAsync("Can you read that sign for me?");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(45));

        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");
        _output.WriteLine($"Completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        _f.DebugLogs.Should().Contain(
            l => l.Contains("read_text", StringComparison.OrdinalIgnoreCase),
            "asking to read a sign should trigger the read_text tool");

        _f.TranscriptCompletions.Should().NotBeEmpty(
            "model should produce a response after reading text");
    }

    [Fact]
    public async Task DeepAnalysis_ConversationAgentExecutes()
    {
        await _f.Orchestrator.SendTextInputAsync(
            "Do a deep analysis of what security risks exist at a parking garage");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(60));

        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");

        var completion = string.Join(" ", _f.TranscriptCompletions);
        _output.WriteLine($"Completion ({completion.Length} chars): {completion}");

        _f.DebugLogs.Should().Contain(
            l => l.Contains("deep_analysis", StringComparison.OrdinalIgnoreCase),
            "asking for deep analysis should trigger the deep_analysis tool");

        completion.Length.Should().BeGreaterThan(50,
            "a deep analysis response should be substantive (>50 chars)");
    }

    [Fact]
    public async Task FindObject_VisionSearchesForObject()
    {
        await _f.Orchestrator.SendTextInputAsync("Can you find any cars in the scene?");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(45));

        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");
        _output.WriteLine($"Completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        _f.DebugLogs.Should().Contain(
            l => l.Contains("find_object", StringComparison.OrdinalIgnoreCase),
            "asking to find cars should trigger the find_object tool");
    }

    [Fact]
    public async Task DescribeScene_CameraUnavailable_HandledGracefully()
    {
        // Force camera to return null (no frame available)
        _f.Orchestrator.FrameCaptureFunc = _ => Task.FromResult<byte[]?>(null);

        await _f.Orchestrator.SendTextInputAsync("What do you see?");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(30));

        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");
        _output.WriteLine($"Completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        _f.Orchestrator.IsRunning.Should().BeTrue(
            "orchestrator should remain running even when camera is unavailable");

        _f.TranscriptCompletions.Should().NotBeEmpty(
            "model should respond gracefully when camera frame is unavailable");
    }
}
