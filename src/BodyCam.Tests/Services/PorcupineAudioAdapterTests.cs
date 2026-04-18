using BodyCam.Services.WakeWord;
using FluentAssertions;

namespace BodyCam.Tests.Services;

public class PorcupineAudioAdapterTests
{
    private const int FrameLength = 512;

    [Fact]
    public void EmptyInput_YieldsNoFrames()
    {
        var adapter = new PorcupineAudioAdapter(FrameLength);

        var frames = adapter.Process([]).ToList();

        frames.Should().BeEmpty();
    }

    [Fact]
    public void SmallInput_BuffersWithoutYielding()
    {
        var adapter = new PorcupineAudioAdapter(FrameLength);

        // 10 samples at 24kHz = 20 bytes → after 3:2 decimation ≈ 6-7 samples at 16kHz
        var pcm = CreatePcm24kHz(10);
        var frames = adapter.Process(pcm).ToList();

        frames.Should().BeEmpty();
    }

    [Fact]
    public void ExactFrameWorthOfInput_YieldsOneFrame()
    {
        var adapter = new PorcupineAudioAdapter(FrameLength);

        // To get FrameLength (512) output samples after 3:2 decimation,
        // we need 512 * 3/2 = 768 input samples at 24kHz
        var pcm = CreatePcm24kHz(768);
        var frames = adapter.Process(pcm).ToList();

        frames.Should().HaveCount(1);
        frames[0].Should().HaveCount(FrameLength);
    }

    [Fact]
    public void OutputFrames_AreExactlyFrameLength()
    {
        var adapter = new PorcupineAudioAdapter(FrameLength);

        // Feed enough for multiple frames: 2400 samples → ~1600 after decimation → 3 frames + remainder
        var pcm = CreatePcm24kHz(2400);
        var frames = adapter.Process(pcm).ToList();

        foreach (var frame in frames)
        {
            frame.Should().HaveCount(FrameLength);
        }
    }

    [Fact]
    public void MultipleSmallChunks_AccumulateToFrame()
    {
        var adapter = new PorcupineAudioAdapter(FrameLength);
        var allFrames = new List<short[]>();

        // Feed 100 samples at a time (200 bytes), need ~768 input for one frame
        for (int i = 0; i < 8; i++)
        {
            var pcm = CreatePcm24kHz(100);
            allFrames.AddRange(adapter.Process(pcm));
        }

        // 800 input samples → ~533 output samples → 1 frame of 512 + 21 buffered
        allFrames.Should().HaveCount(1);
    }

    [Fact]
    public void Decimation_ReducesSampleCount()
    {
        var adapter = new PorcupineAudioAdapter(FrameLength);

        // 3000 input samples → 2000 output samples → 3 frames (1536) + 464 buffered
        var pcm = CreatePcm24kHz(3000);
        var frames = adapter.Process(pcm).ToList();

        frames.Should().HaveCount(3);
    }

    [Fact]
    public void Reset_ClearsBuffers()
    {
        var adapter = new PorcupineAudioAdapter(FrameLength);

        // Partially fill buffer
        var pcm = CreatePcm24kHz(100);
        adapter.Process(pcm).ToList();

        adapter.Reset();

        // After reset, same amount of input should produce same result as fresh adapter
        var freshAdapter = new PorcupineAudioAdapter(FrameLength);
        var pcm2 = CreatePcm24kHz(768);
        var framesAfterReset = adapter.Process(pcm2).ToList();
        var framesFresh = freshAdapter.Process(pcm2).ToList();

        framesAfterReset.Should().HaveCount(framesFresh.Count);
    }

    [Fact]
    public void PreservesAudioData()
    {
        var adapter = new PorcupineAudioAdapter(FrameLength);

        // Create ascending samples so we can verify decimation picks correct ones
        var samples = new short[768];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (short)(i % short.MaxValue);

        var pcm = ShortsToBytes(samples);
        var frames = adapter.Process(pcm).ToList();

        frames.Should().HaveCount(1);

        // First output sample should be samples[0] (position 0 in 3-cycle = kept)
        frames[0][0].Should().Be(samples[0]);
        // Second output sample should be samples[1] (position 1 in 3-cycle = kept)
        frames[0][1].Should().Be(samples[1]);
        // Third output sample should be samples[3] (position 0 in next 3-cycle = kept, samples[2] skipped)
        frames[0][2].Should().Be(samples[3]);
    }

    private static byte[] CreatePcm24kHz(int sampleCount)
    {
        // Create silent PCM (all zeros) — correct byte count
        return new byte[sampleCount * 2];
    }

    private static byte[] ShortsToBytes(short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            bytes[i * 2] = (byte)(samples[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)((samples[i] >> 8) & 0xFF);
        }
        return bytes;
    }
}
