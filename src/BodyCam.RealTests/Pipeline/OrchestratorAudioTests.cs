using BodyCam.RealTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Pipeline;

/// <summary>
/// Step 4 — Audio input and stress tests through the full orchestrator pipeline.
/// Audio-dependent tests are skipped until TTS-generated test files are available.
/// Text-based stress tests validate session stability under rapid and multi-turn usage.
/// </summary>
[Trait("Category", "RealAPI")]
public class OrchestratorAudioTests : IClassFixture<OrchestratorFixture>, IAsyncLifetime
{
    private readonly OrchestratorFixture _f;
    private readonly ITestOutputHelper _output;

    public OrchestratorAudioTests(OrchestratorFixture fixture, ITestOutputHelper output)
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

    private static readonly string TestDataDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "TestData");

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SendSpeechAudio_ModelRespondsWithAudio()
    {
        var pcm = await File.ReadAllBytesAsync(Path.Combine(TestDataDir, "hello_can_you_hear_me.pcm"));
        _f.AudioInput.SendSpeech(pcm, chunkSize: 4800, intervalMs: 100);

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(30));

        _output.WriteLine($"Completions: {string.Join(" | ", _f.TranscriptCompletions)}");
        _output.WriteLine($"Audio output bytes: {_f.AudioOutput.TotalBytes}");

        _f.TranscriptCompletions.Should().NotBeEmpty(
            "model should produce a transcript when speech audio is sent");
        _f.AudioOutput.TotalBytes.Should().BeGreaterThan(0,
            "model should produce audio output in response to speech");
    }

    [Fact]
    public async Task SendSpeechAudio_InputTranscriptReceived()
    {
        var pcm = await File.ReadAllBytesAsync(Path.Combine(TestDataDir, "hello_can_you_hear_me.pcm"));
        _f.AudioInput.SendSpeech(pcm, chunkSize: 4800, intervalMs: 100);

        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(30));

        _output.WriteLine($"Transcript deltas: {string.Join("", _f.TranscriptDeltas)}");
        _output.WriteLine($"Completions: {string.Join(" | ", _f.TranscriptCompletions)}");

        _f.TranscriptCompletions.Should().NotBeEmpty(
            "model should transcribe the incoming speech and respond");
    }

    [Fact]
    public async Task SpeechWhatDoYouSee_TriggersDescribeScene()
    {
        var pcm = await File.ReadAllBytesAsync(Path.Combine(TestDataDir, "what_do_you_see.pcm"));
        _f.AudioInput.SendSpeech(pcm, chunkSize: 4800, intervalMs: 100);

        // Wait up to 30s — collect whatever happens (speech recognition, tool call, or text response)
        var toolCalled = false;
        try
        {
            await _f.WaitUntil(
                () => _f.DebugLogs.Any(l => l.Contains("describe_scene", StringComparison.OrdinalIgnoreCase)),
                TimeSpan.FromSeconds(30));
            toolCalled = true;
        }
        catch (TimeoutException)
        {
            // Dump diagnostics before failing
            _output.WriteLine("=== TIMEOUT — describe_scene was NOT triggered ===");
        }

        _output.WriteLine($"Completions: {string.Join(" | ", _f.TranscriptCompletions)}");
        _output.WriteLine($"Deltas: {string.Join("", _f.TranscriptDeltas)}");
        _output.WriteLine($"Debug logs ({_f.DebugLogs.Count}):");
        foreach (var log in _f.DebugLogs)
            _output.WriteLine($"  {log}");
        _output.WriteLine($"Audio output bytes: {_f.AudioOutput.TotalBytes}");

        toolCalled.Should().BeTrue(
            "asking 'what do you see' via speech should trigger the describe_scene tool");
    }

    [Fact]
    public async Task RapidTextInputs_OrchestratorHandlesGracefully()
    {
        // Send three messages in rapid succession without waiting between them
        await _f.Orchestrator.SendTextInputAsync("One");
        await _f.Orchestrator.SendTextInputAsync("Two");
        await _f.Orchestrator.SendTextInputAsync("Three");

        // Wait for at least one completion to come through
        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(45));

        _output.WriteLine($"Completions ({_f.TranscriptCompletions.Count}): " +
            string.Join(" | ", _f.TranscriptCompletions));
        _output.WriteLine($"Debug logs: {string.Join(" | ", _f.DebugLogs)}");

        _f.TranscriptCompletions.Should().NotBeEmpty(
            "at least one rapid input should produce a transcript completion");

        _f.Orchestrator.IsRunning.Should().BeTrue(
            "orchestrator should remain stable after rapid input bursts");
    }

    [Fact]
    public async Task LongSession_MultipleToolCalls_SessionStable()
    {
        // Turn 1: trigger vision tool
        await _f.Orchestrator.SendTextInputAsync("Describe the scene");
        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(45));

        _output.WriteLine($"Turn 1 completions: {string.Join(" | ", _f.TranscriptCompletions)}");
        _output.WriteLine($"Turn 1 debug logs: {string.Join(" | ", _f.DebugLogs)}");

        _f.Reset();

        // Turn 2: trigger memory save
        await _f.Orchestrator.SendTextInputAsync("Remember there are 3 cars");
        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(30));

        _output.WriteLine($"Turn 2 completions: {string.Join(" | ", _f.TranscriptCompletions)}");
        _output.WriteLine($"Turn 2 debug logs: {string.Join(" | ", _f.DebugLogs)}");

        _f.Reset();

        // Turn 3: recall from memory
        await _f.Orchestrator.SendTextInputAsync("How many cars did I mention?");
        await _f.WaitForTranscriptCompletion(TimeSpan.FromSeconds(30));

        var lastCompletion = string.Join(" ", _f.TranscriptCompletions);
        _output.WriteLine($"Turn 3 completion: {lastCompletion}");
        _output.WriteLine($"Turn 3 debug logs: {string.Join(" | ", _f.DebugLogs)}");

        lastCompletion.Should().Contain("3",
            "model should recall that 3 cars were mentioned");

        _f.Orchestrator.IsRunning.Should().BeTrue(
            "orchestrator should remain running after a multi-turn session with tool calls");
    }
}
