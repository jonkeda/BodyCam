using System.Buffers.Binary;

namespace BodyCam.Services.Camera.A9.Vue990;

public static class A9Vue990ChannelMediaExtractor
{
    public static ReadOnlySpan<byte> ChannelChunkMarker => [0x55, 0xaa, 0x15, 0xa8];

    public const int ChannelChunkHeaderLength = 0x20;

    public static IReadOnlyList<A9Vue990ExtractedJpegFrame> ExtractJpegFrames(
        ReadOnlySpan<byte> bytes,
        int maxFrames = int.MaxValue)
    {
        var frames = new List<A9Vue990ExtractedJpegFrame>();
        var cursor = 0;
        while (cursor < bytes.Length && frames.Count < maxFrames)
        {
            var start = IndexOfJpegStart(bytes[cursor..]);
            if (start < 0)
                break;

            start += cursor;
            var end = IndexOfJpegEnd(bytes[(start + 2)..]);
            if (end < 0)
                break;

            end += start + 2;
            var frameEnd = end + 2;
            frames.Add(new A9Vue990ExtractedJpegFrame(start, bytes[start..frameEnd].ToArray()));
            cursor = frameEnd;
        }

        return frames;
    }

    public static IReadOnlyList<A9Vue990ChannelChunkHeader> ExtractChunkHeaders(ReadOnlySpan<byte> bytes)
    {
        var headers = new List<A9Vue990ChannelChunkHeader>();
        var cursor = 0;
        while (cursor < bytes.Length)
        {
            var relative = bytes[cursor..].IndexOf(ChannelChunkMarker);
            if (relative < 0)
                break;

            var offset = cursor + relative;
            if (TryReadChunkHeader(bytes[offset..], offset, out var header))
                headers.Add(header);

            cursor = offset + ChannelChunkMarker.Length;
        }

        return headers;
    }

    public static bool TryReadChunkHeader(
        ReadOnlySpan<byte> bytes,
        int offset,
        out A9Vue990ChannelChunkHeader header)
    {
        header = default;
        if (bytes.Length < ChannelChunkHeaderLength || !bytes.StartsWith(ChannelChunkMarker))
            return false;

        header = new A9Vue990ChannelChunkHeader(
            Offset: offset,
            VersionMajor: bytes[4],
            VersionMinor: bytes[5],
            StreamCounter: BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..12]),
            ChunkIndex: BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..16]),
            PayloadLength: BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..20]));
        return true;
    }

    private static int IndexOfJpegStart(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i <= bytes.Length - 3; i++)
        {
            if (bytes[i] == 0xff && bytes[i + 1] == 0xd8 && bytes[i + 2] == 0xff)
                return i;
        }

        return -1;
    }

    private static int IndexOfJpegEnd(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i <= bytes.Length - 2; i++)
        {
            if (bytes[i] == 0xff && bytes[i + 1] == 0xd9)
                return i;
        }

        return -1;
    }
}

public sealed record A9Vue990ExtractedJpegFrame(int Offset, byte[] Bytes);

public readonly record struct A9Vue990ChannelChunkHeader(
    int Offset,
    byte VersionMajor,
    byte VersionMinor,
    uint StreamCounter,
    uint ChunkIndex,
    uint PayloadLength);
