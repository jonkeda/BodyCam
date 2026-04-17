using BodyCam.Agents;
using BodyCam.Services;
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
        var chunk = new byte[] { 1, 2, 3, 4 };

        await agent.PlayAudioDeltaAsync(chunk);

        await audioOutput.Received(1).PlayChunkAsync(chunk, Arg.Any<CancellationToken>());
        agent.Tracker.BytesPlayed.Should().Be(4);
    }

    [Fact]
    public async Task PlayAudioDeltaAsync_AccumulatesByteCount()
    {
        var audioOutput = Substitute.For<IAudioOutputService>();
        var agent = new VoiceOutputAgent(audioOutput);

        await agent.PlayAudioDeltaAsync(new byte[100]);
        await agent.PlayAudioDeltaAsync(new byte[200]);

        agent.Tracker.BytesPlayed.Should().Be(300);
    }

    [Fact]
    public void HandleInterruption_ClearsBuffer()
    {
        var audioOutput = Substitute.For<IAudioOutputService>();
        var agent = new VoiceOutputAgent(audioOutput);

        agent.HandleInterruption();

        audioOutput.Received(1).ClearBuffer();
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
