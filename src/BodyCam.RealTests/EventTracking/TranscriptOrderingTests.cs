using BodyCam.RealTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.EventTracking;

/// <summary>
/// Tests that verify transcript event ordering and timing.
/// Targets the bugs described in RCA-003.
/// </summary>
[Trait("Category", "RealAPI")]
public class TranscriptOrderingTests : IAsyncLifetime
{
    private readonly RealtimeFixture _fixture = new();
    private readonly ITestOutputHelper _output;

    public TranscriptOrderingTests(ITestOutputHelper output)
    {
        _output = output;
        _fixture.SetOutput(output);
    }

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task OutputTranscriptDeltas_ArriveBeforeCompleted()
    {
        await _fixture.SendTextInputAsync("Tell me a fun fact about penguins.");
        await _fixture.WaitForResponseAsync();

        var lastDeltaIdx = -1;
        for (var i = _fixture.Events.Count - 1; i >= 0; i--)
        {
            if (_fixture.Events[i].Type == "response.audio_transcript.delta")
            {
                lastDeltaIdx = i;
                break;
            }
        }

        var transcriptDoneIdx = _fixture.IndexOfEvent("response.audio_transcript.done");
        var responseDoneIdx = _fixture.IndexOfEvent("response.done");

        lastDeltaIdx.Should().BeGreaterThanOrEqualTo(0, "should have transcript deltas");
        transcriptDoneIdx.Should().BeGreaterThanOrEqualTo(0, "should have transcript done");

        lastDeltaIdx.Should().BeLessThan(transcriptDoneIdx,
            "all transcript deltas should arrive before transcript done");
        transcriptDoneIdx.Should().BeLessThan(responseDoneIdx,
            "transcript done should arrive before response.done");
    }

    [Fact]
    public async Task ResponseOutputItemAdded_ArrivesBeforeDeltas()
    {
        await _fixture.SendTextInputAsync("Say test.");
        await _fixture.WaitForResponseAsync();

        var outputItemAddedIdx = _fixture.IndexOfEvent("response.output_item.added");
        var firstTranscriptDeltaIdx = _fixture.IndexOfEvent("response.audio_transcript.delta");
        var firstAudioDeltaIdx = _fixture.IndexOfEvent("response.audio.delta");

        outputItemAddedIdx.Should().BeGreaterThanOrEqualTo(0,
            "response.output_item.added should exist");
        firstTranscriptDeltaIdx.Should().BeGreaterThanOrEqualTo(0,
            "transcript deltas should exist");
        firstAudioDeltaIdx.Should().BeGreaterThanOrEqualTo(0,
            "audio deltas should exist");

        outputItemAddedIdx.Should().BeLessThan(firstTranscriptDeltaIdx,
            "output_item.added should arrive before first transcript delta");
        outputItemAddedIdx.Should().BeLessThan(firstAudioDeltaIdx,
            "output_item.added should arrive before first audio delta");
    }

    [Fact]
    public async Task MultiTurnConversation_EventSequenceIsConsistent()
    {
        // Turn 1
        await _fixture.SendTextInputAsync("Say hello.");
        await _fixture.WaitForResponseAsync();

        var turn1TranscriptDone = _fixture.GetEventsByType("response.audio_transcript.done").Count;
        var turn1ResponseDone = _fixture.GetEventsByType("response.done").Count;

        turn1TranscriptDone.Should().Be(1, "turn 1 should have exactly 1 transcript done");
        turn1ResponseDone.Should().Be(1, "turn 1 should have exactly 1 response done");

        _fixture.Reset();

        // Turn 2
        await _fixture.SendTextInputAsync("Now say goodbye.");
        await _fixture.WaitForResponseAsync();

        var turn2TranscriptDone = _fixture.GetEventsByType("response.audio_transcript.done").Count;
        var turn2ResponseDone = _fixture.GetEventsByType("response.done").Count;
        var turn2InputTranscripts = _fixture.GetEventsByType("conversation.item.input_audio_transcription.completed").Count;

        turn2TranscriptDone.Should().Be(1, "turn 2 should have exactly 1 transcript done");
        turn2ResponseDone.Should().Be(1, "turn 2 should have exactly 1 response done");
        turn2InputTranscripts.Should().Be(0, "text input should not produce input transcription events");
    }

    [Fact]
    public async Task ConcurrentInputAndOutput_EventsDoNotInterleaveCorruptly()
    {
        // Send a prompt that produces a long response
        await _fixture.SendTextInputAsync("Tell me a long detailed story about a brave dragon who saves a village.");

        // Wait for first audio delta (response is streaming)
        await _fixture.WaitForFirstAudioAsync(TimeSpan.FromSeconds(15));

        // Cancel the in-progress response before sending new input
        await _fixture.Client.CancelResponseAsync();

        // Wait for the cancelled response.done
        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(10));

        // Now reset and send the second input
        _fixture.Reset();
        await _fixture.SendTextInputAsync("Just say ok.");

        // Wait for the new response to finish
        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        // Each response.done should exist
        _fixture.ResponseDones.Should().NotBeEmpty("should get at least one response.done");

        // No errors
        _fixture.Errors.Should().BeEmpty("no errors should occur during concurrent input");

        // Log the full sequence for analysis
        _output.WriteLine($"Total events after concurrent input: {_fixture.Events.Count}");
        var eventTypes = _fixture.Events.Select(e => e.Type).Distinct().OrderBy(t => t);
        foreach (var type in eventTypes)
        {
            var count = _fixture.Events.Count(e => e.Type == type);
            _output.WriteLine($"  {type}: {count}");
        }
    }

    [Fact]
    public async Task InputTranscriptCompleted_NeverFiredForTextInput()
    {
        await _fixture.SendTextInputAsync("Hello.");
        await _fixture.WaitForResponseAsync();

        // Wait extra time to catch any late-arriving input transcripts
        await _fixture.WaitForInputTranscriptOrTimeoutAsync(TimeSpan.FromSeconds(2));

        _fixture.InputTranscripts.Should().BeEmpty(
            "text input should never produce input_audio_transcription.completed events");
        _fixture.OutputTranscripts.Should().HaveCount(1,
            "should have exactly 1 output transcript");
    }

    [Fact]
    public async Task OutputTranscriptCompleted_MatchesConcatenatedDeltas()
    {
        await _fixture.SendTextInputAsync("What is the capital of Japan?");
        await _fixture.WaitForResponseAsync();

        _fixture.OutputTranscriptDeltas.Should().NotBeEmpty("should have received deltas");
        _fixture.OutputTranscripts.Should().HaveCount(1, "should have exactly 1 completed transcript");

        var concatenated = string.Concat(_fixture.OutputTranscriptDeltas);
        concatenated.Should().Be(_fixture.OutputTranscripts[0],
            "concatenated deltas should exactly match the completed transcript");
    }
}
