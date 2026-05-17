using System.Buffers.Binary;
using System.Text;

namespace BodyCam.Services.Glasses.HeyCyan.Media;

/// <summary>
/// Wraps raw HeyCyan OPUS recordings into playable Ogg/Opus containers.
/// The HeyCyan firmware emits headerless 40-byte raw OPUS packets;
/// this wrapper detects framing and emits a valid Ogg stream.
/// </summary>
public static class OpusOggWrapper
{
    public const int DefaultSampleRate = 16000;
    public const int DefaultChannels = 1;
    public const int DefaultPacketSize = 40;

    private const int MaxPacketSize = 8192;
    private const int MaxOutputRatio = 2;
    private const int MaxOutputOverhead = 64 * 1024;
    private const int MinLengthPrefixedPackets = 3;

    /// <summary>
    /// Detect the framing format of a raw OPUS byte stream.
    /// </summary>
    public static OpusFraming Detect(ReadOnlySpan<byte> raw)
    {
        // 1. Check for existing Ogg container (MUST come first)
        if (raw.Length >= 4 &&
            raw[0] == 0x4F && // 'O'
            raw[1] == 0x67 && // 'g'
            raw[2] == 0x67 && // 'g'
            raw[3] == 0x53)   // 'S'
        {
            return OpusFraming.OggContainer;
        }

        // 2. Try length-prefixed formats
        if (TryLengthPrefixedU16Le(raw, out int packetsLe) && packetsLe >= MinLengthPrefixedPackets)
            return OpusFraming.LengthPrefixedU16Le;

        if (TryLengthPrefixedU16Be(raw, out int packetsBe) && packetsBe >= MinLengthPrefixedPackets)
            return OpusFraming.LengthPrefixedU16Be;

        if (TryLengthPrefixedU8(raw, out int packetsU8) && packetsU8 >= MinLengthPrefixedPackets)
            return OpusFraming.LengthPrefixedU8;

        // 3. Check for fixed 40-byte packets
        if (raw.Length >= 40 && raw.Length % 40 == 0)
            return OpusFraming.FixedPacket40;

        // 4. Unknown framing
        return OpusFraming.Unknown;
    }

    /// <summary>
    /// Wrap raw OPUS bytes into a playable Ogg/Opus container.
    /// </summary>
    public static byte[] WrapToOgg(
        ReadOnlySpan<byte> raw,
        OpusFraming framing,
        int sampleRate = DefaultSampleRate,
        int channels = DefaultChannels,
        int packetSize = DefaultPacketSize)
    {
        // Validate inputs
        if (channels < 1 || channels > 2)
            throw new ArgumentOutOfRangeException(nameof(channels), "Must be 1 or 2");
        if (packetSize < 1 || packetSize > MaxPacketSize)
            throw new ArgumentOutOfRangeException(nameof(packetSize), $"Must be between 1 and {MaxPacketSize}");

        // Pass through existing Ogg container
        if (framing == OpusFraming.OggContainer)
            return raw.ToArray();

        // Extract packets based on framing
        List<byte[]> packets = ExtractPackets(raw, framing, packetSize);
        if (packets.Count == 0)
            return Array.Empty<byte>();

        // Write Ogg container
        var writer = new OggWriter(sampleRate, channels);
        foreach (var packet in packets)
        {
            writer.AddPacket(packet);
        }

        byte[] result = writer.Finish();

        // Defensive cap: if output is unexpectedly huge, retry with FixedPacket40
        int maxOutput = raw.Length * MaxOutputRatio + MaxOutputOverhead;
        if (result.Length > maxOutput && framing != OpusFraming.FixedPacket40 && framing != OpusFraming.Unknown)
        {
            return WrapToOgg(raw, OpusFraming.FixedPacket40, sampleRate, channels, packetSize);
        }

        return result;
    }

    /// <summary>
    /// Auto-detect framing and wrap to Ogg/Opus.
    /// </summary>
    public static byte[] AutoWrap(ReadOnlySpan<byte> raw)
        => WrapToOgg(raw, Detect(raw));

    private static List<byte[]> ExtractPackets(ReadOnlySpan<byte> raw, OpusFraming framing, int packetSize)
    {
        var packets = new List<byte[]>();

        switch (framing)
        {
            case OpusFraming.FixedPacket40:
            case OpusFraming.Unknown:
                // Split into fixed-size chunks, drop trailing partial packet
                for (int i = 0; i + packetSize <= raw.Length; i += packetSize)
                {
                    packets.Add(raw.Slice(i, packetSize).ToArray());
                }
                break;

            case OpusFraming.LengthPrefixedU16Le:
                ExtractLengthPrefixed(raw, packets, 2, span => BinaryPrimitives.ReadUInt16LittleEndian(span));
                break;

            case OpusFraming.LengthPrefixedU16Be:
                ExtractLengthPrefixed(raw, packets, 2, span => BinaryPrimitives.ReadUInt16BigEndian(span));
                break;

            case OpusFraming.LengthPrefixedU8:
                ExtractLengthPrefixed(raw, packets, 1, span => (int)span[0]);
                break;
        }

        return packets;
    }

    private static void ExtractLengthPrefixed(
        ReadOnlySpan<byte> raw,
        List<byte[]> packets,
        int lengthSize,
        Func<ReadOnlySpan<byte>, int> readLength)
    {
        int pos = 0;
        while (pos + lengthSize <= raw.Length)
        {
            int len = readLength(raw.Slice(pos, lengthSize));
            pos += lengthSize;

            if (len <= 0 || len > MaxPacketSize || pos + len > raw.Length)
                break;

            packets.Add(raw.Slice(pos, len).ToArray());
            pos += len;
        }
    }

    private static bool TryLengthPrefixedU16Le(ReadOnlySpan<byte> raw, out int packetCount)
    {
        packetCount = 0;
        int pos = 0;

        while (pos + 2 <= raw.Length)
        {
            int len = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(pos, 2));
            pos += 2;

            if (len <= 0 || len > MaxPacketSize || pos + len > raw.Length)
                return false;

            packetCount++;
            pos += len;
        }

        return pos == raw.Length; // Must consume exactly all bytes
    }

    private static bool TryLengthPrefixedU16Be(ReadOnlySpan<byte> raw, out int packetCount)
    {
        packetCount = 0;
        int pos = 0;

        while (pos + 2 <= raw.Length)
        {
            int len = BinaryPrimitives.ReadUInt16BigEndian(raw.Slice(pos, 2));
            pos += 2;

            if (len <= 0 || len > MaxPacketSize || pos + len > raw.Length)
                return false;

            packetCount++;
            pos += len;
        }

        return pos == raw.Length;
    }

    private static bool TryLengthPrefixedU8(ReadOnlySpan<byte> raw, out int packetCount)
    {
        packetCount = 0;
        int pos = 0;

        while (pos + 1 <= raw.Length)
        {
            int len = raw[pos];
            pos += 1;

            if (len <= 0 || pos + len > raw.Length)
                return false;

            packetCount++;
            pos += len;
        }

        return pos == raw.Length;
    }

    private sealed class OggWriter
    {
        private const uint OggCrcPolynomial = 0x04C11DB7;
        private const int SerialNumber = 1;
        private const int GranuleSamplesPerPacket = 960; // 20ms at 48kHz reference

        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly MemoryStream _buffer;
        private int _pageSequence;
        private long _granulePosition;

        public OggWriter(int sampleRate, int channels)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _buffer = new MemoryStream();
            _pageSequence = 0;
            _granulePosition = 0;

            WriteIdentificationHeader();
            WriteCommentHeader();
        }

        public void AddPacket(byte[] packet)
        {
            _granulePosition += GranuleSamplesPerPacket;
            WritePage(_granulePosition, 0x00, packet);
        }

        public byte[] Finish()
        {
            // Mark last page with EOS flag
            if (_pageSequence > 2)
            {
                // Already wrote data pages; re-flag the last one is complex,
                // so we'll just add an empty EOS page
                WritePage(_granulePosition, 0x04, Array.Empty<byte>());
            }

            return _buffer.ToArray();
        }

        private void WriteIdentificationHeader()
        {
            var payload = new byte[19];
            int pos = 0;

            // Magic signature "OpusHead" (8 bytes)
            Encoding.ASCII.GetBytes("OpusHead").CopyTo(payload, pos);
            pos += 8;

            payload[pos++] = 1; // Version
            payload[pos++] = (byte)_channels;

            // Pre-skip (u16 LE): 3840 samples
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(pos), 3840);
            pos += 2;

            // Input sample rate (u32 LE)
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(pos), (uint)_sampleRate);
            pos += 4;

            // Output gain (i16 LE): 0
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(pos), 0);
            pos += 2;

            payload[pos++] = 0; // Channel mapping family

            WritePage(0, 0x02, payload); // BOS flag
        }

        private void WriteCommentHeader()
        {
            var vendor = Encoding.UTF8.GetBytes("BodyCam");
            var payload = new byte[8 + 4 + vendor.Length + 4];
            int pos = 0;

            // Magic signature "OpusTags"
            Encoding.ASCII.GetBytes("OpusTags").CopyTo(payload, pos);
            pos += 8;

            // Vendor string length (u32 LE)
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(pos), (uint)vendor.Length);
            pos += 4;

            // Vendor string
            vendor.CopyTo(payload, pos);
            pos += vendor.Length;

            // User comment list length (u32 LE): 0
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(pos), 0);

            WritePage(0, 0x00, payload);
        }

        private void WritePage(long granulePosition, byte headerType, byte[] segment)
        {
            int segmentCount = segment.Length == 0 ? 0 : (segment.Length + 254) / 255;
            byte[] segmentTable = new byte[segmentCount == 0 ? 1 : segmentCount];

            if (segment.Length > 0)
            {
                int remaining = segment.Length;
                int idx = 0;
                while (remaining > 0)
                {
                    int chunkSize = Math.Min(remaining, 255);
                    segmentTable[idx++] = (byte)chunkSize;
                    remaining -= chunkSize;
                }
            }
            else
            {
                segmentTable[0] = 0;
            }

            int headerSize = 27 + segmentTable.Length;
            byte[] page = new byte[headerSize + segment.Length];
            int pos = 0;

            // Capture pattern "OggS"
            page[pos++] = 0x4F; // 'O'
            page[pos++] = 0x67; // 'g'
            page[pos++] = 0x67; // 'g'
            page[pos++] = 0x53; // 'S'

            page[pos++] = 0; // Stream structure version
            page[pos++] = headerType; // Header type flags

            // Granule position (i64 LE)
            BinaryPrimitives.WriteInt64LittleEndian(page.AsSpan(pos), granulePosition);
            pos += 8;

            // Serial number (u32 LE)
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(pos), SerialNumber);
            pos += 4;

            // Page sequence (u32 LE)
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(pos), (uint)_pageSequence++);
            pos += 4;

            // CRC checksum (u32 LE): placeholder
            int crcPos = pos;
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(pos), 0);
            pos += 4;

            // Number of segments
            page[pos++] = (byte)segmentTable.Length;

            // Segment table
            segmentTable.CopyTo(page, pos);
            pos += segmentTable.Length;

            // Segment data
            segment.CopyTo(page, pos);

            // Compute and write CRC
            uint crc = ComputeOggCrc(page);
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(crcPos), crc);

            _buffer.Write(page);
        }

        private static uint ComputeOggCrc(byte[] data)
        {
            uint crc = 0;

            foreach (byte b in data)
            {
                crc ^= (uint)b << 24;

                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x80000000) != 0)
                        crc = (crc << 1) ^ OggCrcPolynomial;
                    else
                        crc <<= 1;
                }
            }

            return crc;
        }
    }
}
