using BodyCam.Agents;
using BodyCam.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BodyCam.Tests.Agents;

public class VoiceInputAgentTests
{
    [Fact]
    public async Task StartAsync_StartsAudioInput()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        var agent = new VoiceInputAgent(audioInput, NullLogger<VoiceInputAgent>.Instance);

        await agent.StartAsync();

        await audioInput.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_StopsAudioInput()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        var agent = new VoiceInputAgent(audioInput, NullLogger<VoiceInputAgent>.Instance);

        await agent.StopAsync();

        await audioInput.Received(1).StopAsync();
    }

    [Fact]
    public async Task StartAsync_SubscribesToAudioChunks()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        byte[]? received = null;
        var agent = new VoiceInputAgent(audioInput, NullLogger<VoiceInputAgent>.Instance);
        agent.SetAudioSink(async (data, ct) => received = data);
        agent.SetConnected(true);

        await agent.StartAsync();

        // Simulate audio chunk
        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, new byte[] { 1, 2, 3 });

        // Give the async void handler a moment to execute
        await Task.Delay(50);

        received.Should().NotBeNull();
        received!.Length.Should().Be(3);
    }

    [Fact]
    public async Task StopAsync_UnsubscribesFromAudioChunks()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        byte[]? received = null;
        var agent = new VoiceInputAgent(audioInput, NullLogger<VoiceInputAgent>.Instance);
        agent.SetAudioSink(async (data, ct) => received = data);
        agent.SetConnected(true);

        await agent.StartAsync();
        await agent.StopAsync();

        // Simulate audio chunk after stop — should NOT forward
        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, new byte[] { 1 });
        await Task.Delay(50);

        received.Should().BeNull();
    }

    [Fact]
    public async Task OnAudioChunk_WhenNotConnected_DoesNotSend()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        byte[]? received = null;
        var agent = new VoiceInputAgent(audioInput, NullLogger<VoiceInputAgent>.Instance);
        agent.SetAudioSink(async (data, ct) => received = data);
        agent.SetConnected(false);

        await agent.StartAsync();

        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, new byte[] { 1, 2, 3 });
        await Task.Delay(50);

        received.Should().BeNull();
    }
}
