using BodyCam.RealTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.EventTracking;

/// <summary>
/// Tests that verify event ordering and detect duplicate events.
/// Targets the known duplicate transcription bug.
/// </summary>
[Trait("Category", "RealAPI")]
public class EventSequenceTests : IAsyncLifetime
{
    private readonly RealtimeFixture _fixture = new();
    private readonly ITestOutputHelper _output;

    public EventSequenceTests(ITestOutputHelper output)
    {
        _output = output;
        _fixture.SetOutput(output);
    }

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task OutputTranscription_NoDuplicates()
    {
        await _fixture.SendTextInputAsync("Tell me a short joke.");
        await _fixture.WaitForResponseAsync();

        _fixture.OutputTranscripts.Should().HaveCount(1,
            "OutputTranscriptCompleted should fire exactly once per response");

        // Also check raw events
        var rawTranscriptDone = _fixture.GetEventsByType("response.audio_transcript.done");
        rawTranscriptDone.Should().HaveCount(1,
            "response.audio_transcript.done should appear exactly once");
    }

    [Fact]
    public async Task ResponseDone_FiresExactlyOnce()
    {
        await _fixture.SendTextInputAsync("Say ok.");
        await _fixture.WaitForResponseAsync();

        // Wait a bit to catch any late duplicates
        await Task.Delay(2000);

        _fixture.ResponseDones.Should().HaveCount(1,
            "response.done should fire exactly once per request");
    }

    [Fact]
    public async Task EventOrdering_ResponseCreatedBeforeAudio()
    {
        await _fixture.SendTextInputAsync("Say hello.");
        await _fixture.WaitForResponseAsync();

        var responseCreatedIdx = _fixture.IndexOfEvent("response.created");
        var firstAudioIdx = _fixture.IndexOfEvent("response.audio.delta");
        var responseDoneIdx = _fixture.IndexOfEvent("response.done");

        responseCreatedIdx.Should().BeGreaterThanOrEqualTo(0, "response.created should exist");
        firstAudioIdx.Should().BeGreaterThanOrEqualTo(0, "audio deltas should exist");
        responseDoneIdx.Should().BeGreaterThanOrEqualTo(0, "response.done should exist");

        responseCreatedIdx.Should().BeLessThan(firstAudioIdx,
            "response.created should come before response.audio.delta");
        firstAudioIdx.Should().BeLessThan(responseDoneIdx,
            "response.audio.delta should come before response.done");
    }

    [Fact]
    public async Task EventOrdering_TranscriptDoneBeforeResponseDone()
    {
        await _fixture.SendTextInputAsync("Say goodbye.");
        await _fixture.WaitForResponseAsync();

        var transcriptDoneIdx = _fixture.IndexOfEvent("response.audio_transcript.done");
        var responseDoneIdx = _fixture.IndexOfEvent("response.done");

        transcriptDoneIdx.Should().BeGreaterThanOrEqualTo(0, "transcript done should exist");
        responseDoneIdx.Should().BeGreaterThanOrEqualTo(0, "response.done should exist");

        transcriptDoneIdx.Should().BeLessThan(responseDoneIdx,
            "response.audio_transcript.done should come before response.done");
    }

    [Fact]
    public async Task ResponseOutputItemAdded_Exists()
    {
        await _fixture.SendTextInputAsync("Say test.");
        await _fixture.WaitForResponseAsync();

        var outputItemAdded = _fixture.GetEventsByType("response.output_item.added");
        outputItemAdded.Should().NotBeEmpty(
            "response.output_item.added should be sent by the API and contains the item ID needed for truncation");

        _output.WriteLine($"Found {outputItemAdded.Count} response.output_item.added events");
        foreach (var (type, json) in outputItemAdded)
        {
            _output.WriteLine(json);
        }
    }

    [Fact]
    public async Task NoUnhandledErrors()
    {
        await _fixture.SendTextInputAsync("What is the capital of France?");
        await _fixture.WaitForResponseAsync();

        _fixture.Errors.Should().BeEmpty("no errors should occur during a normal request");
    }

    [Fact]
    public async Task AllEventTypesLogged()
    {
        await _fixture.SendTextInputAsync("Say hi.");
        await _fixture.WaitForResponseAsync();

        var eventTypes = _fixture.Events.Select(e => e.Type).Distinct().OrderBy(t => t).ToList();
        _output.WriteLine("Event types received:");
        foreach (var type in eventTypes)
        {
            var count = _fixture.Events.Count(e => e.Type == type);
            _output.WriteLine($"  {type}: {count}");
        }

        // Basic sanity — we should see these core event types
        eventTypes.Should().Contain("response.created");
        eventTypes.Should().Contain("response.done");
        eventTypes.Should().Contain("response.audio.delta");
    }
}
