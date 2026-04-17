using BodyCam.Agents;
using BodyCam.Services;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Agents;

public class VoiceInputAgentTests
{
    [Fact]
    public async Task StartAsync_StartsAudioInput()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        var realtime = Substitute.For<IRealtimeClient>();
        var agent = new VoiceInputAgent(audioInput, realtime);

        await agent.StartAsync();

        await audioInput.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_StopsAudioInput()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        var realtime = Substitute.For<IRealtimeClient>();
        var agent = new VoiceInputAgent(audioInput, realtime);

        await agent.StopAsync();

        await audioInput.Received(1).StopAsync();
    }

    [Fact]
    public async Task StartAsync_SubscribesToAudioChunks()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        var realtime = Substitute.For<IRealtimeClient>();
        realtime.IsConnected.Returns(true);
        var agent = new VoiceInputAgent(audioInput, realtime);

        await agent.StartAsync();

        // Simulate audio chunk
        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, new byte[] { 1, 2, 3 });

        // Give the async void handler a moment to execute
        await Task.Delay(50);

        await realtime.Received(1).SendAudioChunkAsync(
            Arg.Is<byte[]>(b => b.Length == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_UnsubscribesFromAudioChunks()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        var realtime = Substitute.For<IRealtimeClient>();
        realtime.IsConnected.Returns(true);
        var agent = new VoiceInputAgent(audioInput, realtime);

        await agent.StartAsync();
        await agent.StopAsync();

        // Simulate audio chunk after stop — should NOT forward
        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, new byte[] { 1 });
        await Task.Delay(50);

        await realtime.DidNotReceive().SendAudioChunkAsync(
            Arg.Any<byte[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnAudioChunk_WhenNotConnected_DoesNotSend()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        var realtime = Substitute.For<IRealtimeClient>();
        realtime.IsConnected.Returns(false);
        var agent = new VoiceInputAgent(audioInput, realtime);

        await agent.StartAsync();

        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, new byte[] { 1, 2, 3 });
        await Task.Delay(50);

        await realtime.DidNotReceive().SendAudioChunkAsync(
            Arg.Any<byte[]>(),
            Arg.Any<CancellationToken>());
    }
}
