using BodyCam.RealTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Pipeline;

/// <summary>
/// Tests that verify audio delta integrity and measure buffering characteristics.
/// Targets the garbled voice playback bug (RCA-005).
/// </summary>
[Trait("Category", "RealAPI")]
public class AudioIntegrityTests : IAsyncLifetime
{
    private readonly RealtimeFixture _fixture = new();
    private readonly ITestOutputHelper _output;

    public AudioIntegrityTests(ITestOutputHelper output)
    {
        _output = output;
        _fixture.SetOutput(output);
    }

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task AudioDeltas_TotalBytesMatchReasonableDuration()
    {
        await _fixture.SendTextInputAsync("Count from one to ten slowly.");
        await _fixture.WaitForResponseAsync();

        var totalBytes = _fixture.AudioChunks.Sum(c => c.Length);
        var durationSec = totalBytes / (24000.0 * 2 * 1); // 24kHz, 16-bit, mono

        _output.WriteLine($"Total audio bytes: {totalBytes}");
        _output.WriteLine($"Audio duration: {durationSec:F2}s");
        _output.WriteLine($"Chunk count: {_fixture.AudioChunks.Count}");

        totalBytes.Should().BeGreaterThan(0, "should receive audio data");
        durationSec.Should().BeGreaterThan(1.0, "counting to ten should produce >1s of audio");
    }

    [Fact]
    public async Task AudioDeltas_ChunkSizeDistribution()
    {
        await _fixture.SendTextInputAsync("Give me a detailed recipe for chocolate chip cookies.");
        await _fixture.WaitForResponseAsync();

        _fixture.AudioChunks.Should().NotBeEmpty();

        var sizes = _fixture.AudioChunks.Select(c => c.Length).ToList();
        var totalBytes = sizes.Sum();
        var durationSec = totalBytes / (24000.0 * 2);

        _output.WriteLine($"Chunks: {sizes.Count}");
        _output.WriteLine($"Min size: {sizes.Min()} bytes");
        _output.WriteLine($"Max size: {sizes.Max()} bytes");
        _output.WriteLine($"Avg size: {sizes.Average():F0} bytes");
        _output.WriteLine($"Total: {totalBytes} bytes ({durationSec:F2}s)");
    }

    [Fact]
    public async Task AudioDeltas_NoDuplicateConsecutiveChunks()
    {
        await _fixture.SendTextInputAsync("Explain photosynthesis.");
        await _fixture.WaitForResponseAsync();

        _fixture.AudioChunks.Count.Should().BeGreaterThan(1);

        for (var i = 1; i < _fixture.AudioChunks.Count; i++)
        {
            var prev = _fixture.AudioChunks[i - 1];
            var curr = _fixture.AudioChunks[i];

            if (prev.Length == curr.Length)
            {
                prev.AsSpan().SequenceEqual(curr.AsSpan()).Should().BeFalse(
                    $"audio chunks {i - 1} and {i} should not be identical (would cause echo)");
            }
        }
    }

    [Fact]
    public async Task AudioDeltas_AudioDoneMarksEnd()
    {
        await _fixture.SendTextInputAsync("Say hello.");
        await _fixture.WaitForResponseAsync();

        var audioDoneIdx = _fixture.IndexOfEvent("response.audio.done");
        var responseDoneIdx = _fixture.IndexOfEvent("response.done");

        audioDoneIdx.Should().BeGreaterThanOrEqualTo(0, "response.audio.done should exist");
        responseDoneIdx.Should().BeGreaterThanOrEqualTo(0, "response.done should exist");

        audioDoneIdx.Should().BeLessThan(responseDoneIdx,
            "audio.done should arrive before response.done");

        // No audio deltas should appear after audio.done
        for (var i = audioDoneIdx + 1; i < _fixture.Events.Count; i++)
        {
            _fixture.Events[i].Type.Should().NotBe("response.audio.delta",
                $"no audio deltas should appear after response.audio.done (event {i})");
        }
    }

    [Fact]
    public async Task AudioDeltas_ArrivalBurstRate()
    {
        await _fixture.SendTextInputAsync("Tell me a long story about a brave knight who saved a kingdom.");
        await _fixture.WaitForResponseAsync();

        var audioTimestamps = _fixture.TimestampedEvents
            .Where(e => e.Type == "response.audio.delta")
            .Select(e => e.Timestamp)
            .ToList();

        audioTimestamps.Count.Should().BeGreaterThan(0);

        if (audioTimestamps.Count > 1)
        {
            var intervals = new List<double>();
            for (var i = 1; i < audioTimestamps.Count; i++)
                intervals.Add((audioTimestamps[i] - audioTimestamps[i - 1]).TotalMilliseconds);

            var totalBytes = _fixture.AudioChunks.Sum(c => c.Length);
            var spanMs = (audioTimestamps[^1] - audioTimestamps[0]).TotalMilliseconds;
            var arrivalBytesPerSec = spanMs > 0 ? totalBytes / (spanMs / 1000.0) : 0;
            var playbackBytesPerSec = 24000.0 * 2; // 48000 bytes/sec

            _output.WriteLine($"Audio chunks: {audioTimestamps.Count}");
            _output.WriteLine($"Arrival span: {spanMs:F0}ms");
            _output.WriteLine($"Arrival rate: {arrivalBytesPerSec:F0} bytes/sec");
            _output.WriteLine($"Playback rate: {playbackBytesPerSec:F0} bytes/sec");
            _output.WriteLine($"Ratio (arrival/playback): {arrivalBytesPerSec / playbackBytesPerSec:F2}x");
            _output.WriteLine($"Min interval: {intervals.Min():F1}ms");
            _output.WriteLine($"Avg interval: {intervals.Average():F1}ms");
        }
    }

    [Fact]
    public async Task LongResponse_BufferRequirementSimulation()
    {
        await _fixture.SendTextInputAsync(
            "Tell me a very long bedtime story about a cat who went on an adventure across the ocean. Make it at least ten sentences.");
        await _fixture.WaitForResponseAsync();

        var audioEvents = _fixture.TimestampedEvents
            .Select((e, idx) => (e.Type, e.Timestamp, Index: idx))
            .Where(e => e.Type == "response.audio.delta")
            .ToList();

        audioEvents.Count.Should().BeGreaterThan(0);

        // Simulate buffer fill/drain
        const double playbackRate = 24000.0 * 2; // bytes per second
        double bufferLevel = 0;
        double peakLevel = 0;
        var startTime = audioEvents[0].Timestamp;

        foreach (var (_, timestamp, idx) in audioEvents)
        {
            var elapsedSec = (timestamp - startTime).TotalSeconds;

            // Drain: playback consumes data at the playback rate
            var drained = elapsedSec * playbackRate;
            var chunkSize = _fixture.AudioChunks[
                _fixture.AudioChunks.Count > idx ? idx : _fixture.AudioChunks.Count - 1].Length;

            // Simple model: total added - total drained = current level
            bufferLevel += chunkSize;
            var effectiveLevel = Math.Max(0, bufferLevel - drained);

            if (effectiveLevel > peakLevel)
                peakLevel = effectiveLevel;
        }

        var peakSeconds = peakLevel / playbackRate;

        _output.WriteLine($"Audio chunks: {audioEvents.Count}");
        _output.WriteLine($"Peak buffer level: {peakLevel:F0} bytes ({peakSeconds:F2}s)");
        _output.WriteLine($"5s buffer sufficient: {peakSeconds <= 5}");
        _output.WriteLine($"10s buffer sufficient: {peakSeconds <= 10}");
        _output.WriteLine($"30s buffer sufficient: {peakSeconds <= 30}");
    }
}
