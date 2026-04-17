using BodyCam.Models;
using FluentAssertions;

namespace BodyCam.Tests.Models;

public class RealtimeModelsTests
{
    [Fact]
    public void AudioPlaybackTracker_PlayedMs_CalculatesCorrectly()
    {
        var tracker = new AudioPlaybackTracker
        {
            SampleRate = 24000,
            BitsPerSample = 16,
            Channels = 1,
            BytesPlayed = 48000 // 1 second of 24kHz 16-bit mono
        };

        tracker.PlayedMs.Should().Be(1000);
    }

    [Fact]
    public void AudioPlaybackTracker_PlayedMs_ZeroSampleRate_ReturnsZero()
    {
        var tracker = new AudioPlaybackTracker
        {
            SampleRate = 0,
            BytesPlayed = 1000
        };

        tracker.PlayedMs.Should().Be(0);
    }

    [Fact]
    public void AudioPlaybackTracker_PlayedMs_HalfSecond()
    {
        var tracker = new AudioPlaybackTracker
        {
            SampleRate = 24000,
            BitsPerSample = 16,
            Channels = 1,
            BytesPlayed = 24000 // 0.5 seconds
        };

        tracker.PlayedMs.Should().Be(500);
    }

    [Fact]
    public void AudioPlaybackTracker_Reset_ClearsState()
    {
        var tracker = new AudioPlaybackTracker
        {
            CurrentItemId = "item-123",
            BytesPlayed = 48000
        };

        tracker.Reset();

        tracker.CurrentItemId.Should().BeNull();
        tracker.BytesPlayed.Should().Be(0);
    }

    [Fact]
    public void RealtimeSessionConfig_HasCorrectDefaults()
    {
        var config = new RealtimeSessionConfig();

        config.Model.Should().Be("gpt-realtime-1.5");
        config.Voice.Should().Be("marin");
        config.TurnDetection.Should().Be("semantic_vad");
        config.NoiseReduction.Should().Be("near_field");
        config.SampleRate.Should().Be(24000);
    }

    [Fact]
    public void RealtimeResponseInfo_RequiredResponseId()
    {
        var info = new RealtimeResponseInfo { ResponseId = "resp-123" };

        info.ResponseId.Should().Be("resp-123");
        info.ItemId.Should().BeNull();
        info.OutputTranscript.Should().BeNull();
    }
}
