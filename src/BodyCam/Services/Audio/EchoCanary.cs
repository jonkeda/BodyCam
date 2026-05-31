namespace BodyCam.Services.Audio;

public sealed class EchoCanaryTranscriptMonitor
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _silentWindow;
    private readonly string _normalizedCanaryPhrase;
    private readonly string[] _canaryTokens;
    private readonly double _minimumTokenMatchRatio;
    private readonly List<string> _echoUserTranscripts = [];
    private DateTimeOffset? _assistantCanaryCompletedAt;
    private int _assistantResponsesAfterCanary;

    public EchoCanaryTranscriptMonitor(
        string canaryPhrase,
        TimeSpan? silentWindow = null,
        TimeProvider? timeProvider = null,
        double minimumTokenMatchRatio = 0.75)
    {
        if (string.IsNullOrWhiteSpace(canaryPhrase))
            throw new ArgumentException("Canary phrase must not be empty.", nameof(canaryPhrase));

        if (minimumTokenMatchRatio <= 0 || minimumTokenMatchRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(minimumTokenMatchRatio), minimumTokenMatchRatio, "Match ratio must be in (0, 1].");

        _timeProvider = timeProvider ?? TimeProvider.System;
        _silentWindow = silentWindow ?? TimeSpan.FromSeconds(15);
        _normalizedCanaryPhrase = Normalize(canaryPhrase);
        _canaryTokens = _normalizedCanaryPhrase
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _minimumTokenMatchRatio = minimumTokenMatchRatio;
    }

    public void RecordAssistantTranscriptCompleted(string transcript, DateTimeOffset? observedAt = null)
    {
        if (!ContainsCanary(transcript))
            return;

        _assistantCanaryCompletedAt = observedAt ?? _timeProvider.GetUtcNow();
        _assistantResponsesAfterCanary = 0;
        _echoUserTranscripts.Clear();
    }

    public void RecordUserTranscriptCompleted(string transcript, DateTimeOffset? observedAt = null)
    {
        if (!IsWithinSilentWindow(observedAt ?? _timeProvider.GetUtcNow()))
            return;

        if (ContainsCanary(transcript))
            _echoUserTranscripts.Add(transcript);
    }

    public void RecordAssistantResponseStarted(DateTimeOffset? observedAt = null)
    {
        if (IsWithinSilentWindow(observedAt ?? _timeProvider.GetUtcNow()))
            _assistantResponsesAfterCanary++;
    }

    public EchoCanaryTranscriptResult Snapshot()
    {
        var canaryObserved = _assistantCanaryCompletedAt.HasValue;
        var echoDetected = _echoUserTranscripts.Count > 0;
        var loopDetected = _assistantResponsesAfterCanary > 0;
        var passed = canaryObserved && !echoDetected && !loopDetected;

        var reason = !canaryObserved
            ? "Assistant canary phrase was not observed."
            : echoDetected
                ? "User transcript repeated the assistant canary phrase during the silent window."
                : loopDetected
                    ? "Assistant started another response during the silent window."
                    : "No transcript echo or response loop detected during the silent window.";

        return new EchoCanaryTranscriptResult(
            canaryObserved,
            echoDetected,
            loopDetected,
            passed,
            _assistantResponsesAfterCanary,
            _echoUserTranscripts.ToArray(),
            reason);
    }

    public bool ContainsCanary(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return false;

        var transcriptTokens = Normalize(transcript)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        if (transcriptTokens.Count == 0)
            return false;

        var matchedTokens = _canaryTokens.Count(transcriptTokens.Contains);
        return matchedTokens / (double)_canaryTokens.Length >= _minimumTokenMatchRatio;
    }

    private bool IsWithinSilentWindow(DateTimeOffset observedAt)
    {
        if (_assistantCanaryCompletedAt is not { } startedAt)
            return false;

        var elapsed = observedAt - startedAt;
        return elapsed >= TimeSpan.Zero && elapsed <= _silentWindow;
    }

    private static string Normalize(string value)
    {
        var chars = new char[value.Length];
        var index = 0;
        var previousWasSpace = true;

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                chars[index++] = char.ToLowerInvariant(c);
                previousWasSpace = false;
                continue;
            }

            if (!previousWasSpace)
            {
                chars[index++] = ' ';
                previousWasSpace = true;
            }
        }

        return new string(chars, 0, index).Trim();
    }
}

public sealed record EchoCanaryTranscriptResult(
    bool AssistantCanaryObserved,
    bool EchoDetected,
    bool LoopDetected,
    bool Passed,
    int AssistantResponsesAfterCanary,
    IReadOnlyList<string> EchoUserTranscripts,
    string Reason);

public static class EchoCanaryAudioAnalyzer
{
    public static EchoCanaryAudioMatch FindBestEchoMatch(
        byte[] referencePcm16,
        byte[] capturedPcm16,
        int sampleRate,
        TimeSpan minDelay,
        TimeSpan maxDelay,
        TimeSpan? delayStep = null)
    {
        ArgumentNullException.ThrowIfNull(referencePcm16);
        ArgumentNullException.ThrowIfNull(capturedPcm16);

        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");

        if (minDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minDelay), minDelay, "Minimum delay must be non-negative.");

        if (maxDelay < minDelay)
            throw new ArgumentOutOfRangeException(nameof(maxDelay), maxDelay, "Maximum delay must be greater than or equal to minimum delay.");

        var reference = ReadPcm16(referencePcm16);
        var captured = ReadPcm16(capturedPcm16);
        if (reference.Length == 0 || captured.Length == 0)
            return EchoCanaryAudioMatch.None;

        var minDelaySamples = ToSamples(minDelay, sampleRate);
        var maxDelaySamples = Math.Min(ToSamples(maxDelay, sampleRate), captured.Length - 1);
        var stepSamples = Math.Max(1, ToSamples(delayStep ?? TimeSpan.FromMilliseconds(1), sampleRate));

        var best = EchoCanaryAudioMatch.None;
        for (var delay = minDelaySamples; delay <= maxDelaySamples; delay += stepSamples)
        {
            var comparedSamples = Math.Min(reference.Length, captured.Length - delay);
            if (comparedSamples <= 0)
                continue;

            var score = ComputeCorrelation(reference, captured, delay, comparedSamples);
            if (Math.Abs(score) > Math.Abs(best.Score))
            {
                best = new EchoCanaryAudioMatch(
                    Math.Abs(score),
                    TimeSpan.FromSeconds(delay / (double)sampleRate),
                    delay,
                    comparedSamples);
            }
        }

        return best;
    }

    public static bool IsEchoLikely(EchoCanaryAudioMatch match, double threshold = 0.35)
    {
        if (threshold <= 0 || threshold > 1)
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "Threshold must be in (0, 1].");

        return match.Score >= threshold;
    }

    private static double ComputeCorrelation(short[] reference, short[] captured, int delay, int comparedSamples)
    {
        double dot = 0;
        double referenceEnergy = 0;
        double capturedEnergy = 0;

        for (var i = 0; i < comparedSamples; i++)
        {
            var referenceSample = reference[i] / 32768.0;
            var capturedSample = captured[delay + i] / 32768.0;
            dot += referenceSample * capturedSample;
            referenceEnergy += referenceSample * referenceSample;
            capturedEnergy += capturedSample * capturedSample;
        }

        if (referenceEnergy <= double.Epsilon || capturedEnergy <= double.Epsilon)
            return 0;

        return dot / Math.Sqrt(referenceEnergy * capturedEnergy);
    }

    private static int ToSamples(TimeSpan delay, int sampleRate)
    {
        return Math.Max(0, (int)Math.Round(delay.TotalSeconds * sampleRate));
    }

    private static short[] ReadPcm16(byte[] pcm16)
    {
        if (pcm16.Length % 2 != 0)
            throw new ArgumentException("PCM16 data must contain an even number of bytes.", nameof(pcm16));

        var samples = new short[pcm16.Length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(pcm16, i * 2);

        return samples;
    }
}

public sealed record EchoCanaryAudioMatch(
    double Score,
    TimeSpan Delay,
    int DelaySamples,
    int ComparedSamples)
{
    public static EchoCanaryAudioMatch None { get; } = new(0, TimeSpan.Zero, 0, 0);
}
