using System.Text;

namespace BodyCam.A9PhoneProbe;

internal static class MjpegAviWriter
{
    public static void Write(string path, IReadOnlyList<byte[]> frames, int width, int height, int framesPerSecond)
    {
        if (frames.Count == 0)
            throw new ArgumentException("At least one frame is required.", nameof(frames));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (framesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond));

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.ASCII, leaveOpen: true);

        var maxFrameSize = frames.Max(frame => frame.Length);
        var riffSize = BeginRiff(writer, "AVI ");
        var hdrlSize = BeginList(writer, "hdrl");

        WriteChunk(writer, "avih", body =>
        {
            body.Write(1_000_000 / framesPerSecond);
            body.Write(maxFrameSize * framesPerSecond);
            body.Write(0);
            body.Write(0x10);
            body.Write(frames.Count);
            body.Write(0);
            body.Write(1);
            body.Write(maxFrameSize);
            body.Write(width);
            body.Write(height);
            body.Write(0);
            body.Write(0);
            body.Write(0);
            body.Write(0);
        });

        var strlSize = BeginList(writer, "strl");
        WriteChunk(writer, "strh", body =>
        {
            WriteFourCc(body, "vids");
            WriteFourCc(body, "MJPG");
            body.Write(0);
            body.Write((short)0);
            body.Write((short)0);
            body.Write(0);
            body.Write(1);
            body.Write(framesPerSecond);
            body.Write(0);
            body.Write(frames.Count);
            body.Write(maxFrameSize);
            body.Write(-1);
            body.Write(0);
            body.Write(0);
            body.Write(0);
            body.Write(width);
            body.Write(height);
        });

        WriteChunk(writer, "strf", body =>
        {
            body.Write(40);
            body.Write(width);
            body.Write(height);
            body.Write((short)1);
            body.Write((short)24);
            WriteFourCc(body, "MJPG");
            body.Write(width * height * 3);
            body.Write(0);
            body.Write(0);
            body.Write(0);
            body.Write(0);
        });
        EndChunk(writer, strlSize, padToWord: true);
        EndChunk(writer, hdrlSize, padToWord: true);

        var moviSize = BeginList(writer, "movi");
        var moviDataStart = writer.BaseStream.Position;
        var index = new List<AviIndexEntry>(frames.Count);
        foreach (var frame in frames)
        {
            var offset = checked((uint)(writer.BaseStream.Position - moviDataStart));
            WriteFourCc(writer, "00dc");
            writer.Write(frame.Length);
            writer.Write(frame);
            if ((frame.Length & 1) != 0)
                writer.Write((byte)0);

            index.Add(new AviIndexEntry(offset, checked((uint)frame.Length)));
        }
        EndChunk(writer, moviSize, padToWord: true);

        WriteChunk(writer, "idx1", body =>
        {
            foreach (var entry in index)
            {
                WriteFourCc(body, "00dc");
                body.Write(0x10);
                body.Write(entry.Offset);
                body.Write(entry.Size);
            }
        });

        EndChunk(writer, riffSize, padToWord: false);
        File.WriteAllBytes(path, memory.ToArray());
    }

    private static long BeginRiff(BinaryWriter writer, string type)
    {
        WriteFourCc(writer, "RIFF");
        var sizePosition = writer.BaseStream.Position;
        writer.Write(0);
        WriteFourCc(writer, type);
        return sizePosition;
    }

    private static long BeginList(BinaryWriter writer, string type)
    {
        WriteFourCc(writer, "LIST");
        var sizePosition = writer.BaseStream.Position;
        writer.Write(0);
        WriteFourCc(writer, type);
        return sizePosition;
    }

    private static void WriteChunk(BinaryWriter writer, string id, Action<BinaryWriter> writeBody)
    {
        WriteFourCc(writer, id);
        var sizePosition = writer.BaseStream.Position;
        writer.Write(0);
        writeBody(writer);
        EndChunk(writer, sizePosition, padToWord: true);
    }

    private static void EndChunk(BinaryWriter writer, long sizePosition, bool padToWord)
    {
        var endPosition = writer.BaseStream.Position;
        var size = checked((int)(endPosition - sizePosition - sizeof(int)));
        writer.BaseStream.Position = sizePosition;
        writer.Write(size);
        writer.BaseStream.Position = endPosition;

        if (padToWord && (size & 1) != 0)
            writer.Write((byte)0);
    }

    private static void WriteFourCc(BinaryWriter writer, string value)
    {
        if (value.Length != 4)
            throw new ArgumentException("FourCC values must be exactly four ASCII characters.", nameof(value));

        writer.Write(Encoding.ASCII.GetBytes(value));
    }

    private readonly record struct AviIndexEntry(uint Offset, uint Size);
}
