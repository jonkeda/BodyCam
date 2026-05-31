using BodyCam.Services.Audio;
using FluentAssertions;

namespace BodyCam.Tests.Services.Audio;

public class EchoCanaryTests
{
    private const int SampleRate = 48000;
    private static readonly DateTimeOffset StartedAt = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TranscriptMonitor_Passes_WhenAssistantCanaryDoesNotReturnAsUserSpeech()
    {
        var monitor = new EchoCanaryTranscriptMonitor("echo canary violet", TimeSpan.FromSeconds(10));

        monitor.RecordAssistantTranscriptCompleted("Echo canary violet.", StartedAt);

        var result = monitor.Snapshot();

        result.AssistantCanaryObserved.Should().BeTrue();
        result.Passed.Should().BeTrue();
        result.EchoDetected.Should().BeFalse();
        result.LoopDetected.Should().BeFalse();
    }

    [Fact]
    public void TranscriptMonitor_Fails_WhenUserTranscriptRepeatsCanaryDuringSilentWindow()
    {
        var monitor = new EchoCanaryTranscriptMonitor("echo canary violet", TimeSpan.FromSeconds(10));

        monitor.RecordAssistantTranscriptCompleted("Echo canary violet.", StartedAt);
        monitor.RecordUserTranscriptCompleted("I heard echo canary violet from the speaker.", StartedAt.AddSeconds(4));

        var result = monitor.Snapshot();

        result.Passed.Should().BeFalse();
        result.EchoDetected.Should().BeTrue();
        result.EchoUserTranscripts.Should().ContainSingle();
        result.Reason.Should().Contain("User transcript repeated");
    }

    [Fact]
    public void TranscriptMonitor_Fails_WhenAssistantStartsAgainDuringSilentWindow()
    {
        var monitor = new EchoCanaryTranscriptMonitor("echo canary violet", TimeSpan.FromSeconds(10));

        monitor.RecordAssistantTranscriptCompleted("Echo canary violet.", StartedAt);
        monitor.RecordAssistantResponseStarted(StartedAt.AddSeconds(5));

        var result = monitor.Snapshot();

        result.Passed.Should().BeFalse();
        result.LoopDetected.Should().BeTrue();
        result.AssistantResponsesAfterCanary.Should().Be(1);
    }

    [Fact]
    public void TranscriptMonitor_IgnoresCanaryAfterSilentWindow()
    {
        var monitor = new EchoCanaryTranscriptMonitor("echo canary violet", TimeSpan.FromSeconds(10));

        monitor.RecordAssistantTranscriptCompleted("Echo canary violet.", StartedAt);
        monitor.RecordUserTranscriptCompleted("echo canary violet", StartedAt.AddSeconds(20));

        monitor.Snapshot().Passed.Should().BeTrue();
    }

    [Fact]
    public void AudioAnalyzer_DetectsDelayedEchoAtExpectedDelay()
    {
        var reference = GenerateReferencePcm(durationMs: 600);
        var captured = CreateDelayedEcho(reference, delayMs: 80, gain: 0.35, extraMs: 120);

        var match = EchoCanaryAudioAnalyzer.FindBestEchoMatch(
            reference,
            captured,
            SampleRate,
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(140));

        match.Score.Should().BeGreaterThan(0.98);
        match.Delay.Should().BeCloseTo(TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(1));
        EchoCanaryAudioAnalyzer.IsEchoLikely(match).Should().BeTrue();
    }

    [Fact]
    public void AudioAnalyzer_ReportsLowScoreForSilence()
    {
        var reference = GenerateReferencePcm(durationMs: 600);
        var capturedSilence = new byte[reference.Length + SampleRate / 5 * 2];

        var match = EchoCanaryAudioAnalyzer.FindBestEchoMatch(
            reference,
            capturedSilence,
            SampleRate,
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(140));

        match.Score.Should().Be(0);
        EchoCanaryAudioAnalyzer.IsEchoLikely(match).Should().BeFalse();
    }

    [Fact]
    public void SyntheticEchoCanary_PositiveControlFailsWhenEchoIsPresent_AndPassesWhenEchoIsSuppressed()
    {
        var reference = GenerateReferencePcm(durationMs: 600);
        var echoCapture = CreateDelayedEcho(reference, delayMs: 80, gain: 0.35, extraMs: 120);
        var suppressedCapture = SubtractDelayedEcho(echoCapture, reference, delayMs: 80, gain: 0.35);

        var forcedOff = EchoCanaryAudioAnalyzer.FindBestEchoMatch(
            reference,
            echoCapture,
            SampleRate,
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(140));

        var aecOn = EchoCanaryAudioAnalyzer.FindBestEchoMatch(
            reference,
            suppressedCapture,
            SampleRate,
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(140));

        EchoCanaryAudioAnalyzer.IsEchoLikely(forcedOff).Should().BeTrue("the positive control must prove echo is detectable");
        EchoCanaryAudioAnalyzer.IsEchoLikely(aecOn, threshold: 0.10).Should().BeFalse("suppressed echo should fall below the canary threshold");
    }

    private static byte[] GenerateReferencePcm(int durationMs)
    {
        var sampleCount = SampleRate * durationMs / 1000;
        var random = new Random(7391);
        var samples = new short[sampleCount];
        var filtered = 0.0;

        for (var i = 0; i < sampleCount; i++)
        {
            // Deterministic speech-shaped noise makes the correlation peak unique.
            filtered = (filtered * 0.86) + (((random.NextDouble() * 2) - 1) * 0.14);
            var envelope = Math.Sin(Math.PI * i / Math.Max(1, sampleCount - 1));
            samples[i] = ClampToPcm16(filtered * envelope * 26000);
        }

        return ToPcm16(samples);
    }

    private static byte[] CreateDelayedEcho(byte[] referencePcm16, int delayMs, double gain, int extraMs)
    {
        var reference = ToSamples(referencePcm16);
        var delaySamples = SampleRate * delayMs / 1000;
        var extraSamples = SampleRate * extraMs / 1000;
        var captured = new short[delaySamples + reference.Length + extraSamples];

        for (var i = 0; i < reference.Length; i++)
            captured[delaySamples + i] = ClampToPcm16(reference[i] * gain);

        return ToPcm16(captured);
    }

    private static byte[] SubtractDelayedEcho(byte[] capturedPcm16, byte[] referencePcm16, int delayMs, double gain)
    {
        var captured = ToSamples(capturedPcm16);
        var reference = ToSamples(referencePcm16);
        var delaySamples = SampleRate * delayMs / 1000;

        for (var i = 0; i < reference.Length && delaySamples + i < captured.Length; i++)
            captured[delaySamples + i] = ClampToPcm16(captured[delaySamples + i] - (reference[i] * gain));

        return ToPcm16(captured);
    }

    private static short[] ToSamples(byte[] pcm16)
    {
        var samples = new short[pcm16.Length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(pcm16, i * 2);

        return samples;
    }

    private static byte[] ToPcm16(short[] samples)
    {
        var pcm16 = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
            BitConverter.TryWriteBytes(pcm16.AsSpan(i * 2), samples[i]);

        return pcm16;
    }

    private static short ClampToPcm16(double value)
    {
        return (short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue);
    }
}
