using System;

namespace BodyCam.Services.Audio;

/// <summary>
/// Polyphase FIR resampler with windowed-sinc kernel for high-quality band-limited resampling.
/// Provides ~80dB stopband rejection, suitable for AEC pipeline.
/// </summary>
public sealed class PolyphaseFirResampler
{
    private readonly int _l;          // Upsample factor
    private readonly int _m;          // Downsample factor
    private readonly int _taps;       // Taps per phase
    private readonly float[][] _polyphase; // Polyphase filter bank [L phases][taps]
    private readonly float[] _history;// Delay line for filtering

    /// <summary>
    /// Create a resampler for srcRate → dstRate conversion.
    /// </summary>
    /// <param name="srcRate">Source sample rate (Hz)</param>
    /// <param name="dstRate">Destination sample rate (Hz)</param>
    /// <param name="taps">Number of taps per polyphase (default 32)</param>
    public PolyphaseFirResampler(int srcRate, int dstRate, int taps = 32)
    {
        if (srcRate <= 0 || dstRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(srcRate), "Sample rates must be positive.");
        if (taps < 8 || taps > 256)
            throw new ArgumentOutOfRangeException(nameof(taps), "Taps must be in [8, 256].");

        // Compute rational conversion factor L/M
        int gcd = Gcd(srcRate, dstRate);
        _l = dstRate / gcd;
        _m = srcRate / gcd;
        _taps = taps;

        // Allocate polyphase filter bank and history
        _polyphase = new float[_l][];
        for (int i = 0; i < _l; i++)
            _polyphase[i] = new float[_taps];
        _history = new float[_taps];

        // Generate windowed-sinc kernel and decompose into polyphases
        int kernelLen = _l * _taps;
        float[] fullKernel = new float[kernelLen];
        double cutoff = Math.Min(_l, _m) / (2.0 * _l);
        double beta = 8.0; // Kaiser window β for ~80dB stopband

        for (int i = 0; i < kernelLen; i++)
        {
            double t = (i - kernelLen / 2.0) / _l;
            double sinc = Math.Abs(t) < 1e-9 ? 1.0 : Math.Sin(2.0 * Math.PI * cutoff * t) / (Math.PI * t);
            double window = Kaiser(i, kernelLen, beta);
            fullKernel[i] = (float)(sinc * window * _l); // Gain correction
        }

        // Decompose into polyphase components
        for (int phase = 0; phase < _l; phase++)
        {
            for (int tap = 0; tap < _taps; tap++)
            {
                _polyphase[phase][tap] = fullKernel[tap * _l + phase];
            }
        }
    }

    /// <summary>
    /// Resample input to output. Output length = input.Length * L / M.
    /// </summary>
    public int Resample(ReadOnlySpan<float> input, Span<float> output)
    {
        int expectedOut = (input.Length * _l + _m - 1) / _m;
        if (output.Length < expectedOut)
            throw new ArgumentException("Output buffer too small.", nameof(output));

        // Build working buffer: [history][input]
        int totalLen = _history.Length + input.Length;
        float[] working = new float[totalLen];
        _history.CopyTo(working.AsSpan(0, _history.Length));
        input.CopyTo(working.AsSpan(_history.Length));

        int outIdx = 0;
        long timeAccum = 0; // Fractional time accumulator (in units of L)

        while (outIdx < expectedOut)
        {
            int inIdx = (int)(timeAccum / _l);
            int phase = (int)(timeAccum % _l);

            if (inIdx >= input.Length)
                break;

            // Convolve with polyphase filter for this phase
            float acc = 0f;
            int baseIdx = _history.Length + inIdx;
            for (int tap = 0; tap < _taps; tap++)
            {
                int srcIdx = baseIdx - _taps + 1 + tap;
                if (srcIdx >= 0 && srcIdx < totalLen)
                    acc += working[srcIdx] * _polyphase[phase][tap];
            }
            output[outIdx++] = acc;

            // Advance time by M/L
            timeAccum += _m;
        }

        // Save last _taps samples as history
        if (input.Length >= _taps)
            input[^_taps..].CopyTo(_history);
        else
        {
            // Shift history left and append new samples
            Array.Copy(_history, input.Length, _history, 0, _taps - input.Length);
            input.CopyTo(_history.AsSpan(_taps - input.Length));
        }

        return outIdx;
    }

    /// <summary>
    /// Reset internal state (history buffer).
    /// </summary>
    public void Reset()
    {
        Array.Clear(_history);
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            int t = b;
            b = a % b;
            a = t;
        }
        return a;
    }

    private static double Kaiser(int n, int N, double beta)
    {
        double x = 2.0 * n / (N - 1) - 1.0;
        return I0(beta * Math.Sqrt(1.0 - x * x)) / I0(beta);
    }

    private static double I0(double x)
    {
        // Modified Bessel function of the first kind, order 0
        double sum = 1.0;
        double term = 1.0;
        for (int k = 1; k < 50; k++)
        {
            term *= (x / (2.0 * k)) * (x / (2.0 * k));
            sum += term;
            if (term < 1e-12 * sum)
                break;
        }
        return sum;
    }
}
