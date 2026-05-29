using BodyCam.Services.Camera.A9.Vue990;
using FluentAssertions;

namespace BodyCam.Tests.Services.Camera.A9.Vue990;

public sealed class A9Vue990ChannelMediaExtractorTests
{
    [Fact]
    public void ExtractJpegFrames_Finds_Frames_With_And_Without_Channel_Envelope()
    {
        var jpeg1 = BuildTinyJpeg(0x11);
        var jpeg2 = BuildTinyJpeg(0x22);
        var bytes = new List<byte>();
        bytes.AddRange(BuildEnvelope(0x1234, jpeg1));
        bytes.AddRange([0x00, 0x01, 0x02]);
        bytes.AddRange(jpeg2);

        var frames = A9Vue990ChannelMediaExtractor.ExtractJpegFrames(bytes.ToArray());

        frames.Should().HaveCount(2);
        frames[0].Offset.Should().Be(A9Vue990ChannelMediaExtractor.ChannelChunkHeaderLength);
        frames[0].Bytes.Should().Equal(jpeg1);
        frames[1].Bytes.Should().Equal(jpeg2);
    }

    [Fact]
    public void ExtractChunkHeaders_Reads_Channel_Header_Fields()
    {
        var jpeg = BuildTinyJpeg(0x33);
        var bytes = BuildEnvelope(0x2345, jpeg);

        var headers = A9Vue990ChannelMediaExtractor.ExtractChunkHeaders(bytes);

        headers.Should().ContainSingle();
        headers[0].VersionMajor.Should().Be(0x03);
        headers[0].VersionMinor.Should().Be(0x03);
        headers[0].StreamCounter.Should().Be(0x6a19c610);
        headers[0].ChunkIndex.Should().Be(0x2345);
        headers[0].PayloadLength.Should().Be((uint)jpeg.Length);
    }

    private static byte[] BuildEnvelope(uint chunkIndex, byte[] payload)
    {
        var envelope = new byte[A9Vue990ChannelMediaExtractor.ChannelChunkHeaderLength + payload.Length];
        A9Vue990ChannelMediaExtractor.ChannelChunkMarker.CopyTo(envelope);
        envelope[4] = 0x03;
        envelope[5] = 0x03;
        WriteUInt32LittleEndian(envelope, 8, 0x6a19c610);
        WriteUInt32LittleEndian(envelope, 12, chunkIndex);
        WriteUInt32LittleEndian(envelope, 16, (uint)payload.Length);
        payload.CopyTo(envelope.AsSpan(A9Vue990ChannelMediaExtractor.ChannelChunkHeaderLength));
        return envelope;
    }

    private static byte[] BuildTinyJpeg(byte value)
    {
        return [0xff, 0xd8, 0xff, 0xe0, value, 0xff, 0xd9];
    }

    private static void WriteUInt32LittleEndian(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }
}
