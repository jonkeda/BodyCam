using BodyCam.Services.Audio;
using FluentAssertions;
using Xunit;

namespace BodyCam.Tests.Services.Audio;

/// <summary>
/// Phase 6.3: Tests for WAV capture recorder (A/B testing and regression analysis).
/// </summary>
public class MicCaptureRecorderTests
{
    [Fact]
    public void RecordChunk_KeepsLast10Seconds()
    {
        // Arrange
        var recorder = new MicCaptureRecorder(sampleRate: 48000, seconds: 10);
        int samplesPerChunk = 2400; // 50ms at 48kHz
        int bytesPerChunk = samplesPerChunk * 2; // PCM16

        // Act: Write 15 seconds of audio (300 chunks)
        for (int i = 0; i < 300; i++)
        {
            var chunk = new byte[bytesPerChunk];
            recorder.RecordChunk(chunk);
        }

        // Assert: Should have kept last 10s = 480,000 samples = 960,000 bytes
        // We'll verify by saving to WAV and checking file size
        var tempPath = Path.GetTempFileName();
        try
        {
            recorder.SaveToWav(tempPath);
            var info = new FileInfo(tempPath);
            // WAV header is 44 bytes + 960,000 bytes data = 960,044 total
            info.Length.Should().Be(960_044);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void SaveToWav_ProducesValidWavFile()
    {
        // Arrange
        var recorder = new MicCaptureRecorder(sampleRate: 24000, seconds: 1);
        int samplesPerChunk = 1200; // 50ms at 24kHz
        int bytesPerChunk = samplesPerChunk * 2;

        // Write 1 second (20 chunks)
        for (int i = 0; i < 20; i++)
        {
            var chunk = new byte[bytesPerChunk];
            // Fill with a simple sawtooth pattern
            for (int j = 0; j < samplesPerChunk; j++)
            {
                short sample = (short)(j % 32768);
                BitConverter.TryWriteBytes(chunk.AsSpan(j * 2), sample);
            }
            recorder.RecordChunk(chunk);
        }

        var tempPath = Path.GetTempFileName();
        try
        {
            // Act
            recorder.SaveToWav(tempPath);

            // Assert: Verify WAV header
            using var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            // RIFF header
            var riff = new string(br.ReadChars(4));
            riff.Should().Be("RIFF");
            var chunkSize = br.ReadInt32();
            chunkSize.Should().Be(36 + 24000 * 2); // 36 + data size

            var wave = new string(br.ReadChars(4));
            wave.Should().Be("WAVE");

            // fmt subchunk
            var fmt = new string(br.ReadChars(4));
            fmt.Should().Be("fmt ");
            var subchunk1Size = br.ReadInt32();
            subchunk1Size.Should().Be(16);

            var audioFormat = br.ReadInt16();
            audioFormat.Should().Be(1); // PCM

            var numChannels = br.ReadInt16();
            numChannels.Should().Be(1); // Mono

            var sampleRate = br.ReadInt32();
            sampleRate.Should().Be(24000);

            var byteRate = br.ReadInt32();
            byteRate.Should().Be(24000 * 2); // SampleRate * NumChannels * BitsPerSample/8

            var blockAlign = br.ReadInt16();
            blockAlign.Should().Be(2); // NumChannels * BitsPerSample/8

            var bitsPerSample = br.ReadInt16();
            bitsPerSample.Should().Be(16);

            // data subchunk
            var data = new string(br.ReadChars(4));
            data.Should().Be("data");
            var dataSize = br.ReadInt32();
            dataSize.Should().Be(24000 * 2);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void SaveToWav_WhenNoData_ThrowsInvalidOperationException()
    {
        // Arrange
        var recorder = new MicCaptureRecorder(sampleRate: 48000, seconds: 10);

        // Act
        var act = () => recorder.SaveToWav("dummy.wav");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("No audio data to save.");
    }

    [Fact]
    public void Clear_RemovesAllBufferedAudio()
    {
        // Arrange
        var recorder = new MicCaptureRecorder(sampleRate: 48000, seconds: 10);
        var chunk = new byte[4800]; // 50ms at 48kHz
        recorder.RecordChunk(chunk);

        // Act
        recorder.Clear();

        // Assert
        var act = () => recorder.SaveToWav("dummy.wav");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("No audio data to save.");
    }

    [Fact]
    public void RecordChunk_IsThreadSafe()
    {
        // Arrange
        var recorder = new MicCaptureRecorder(sampleRate: 48000, seconds: 10);
        var chunk = new byte[4800];
        var tasks = new List<Task>();

        // Act: 100 concurrent RecordChunk calls
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => recorder.RecordChunk(chunk)));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert: Should not crash and should have buffered something
        var tempPath = Path.GetTempFileName();
        try
        {
            recorder.SaveToWav(tempPath);
            var info = new FileInfo(tempPath);
            info.Length.Should().BeGreaterThan(44); // At least header + some data
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
