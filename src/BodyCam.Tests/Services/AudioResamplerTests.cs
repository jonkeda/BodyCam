using BodyCam.Services.Audio;
using FluentAssertions;

namespace BodyCam.Tests.Services;

public class AudioResamplerTests
{
    [Fact]
    public void SameRate_ReturnsSameData()
    {
        var input = new byte[] { 0x00, 0x10, 0x00, 0x20 }; // 2 samples
        var result = AudioResampler.Resample(input, 16000, 16000);
        result.Should().BeEquivalentTo(input);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var result = AudioResampler.Resample([], 16000, 24000);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Upsample_16kTo24k_CorrectLength()
    {
        // 100 samples at 16kHz → should produce 150 samples at 24kHz
        var input = new byte[100 * 2]; // 100 samples, 16-bit
        var result = AudioResampler.Resample(input, 16000, 24000);
        int expectedSamples = 100 * 24000 / 16000; // 150
        result.Length.Should().Be(expectedSamples * 2);
    }

    [Fact]
    public void Upsample_8kTo24k_CorrectLength()
    {
        // 80 samples at 8kHz → should produce 240 samples at 24kHz
        var input = new byte[80 * 2];
        var result = AudioResampler.Resample(input, 8000, 24000);
        int expectedSamples = 80 * 24000 / 8000; // 240
        result.Length.Should().Be(expectedSamples * 2);
    }

    [Fact]
    public void Downsample_32kTo24k_CorrectLength()
    {
        var input = new byte[320 * 2]; // 320 samples at 32kHz
        var result = AudioResampler.Resample(input, 32000, 24000);
        int expectedSamples = 320 * 24000 / 32000; // 240
        result.Length.Should().Be(expectedSamples * 2);
    }

    [Fact]
    public void PreservesSilence()
    {
        var input = new byte[200]; // 100 samples of silence (all zeros)
        var result = AudioResampler.Resample(input, 16000, 24000);

        // All output samples should be zero
        for (int i = 0; i < result.Length; i += 2)
        {
            var sample = BitConverter.ToInt16(result, i);
            sample.Should().Be(0);
        }
    }

    [Fact]
    public void PreservesSineWave()
    {
        // Generate 1kHz sine wave at 16kHz for 10ms = 160 samples
        int sourceSamples = 160;
        var input = new byte[sourceSamples * 2];
        for (int i = 0; i < sourceSamples; i++)
        {
            double t = (double)i / 16000;
            short sample = (short)(Math.Sin(2 * Math.PI * 1000 * t) * 16000);
            BitConverter.TryWriteBytes(input.AsSpan(i * 2), sample);
        }

        var result = AudioResampler.Resample(input, 16000, 24000);
        int outputSamples = result.Length / 2;

        // Verify the output contains a sine-like pattern (alternating positive/negative)
        int zeroCrossings = 0;
        short prev = BitConverter.ToInt16(result, 0);
        for (int i = 1; i < outputSamples; i++)
        {
            short current = BitConverter.ToInt16(result, i * 2);
            if ((prev > 0 && current <= 0) || (prev < 0 && current >= 0))
                zeroCrossings++;
            prev = current;
        }

        // 1kHz in 10ms = 10 cycles = ~20 zero crossings
        zeroCrossings.Should().BeInRange(18, 22);
    }

    [Fact]
    public void InvalidRate_Throws()
    {
        var input = new byte[100];
        var act = () => AudioResampler.Resample(input, 0, 24000);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
