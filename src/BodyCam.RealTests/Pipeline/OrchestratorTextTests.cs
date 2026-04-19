using BodyCam.RealTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Pipeline;

/// <summary>
/// Step 2 — Text-input round-trip tests through the full orchestrator pipeline.
/// Validates transcript reception, audio output, context retention, and session stability.
/// </summary>
[Trait("Category", "RealAPI")]
public class OrchestratorTextTests : IClassFixture<OrchestratorFixture>, IAsyncLifetime
{
    private readonly OrchestratorFixture _f;
    private readonly ITestOutputHelper _output;

    public OrchestratorTextTests(OrchestratorFixture fixture, ITestOutputHelper output)
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
    public async Task SendText_ReceivesTranscript()
    {
        await _f.Orchestrator.SendTextInputAsync("Say the word hello");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(30));

        _f.TranscriptCompletions.Should().NotBeEmpty("model should produce a transcript completion");

        var completion = string.Join(" ", _f.TranscriptCompletions);
        _output.WriteLine($"Transcript: {completion}");

        completion.Should().ContainEquivalentOf("hello",
            "model was asked to say 'hello' and should include it in the response");
    }

    [Fact]
    public async Task SendText_ReceivesAudio()
    {
        await _f.Orchestrator.SendTextInputAsync("Count to three");

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(30));

        // 1 second of 24kHz PCM16 mono = 48000 bytes
        await _f.WaitUntil(() => _f.AudioOutput.TotalBytes > 48000, TimeSpan.FromSeconds(30));

        _output.WriteLine($"Audio bytes received: {_f.AudioOutput.TotalBytes}");
        _output.WriteLine($"Audio chunks: {_f.AudioOutput.Chunks.Count}");

        _f.AudioOutput.TotalBytes.Should().BeGreaterThan(48000,
            "counting to three should produce at least 1 second of 24kHz PCM16 audio");
    }

    [Fact]
    public async Task MultipleTurns_MaintainContext()
    {
        // Turn 1: establish context
        await _f.Orchestrator.SendTextInputAsync("My name is TestBot");
        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(30));

        _output.WriteLine($"Turn 1 transcript: {string.Join(" ", _f.TranscriptCompletions)}");

        // Reset captured events but keep the session alive (context persists in the model)
        _f.Reset();

        // Turn 2: recall context
        await _f.Orchestrator.SendTextInputAsync("What is my name?");
        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(30));

        var completion = string.Join(" ", _f.TranscriptCompletions);
        _output.WriteLine($"Turn 2 transcript: {completion}");

        completion.Should().ContainEquivalentOf("TestBot",
            "model should remember the name from the previous turn");
    }

    [Fact]
    public async Task AfterResponse_OrchestratorStillRunning()
    {
        await _f.Orchestrator.SendTextInputAsync("Hi");
        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(30));

        _output.WriteLine($"Transcript: {string.Join(" ", _f.TranscriptCompletions)}");

        _f.Orchestrator.IsRunning.Should().BeTrue(
            "orchestrator should remain running after completing a response");
    }
}
