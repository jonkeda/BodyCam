using BodyCam.RealTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Pipeline;

/// <summary>
/// Tests that send text input (no audio) and verify the response pipeline.
/// These are the fastest and most deterministic real API tests.
/// </summary>
[Trait("Category", "RealAPI")]
public class TextRoundTripTests : IAsyncLifetime
{
    private readonly RealtimeFixture _fixture = new();
    private readonly ITestOutputHelper _output;

    public TextRoundTripTests(ITestOutputHelper output)
    {
        _output = output;
        _fixture.SetOutput(output);
    }

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task SendTextInput_ReceivesAudioResponse()
    {
        await _fixture.SendTextInputAsync("What is 2 plus 2? Answer in one word.");

        await _fixture.WaitForResponseAsync();

        _fixture.OutputTranscripts.Should().HaveCount(1, "OutputTranscriptCompleted should fire exactly once");
        _fixture.AudioChunks.Should().NotBeEmpty("should receive audio data back");
        _fixture.ResponseDones.Should().HaveCount(1, "response.done should fire exactly once");
        _fixture.Errors.Should().BeEmpty("no errors should occur");
    }

    [Fact]
    public async Task SendTextInput_ReceivesTextTranscript()
    {
        await _fixture.SendTextInputAsync("Say the word hello and nothing else.");

        await _fixture.WaitForResponseAsync();

        _fixture.OutputTranscripts.Should().HaveCount(1);
        _fixture.OutputTranscripts[0].Should().ContainEquivalentOf("hello",
            "the model should have said 'hello'");
    }

    [Fact]
    public async Task SendTextInput_NoInputTranscript()
    {
        await _fixture.SendTextInputAsync("Hi");

        await _fixture.WaitForResponseAsync();

        _fixture.InputTranscripts.Should().BeEmpty(
            "text input should not produce InputTranscriptCompleted events");
    }

    [Fact]
    public async Task SendTextInput_ResponseDoneContainsResponseId()
    {
        await _fixture.SendTextInputAsync("Say ok.");

        await _fixture.WaitForResponseAsync();

        _fixture.ResponseDones.Should().HaveCount(1);
        _fixture.ResponseDones[0].ResponseId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SendTextInput_TranscriptDeltasAggregateToCompleted()
    {
        await _fixture.SendTextInputAsync("Say the word test.");

        await _fixture.WaitForResponseAsync();

        _fixture.OutputTranscriptDeltas.Should().NotBeEmpty(
            "should receive incremental transcript deltas");
        _fixture.OutputTranscripts.Should().HaveCount(1);

        var aggregated = string.Concat(_fixture.OutputTranscriptDeltas);
        aggregated.Should().Be(_fixture.OutputTranscripts[0],
            "concatenated deltas should equal the completed transcript");
    }

    [Fact]
    public async Task SendTextInput_SessionCreatedEventReceived()
    {
        // session.created is sent on connection — we should already have it
        await _fixture.SendTextInputAsync("Hi");
        await _fixture.WaitForResponseAsync();

        _fixture.GetEventsByType("session.created").Should().NotBeEmpty(
            "session.created should be received on connection");
        _fixture.GetEventsByType("session.updated").Should().NotBeEmpty(
            "session.updated should be received after UpdateSessionAsync");
    }
}
