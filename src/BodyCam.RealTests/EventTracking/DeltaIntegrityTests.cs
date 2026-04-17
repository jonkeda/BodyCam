using BodyCam.RealTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.EventTracking;

/// <summary>
/// Tests that verify transcript delta data integrity.
/// Targets the garbled streaming text bug (RCA-004).
/// </summary>
[Trait("Category", "RealAPI")]
public class DeltaIntegrityTests : IAsyncLifetime
{
    private readonly RealtimeFixture _fixture = new();
    private readonly ITestOutputHelper _output;

    public DeltaIntegrityTests(ITestOutputHelper output)
    {
        _output = output;
        _fixture.SetOutput(output);
    }

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Deltas_ContainNoControlCharacters()
    {
        await _fixture.SendTextInputAsync("Give me a recipe for chocolate chip cookies with all ingredients.");
        await _fixture.WaitForResponseAsync();

        _fixture.OutputTranscriptDeltas.Should().NotBeEmpty();

        foreach (var delta in _fixture.OutputTranscriptDeltas)
        {
            delta.Should().NotBeNullOrEmpty();
            foreach (var ch in delta)
            {
                if (ch < 0x20 && ch != '\n' && ch != '\r' && ch != '\t')
                    throw new Xunit.Sdk.XunitException(
                        $"Delta contains control character 0x{(int)ch:X2}: \"{delta}\"");
            }
        }
    }

    [Fact]
    public async Task Deltas_ConcatenateToCompletedTranscript_LongResponse()
    {
        await _fixture.SendTextInputAsync(
            "Give me a detailed recipe for chocolate chip cookies including all ingredients, measurements, and step-by-step baking instructions.");
        await _fixture.WaitForResponseAsync();

        _fixture.OutputTranscriptDeltas.Count.Should().BeGreaterThan(10,
            "a long response should produce many deltas");
        _fixture.OutputTranscripts.Should().HaveCount(1);

        var concatenated = string.Concat(_fixture.OutputTranscriptDeltas);
        concatenated.Should().Be(_fixture.OutputTranscripts[0],
            "concatenated deltas must exactly match the completed transcript");

        _output.WriteLine($"Delta count: {_fixture.OutputTranscriptDeltas.Count}");
        _output.WriteLine($"Total chars: {concatenated.Length}");
    }

    [Fact]
    public async Task Deltas_AreUtf8Clean()
    {
        await _fixture.SendTextInputAsync(
            "Tell me about crème brûlée, café, and résumé. Use the accented characters.");
        await _fixture.WaitForResponseAsync();

        _fixture.OutputTranscriptDeltas.Should().NotBeEmpty();

        foreach (var delta in _fixture.OutputTranscriptDeltas)
        {
            // Check for unpaired surrogates (invalid UTF-16)
            for (var i = 0; i < delta.Length; i++)
            {
                if (char.IsHighSurrogate(delta[i]))
                {
                    (i + 1 < delta.Length && char.IsLowSurrogate(delta[i + 1])).Should().BeTrue(
                        $"high surrogate at position {i} must be followed by low surrogate");
                    i++; // skip the low surrogate
                }
                else if (char.IsLowSurrogate(delta[i]))
                {
                    throw new Xunit.Sdk.XunitException(
                        $"Orphaned low surrogate at position {i} in delta: \"{delta}\"");
                }
            }
        }

        var concatenated = string.Concat(_fixture.OutputTranscriptDeltas);
        concatenated.Should().Be(_fixture.OutputTranscripts[0]);
    }

    [Fact]
    public async Task Deltas_ArriveInStrictSequentialOrder()
    {
        await _fixture.SendTextInputAsync("Count from 1 to 10, one number per line.");
        await _fixture.WaitForResponseAsync();

        _fixture.OutputTranscriptDeltas.Should().NotBeEmpty();
        _fixture.OutputTranscripts.Should().HaveCount(1);

        var completed = _fixture.OutputTranscripts[0];
        var offset = 0;

        foreach (var delta in _fixture.OutputTranscriptDeltas)
        {
            var idx = completed.IndexOf(delta, offset, StringComparison.Ordinal);
            idx.Should().Be(offset,
                $"delta \"{delta}\" should appear at offset {offset} in completed transcript");
            offset += delta.Length;
        }

        offset.Should().Be(completed.Length,
            "all deltas should cover the entire completed transcript");
    }

    [Fact]
    public async Task RapidDeltas_AllCaptured()
    {
        await _fixture.SendTextInputAsync("List 20 different types of cookies, one per line.");
        await _fixture.WaitForResponseAsync();

        _fixture.OutputTranscriptDeltas.Count.Should().BeGreaterThan(15,
            "listing 20 items should produce many deltas");

        var concatenated = string.Concat(_fixture.OutputTranscriptDeltas);
        concatenated.Should().Be(_fixture.OutputTranscripts[0],
            "no deltas should be lost even under rapid arrival");

        _output.WriteLine($"Captured {_fixture.OutputTranscriptDeltas.Count} deltas, {concatenated.Length} total chars");
    }

    [Fact]
    public async Task DeltaTimings_MeasureBurstRate()
    {
        await _fixture.SendTextInputAsync("Tell me about the history of bread making in detail.");
        await _fixture.WaitForResponseAsync();

        var deltaTimestamps = _fixture.TimestampedEvents
            .Where(e => e.Type == "response.audio_transcript.delta")
            .Select(e => e.Timestamp)
            .ToList();

        deltaTimestamps.Count.Should().BeGreaterThan(0);

        if (deltaTimestamps.Count > 1)
        {
            var intervals = new List<double>();
            for (var i = 1; i < deltaTimestamps.Count; i++)
                intervals.Add((deltaTimestamps[i] - deltaTimestamps[i - 1]).TotalMilliseconds);

            _output.WriteLine($"Transcript delta count: {deltaTimestamps.Count}");
            _output.WriteLine($"Total span: {(deltaTimestamps[^1] - deltaTimestamps[0]).TotalMilliseconds:F0}ms");
            _output.WriteLine($"Min interval: {intervals.Min():F1}ms");
            _output.WriteLine($"Max interval: {intervals.Max():F1}ms");
            _output.WriteLine($"Avg interval: {intervals.Average():F1}ms");
        }
    }
}
