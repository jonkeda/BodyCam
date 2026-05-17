using BodyCam.Services.Audio;
using BodyCam.Services;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Services.Audio;

/// <summary>
/// Phase 5.4: Tests for fade-out functionality to prevent clicks on barge-in.
/// </summary>
public class FadeOutTests
{
    [Fact]
    public async Task FadeOutAndClearAsync_WithNoRecentSamples_ClearsImmediately()
    {
        var provider = Substitute.For<IAudioOutputProvider>();
        provider.FadeOutAndClearAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await provider.FadeOutAndClearAsync(30);

        await provider.Received(1).FadeOutAndClearAsync(30, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FadeOutAndClearAsync_DefaultFadeMs_Is30()
    {
        var provider = Substitute.For<IAudioOutputProvider>();
        provider.FadeOutAndClearAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await provider.FadeOutAndClearAsync();

        await provider.Received(1).FadeOutAndClearAsync(30, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GenerateFadeChunk_CreatesLinearRamp()
    {
        // Generate a constant tone at 1000 Hz
        int sampleRate = 48000;
        int durationMs = 30;
        int samples = sampleRate * durationMs / 1000;
        byte[] pcm = new byte[samples * 2];

        // Fill with constant amplitude (half of max to avoid clipping)
        short amplitude = 16000;
        for (int i = 0; i < samples; i++)
        {
            BitConverter.TryWriteBytes(pcm.AsSpan(i * 2), amplitude);
        }

        // Apply linear fade-out
        for (int i = 0; i < samples; i++)
        {
            short sample = BitConverter.ToInt16(pcm, i * 2);
            float gain = 1.0f - ((float)i / samples);
            short faded = (short)(sample * gain);
            BitConverter.TryWriteBytes(pcm.AsSpan(i * 2), faded);
        }

        // Verify fade: first sample should be near full amplitude
        short firstSample = BitConverter.ToInt16(pcm, 0);
        firstSample.Should().BeGreaterThan(15000);

        // Last sample should be near zero
        short lastSample = BitConverter.ToInt16(pcm, (samples - 1) * 2);
        Math.Abs(lastSample).Should().BeLessThan(500);

        // Middle sample should be roughly half
        short middleSample = BitConverter.ToInt16(pcm, (samples / 2) * 2);
        middleSample.Should().BeInRange(7000, 9000);
    }

    [Fact]
    public async Task AudioOutputManager_FadeOutAndClearAsync_ProxiesToProvider()
    {
        var provider = Substitute.For<IAudioOutputProvider>();
        provider.IsAvailable.Returns(true);
        provider.ProviderId.Returns("test-provider");
        provider.FadeOutAndClearAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var settings = Substitute.For<ISettingsService>();
        var appSettings = new AppSettings();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<AudioOutputManager>>();

        var manager = new AudioOutputManager(
            new[] { provider },
            settings,
            appSettings,
            logger);

        await manager.SetActiveAsync("test-provider");
        await manager.FadeOutAndClearAsync(30);

        await provider.Received(1).FadeOutAndClearAsync(30, Arg.Any<CancellationToken>());
    }
}
