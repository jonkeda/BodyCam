using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BodyCam.Tests.Agents;

/// <summary>
/// Tests that playback never gates microphone chunks, preserving barge-in.
/// </summary>
public class VoiceInputAgentBargeInTests
{
    [Fact]
    public async Task OnAudioChunk_WhenLegacyPauseMicSettingDisabled_SendsChunksDuringPlayback()
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

        agent.SetAudioSink((data, ct) =>
        {
            received = data;
            return Task.CompletedTask;
        });
        agent.SetConnected(true);
        await agent.StartAsync();

        // Simulate audio chunk while playing
        var chunk = GenerateChunk48k(50);
        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, chunk);

        await Task.Delay(100);

        received.Should().NotBeNull("audio should flow even when playback is active");
    }

    [Fact]
    public async Task OnAudioChunk_WhenLegacyPauseMicSettingEnabled_StillSendsChunksDuringPlayback()
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

        agent.SetAudioSink((data, ct) =>
        {
            received = data;
            return Task.CompletedTask;
        });
        agent.SetConnected(true);
        await agent.StartAsync();

        // Simulate audio chunk while playing
        var chunk = GenerateChunk48k(50);
        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, chunk);

        await Task.Delay(100);

        received.Should().NotBeNull("barge-in requires mic audio to keep flowing during playback");
    }

    [Fact]
    public async Task OnAudioChunk_WhenLegacyPauseMicSettingEnabled_SendsChunksWhenNotPlaying()
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

        agent.SetAudioSink((data, ct) =>
        {
            received = data;
            return Task.CompletedTask;
        });
        agent.SetConnected(true);
        await agent.StartAsync();

        // Simulate audio chunk when not playing
        var chunk = GenerateChunk48k(50);
        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, chunk);

        await Task.Delay(100);

        received.Should().NotBeNull("audio should flow when not playing");
    }

    [Fact]
    public async Task OnAudioChunk_WhenPlaybackTrackerResets_MicKeepsSendingChunks()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        var tracker = new AudioPlaybackTracker { SampleRate = 48000, BytesPlayed = 48000 }; // 1s at 48kHz
        var settings = new AppSettings { PauseMicWhilePlaying = true };
        var receivedCount = 0;

        var agent = new VoiceInputAgent(
            audioInput,
            NullLogger<VoiceInputAgent>.Instance,
            tracker,
            settings);

        agent.SetAudioSink((data, ct) =>
        {
            receivedCount++;
            return Task.CompletedTask;
        });
        agent.SetConnected(true);
        await agent.StartAsync();

        // First chunk: playback is active, but mic audio still flows.
        var chunk = GenerateChunk48k(50);
        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, chunk);
        await Task.Delay(100);
        receivedCount.Should().Be(1);

        // Reset tracker (playback finished)
        tracker.Reset();

        // Second chunk: still flows after playback reset.
        audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(audioInput, chunk);
        await Task.Delay(100);
        receivedCount.Should().Be(2);
    }

    [Fact]
    public async Task OnAudioChunk_WithNullTracker_StillSendsChunks()
    {
        var audioInput = Substitute.For<IAudioInputService>();
        var settings = new AppSettings { PauseMicWhilePlaying = true };
        byte[]? received = null;

        var agent = new VoiceInputAgent(
            audioInput,
            NullLogger<VoiceInputAgent>.Instance,
            playbackTracker: null, // No tracker
            settings);

        agent.SetAudioSink((data, ct) =>
        {
            received = data;
            return Task.CompletedTask;
        });
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
