using BodyCam.Agents;
using BodyCam.Services;
using BodyCam.Services.Audio.WebRtcApm;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Agents;

public class VoiceOutputAgentTests
{
    [Fact]
    public async Task StartAsync_StartsAudioOutput()
    {
        var audioOutput = Substitute.For<IAudioOutputService>();
        var agent = new VoiceOutputAgent(audioOutput);

        await agent.StartAsync();

        await audioOutput.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_StopsAudioOutput()
    {
        var audioOutput = Substitute.For<IAudioOutputService>();
        var agent = new VoiceOutputAgent(audioOutput);

        await agent.StopAsync();

        await audioOutput.Received(1).StopAsync();
    }

    [Fact]
    public async Task StopAsync_ResetsTracker()
    {
        var audioOutput = Substitute.For<IAudioOutputService>();
        var agent = new VoiceOutputAgent(audioOutput);
        agent.Tracker.BytesPlayed = 1000;
        agent.Tracker.CurrentItemId = "item-1";

        await agent.StopAsync();

        agent.Tracker.BytesPlayed.Should().Be(0);
        agent.Tracker.CurrentItemId.Should().BeNull();
    }

    [Fact]
    public async Task PlayAudioDeltaAsync_PlaysAndTracksByteCount()
    {
        var audioOutput = Substitute.For<IAudioOutputService>();
        var agent = new VoiceOutputAgent(audioOutput);
        var chunk = new byte[] { 1, 2, 3, 4 }; // 4 bytes at 24kHz

        await agent.PlayAudioDeltaAsync(chunk);

        // Audio is resampled 24kHz → 48kHz (2x), so expect roughly 2x the bytes
        await audioOutput.Received(1).PlayChunkAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
        agent.Tracker.BytesPlayed.Should().BeGreaterThan(4); // Resampled chunk is larger
    }

    [Fact]
    public async Task PlayAudioDeltaAsync_DoesNotFeedAecRenderReferenceDirectly()
    {
        var audioOutput = Substitute.For<IAudioOutputService>();
        var aec = Substitute.For<IAecProcessor>();
        var agent = new VoiceOutputAgent(audioOutput, aec);

        await agent.PlayAudioDeltaAsync(new byte[100]);

        aec.DidNotReceive().FeedRenderReference(Arg.Any<byte[]>());
        await audioOutput.Received(1).PlayChunkAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlayAudioDeltaAsync_TracksPlaybackAtOutputSampleRate()
    {
        var audioOutput = Substitute.For<IAudioOutputService>();
        var agent = new VoiceOutputAgent(audioOutput);

        await agent.PlayAudioDeltaAsync(new byte[2400]);

        agent.Tracker.SampleRate.Should().Be(48000);
        agent.Tracker.PlayedMs.Should().BeInRange(45, 55);
    }

    [Fact]
    public async Task PlayAudioDeltaAsync_AccumulatesByteCount()
    {
        var audioOutput = Substitute.For<IAudioOutputService>();
        var agent = new VoiceOutputAgent(audioOutput);

        await agent.PlayAudioDeltaAsync(new byte[100]); // 100 bytes at 24kHz → ~200 bytes at 48kHz
        await agent.PlayAudioDeltaAsync(new byte[200]); // 200 bytes at 24kHz → ~400 bytes at 48kHz

        // Total: ~600 bytes (resampled)
        agent.Tracker.BytesPlayed.Should().BeGreaterThan(300);
        agent.Tracker.BytesPlayed.Should().BeInRange(500, 700); // Account for resampling
    }

    [Fact]
    public async Task HandleInterruptionAsync_ClearsBufferWithFade()
    {
        var audioOutput = Substitute.For<IAudioOutputService>();
        var agent = new VoiceOutputAgent(audioOutput);

        await agent.HandleInterruptionAsync();

        await audioOutput.Received(1).FadeOutAndClearAsync(30, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ResetTracker_ClearsState()
    {
        var audioOutput = Substitute.For<IAudioOutputService>();
        var agent = new VoiceOutputAgent(audioOutput);
        agent.Tracker.BytesPlayed = 5000;
        agent.Tracker.CurrentItemId = "item-abc";

        agent.ResetTracker();

        agent.Tracker.BytesPlayed.Should().Be(0);
        agent.Tracker.CurrentItemId.Should().BeNull();
    }

    [Fact]
    public void SetCurrentItem_SetsTrackerId()
    {
        var audioOutput = Substitute.For<IAudioOutputService>();
        var agent = new VoiceOutputAgent(audioOutput);

        agent.SetCurrentItem("item-xyz");

        agent.Tracker.CurrentItemId.Should().Be("item-xyz");
    }
}
