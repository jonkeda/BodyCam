using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BodyCam.Tests.Agents;

/// <summary>
/// Phase 5.3: Tests for optional mic ducking during playback.
/// </summary>
public class VoiceInputAgentDuckingTests
{
    [Fact]
    public async Task OnAudioChunk_WhenDuckingDisabled_AlwaysSendsChunks()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        var tracker = new AudioPlaybackTracker { SampleRate = 48000, BytesPlayed = 48000 }; // 1s at 48kHz
        var settings = new AppSettings { PauseMicWhilePlaying = false };
        byte[]? received = null;

        var agent = new VoiceInputAgent(
            audioInput,
            NullLogger<VoiceInputAgent>.Instance,
            tracker,
            settings);

        agent.SetAudioSink(async (data, ct) => received = data);
        agent.SetConnected(true);
        await agent.StartAsync();

        // Simulate audio chunk while playing
        var chunk = GenerateChunk48k(50);
        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, chunk);

        await Task.Delay(100);

        received.Should().NotBeNull("audio should flow even when playback is active");
    }

    [Fact]
    public async Task OnAudioChunk_WhenDuckingEnabled_BlocksChunksDuringPlayback()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        var tracker = new AudioPlaybackTracker { SampleRate = 48000, BytesPlayed = 48000 }; // 1s at 48kHz
        var settings = new AppSettings { PauseMicWhilePlaying = true };
        byte[]? received = null;

        var agent = new VoiceInputAgent(
            audioInput,
            NullLogger<VoiceInputAgent>.Instance,
            tracker,
            settings);

        agent.SetAudioSink(async (data, ct) => received = data);
        agent.SetConnected(true);
        await agent.StartAsync();

        // Simulate audio chunk while playing
        var chunk = GenerateChunk48k(50);
        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, chunk);

        await Task.Delay(100);

        received.Should().BeNull("audio should be blocked when playback is active");
    }

    [Fact]
    public async Task OnAudioChunk_WhenDuckingEnabled_AllowsChunksWhenNotPlaying()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        var tracker = new AudioPlaybackTracker { SampleRate = 48000, BytesPlayed = 0 }; // No playback
        var settings = new AppSettings { PauseMicWhilePlaying = true };
        byte[]? received = null;

        var agent = new VoiceInputAgent(
            audioInput,
            NullLogger<VoiceInputAgent>.Instance,
            tracker,
            settings);

        agent.SetAudioSink(async (data, ct) => received = data);
        agent.SetConnected(true);
        await agent.StartAsync();

        // Simulate audio chunk when not playing
        var chunk = GenerateChunk48k(50);
        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, chunk);

        await Task.Delay(100);

        received.Should().NotBeNull("audio should flow when not playing");
    }

    [Fact]
    public async Task OnAudioChunk_WhenDuckingEnabled_AllowsChunksAfterReset()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        var tracker = new AudioPlaybackTracker { SampleRate = 48000, BytesPlayed = 48000 }; // 1s at 48kHz
        var settings = new AppSettings { PauseMicWhilePlaying = true };
        byte[]? received = null;

        var agent = new VoiceInputAgent(
            audioInput,
            NullLogger<VoiceInputAgent>.Instance,
            tracker,
            settings);

        agent.SetAudioSink(async (data, ct) => received = data);
        agent.SetConnected(true);
        await agent.StartAsync();

        // First chunk: blocked
        var chunk = GenerateChunk48k(50);
        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, chunk);
        await Task.Delay(100);
        received.Should().BeNull();

        // Reset tracker (playback finished)
        tracker.Reset();

        // Second chunk: allowed
        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, chunk);
        await Task.Delay(100);
        received.Should().NotBeNull("audio should flow after tracker reset");
    }

    [Fact]
    public async Task OnAudioChunk_WithNullTracker_NeverDucks()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        var settings = new AppSettings { PauseMicWhilePlaying = true };
        byte[]? received = null;

        var agent = new VoiceInputAgent(
            audioInput,
            NullLogger<VoiceInputAgent>.Instance,
            playbackTracker: null, // No tracker
            settings);

        agent.SetAudioSink(async (data, ct) => received = data);
        agent.SetConnected(true);
        await agent.StartAsync();

        var chunk = GenerateChunk48k(50);
        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, chunk);

        await Task.Delay(100);

        received.Should().NotBeNull("audio should flow when tracker is null");
    }

    private static byte[] GenerateChunk48k(int durationMs)
    {
        int sampleRate = 48000;
        int samples = sampleRate * durationMs / 1000;
        return new byte[samples * 2];
    }
}
