namespace BodyCam.Services.Audio;

/// <summary>
/// Resamples 16-bit signed PCM mono between sample rates using linear interpolation.
/// Suitable for voice-quality audio (not music).
/// </summary>
public static class AudioResampler
{
    /// <summary>
    /// Resample PCM16 mono audio from <paramref name="sourceRate"/> to <paramref name="targetRate"/>.
    /// </summary>
    public static byte[] Resample(byte[] pcm16, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate)
            return pcm16;

        if (pcm16.Length == 0)
            return [];

        if (sourceRate <= 0 || targetRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRate), "Sample rates must be positive.");

        int sourceSamples = pcm16.Length / 2;
        int targetSamples = (int)((long)sourceSamples * targetRate / sourceRate);
        if (targetSamples == 0)
            return [];

        var output = new byte[targetSamples * 2];
        double ratio = (double)(sourceSamples - 1) / Math.Max(targetSamples - 1, 1);

        for (int i = 0; i < targetSamples; i++)
        {
            double srcPos = i * ratio;
            int srcIdx = (int)srcPos;
            double frac = srcPos - srcIdx;

            short s0 = BitConverter.ToInt16(pcm16, srcIdx * 2);
            short s1 = (srcIdx + 1 < sourceSamples)
                ? BitConverter.ToInt16(pcm16, (srcIdx + 1) * 2)
                : s0;

            short interpolated = (short)(s0 + (s1 - s0) * frac);
            BitConverter.TryWriteBytes(output.AsSpan(i * 2), interpolated);
        }

        return output;
    }
}
