using BodyCam.Services.Glasses.HeyCyan.Media;
using FluentAssertions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan.Media;

public class OpusOggWrapperTests
{
    [Fact]
    public void Detect_on_OggS_magic_returns_OggContainer()
    {
        var ogg = new byte[] { 0x4F, 0x67, 0x67, 0x53, 0x00, 0x02 }; // "OggS" + version + header type

        var framing = OpusOggWrapper.Detect(ogg);

        framing.Should().Be(OpusFraming.OggContainer);
    }

    [Fact]
    public void WrapToOgg_with_OggContainer_returns_input_unchanged()
    {
        var ogg = new byte[] { 0x4F, 0x67, 0x67, 0x53, 0x00, 0x02, 0x00, 0x00 };

        var result = OpusOggWrapper.WrapToOgg(ogg, OpusFraming.OggContainer);

        result.Should().Equal(ogg);
    }

    [Fact]
    public void Detect_on_40_byte_aligned_random_returns_FixedPacket40()
    {
        var raw = new byte[40 * 5]; // 5 packets worth
        Random.Shared.NextBytes(raw);

        var framing = OpusOggWrapper.Detect(raw);

        framing.Should().Be(OpusFraming.FixedPacket40);
    }

    [Fact]
    public void Detect_on_39_bytes_returns_Unknown()
    {
        var raw = new byte[39];

        var framing = OpusOggWrapper.Detect(raw);

        framing.Should().Be(OpusFraming.Unknown);
    }

    [Fact]
    public void Detect_on_length_prefixed_u16_le_returns_correct_framing()
    {
        // Build synthetic stream: 5 packets of varying sizes
        var packets = new byte[][] {
            new byte[60],
            new byte[80],
            new byte[40],
            new byte[120],
            new byte[50]
        };

        var stream = BuildLengthPrefixedU16Le(packets);

        var framing = OpusOggWrapper.Detect(stream);

        framing.Should().Be(OpusFraming.LengthPrefixedU16Le);
    }

    [Fact]
    public void Detect_on_length_prefixed_u16_be_returns_correct_framing()
    {
        var packets = new byte[][] {
            new byte[60],
            new byte[80],
            new byte[40]
        };

        var stream = BuildLengthPrefixedU16Be(packets);

        var framing = OpusOggWrapper.Detect(stream);

        framing.Should().Be(OpusFraming.LengthPrefixedU16Be);
    }

    [Fact]
    public void Detect_on_length_prefixed_u8_returns_correct_framing()
    {
        var packets = new byte[][] {
            new byte[60],
            new byte[80],
            new byte[40]
        };

        var stream = BuildLengthPrefixedU8(packets);

        var framing = OpusOggWrapper.Detect(stream);

        framing.Should().Be(OpusFraming.LengthPrefixedU8);
    }

    [Fact]
    public void WrapToOgg_FixedPacket40_produces_valid_Ogg_structure()
    {
        var raw = new byte[40 * 10]; // 10 packets
        Random.Shared.NextBytes(raw);

        var result = OpusOggWrapper.WrapToOgg(raw, OpusFraming.FixedPacket40);

        // Must start with OggS
        result.Should().StartWith(new byte[] { 0x4F, 0x67, 0x67, 0x53 });

        // Parse pages
        var pages = ParseOggPages(result);

        // Should have: OpusHead (BOS), OpusTags, N data pages, EOS marker
        pages.Count.Should().BeGreaterThanOrEqualTo(12); // 2 headers + 10 data
        
        // First page: OpusHead with BOS flag
        pages[0].HeaderType.Should().HaveFlag(OggHeaderType.BOS);
        pages[0].Payload.Should().StartWith("OpusHead"u8.ToArray());

        // Second page: OpusTags
        pages[1].Payload.Should().StartWith("OpusTags"u8.ToArray());

        // Data pages follow
        pages.Skip(2).Take(10).Should().AllSatisfy(p => p.Payload.Length.Should().Be(40));
    }

    [Fact]
    public void WrapToOgg_granule_position_advances_correctly()
    {
        var raw = new byte[40 * 5]; // 5 packets
        Random.Shared.NextBytes(raw);

        var result = OpusOggWrapper.WrapToOgg(raw, OpusFraming.FixedPacket40);
        var pages = ParseOggPages(result);

        // Skip headers (first 2 pages)
        var dataPages = pages.Skip(2).ToList();

        // Each 20ms packet advances granule by 960 samples at 48kHz reference
        for (int i = 0; i < dataPages.Count && i < 5; i++)
        {
            long expectedGranule = (i + 1) * 960;
            dataPages[i].GranulePosition.Should().Be(expectedGranule);
        }
    }

    [Fact]
    public void WrapToOgg_crc_validates_for_all_pages()
    {
        var raw = new byte[40 * 3];
        Random.Shared.NextBytes(raw);

        var result = OpusOggWrapper.WrapToOgg(raw, OpusFraming.FixedPacket40);
        var pages = ParseOggPages(result);

        // All pages should have valid CRCs (parser validates during parse)
        pages.Should().NotBeEmpty();
        pages.Should().AllSatisfy(p => p.CrcValid.Should().BeTrue());
    }

    [Fact]
    public void WrapToOgg_LastPage_HasEosFlag()
    {
        var raw = new byte[40 * 5]; // 5 packets
        Random.Shared.NextBytes(raw);

        var result = OpusOggWrapper.WrapToOgg(raw, OpusFraming.FixedPacket40);
        var pages = ParseOggPages(result);

        // Last page should have EOS flag set
        pages.Should().NotBeEmpty();
        pages[^1].HeaderType.Should().HaveFlag(OggHeaderType.EOS);
    }

    [Fact]
    public void WrapToOgg_random_garbage_produces_structurally_valid_Ogg()
    {
        var raw = new byte[1024];
        Random.Shared.NextBytes(raw);

        var result = OpusOggWrapper.AutoWrap(raw);

        // Should parse without throwing
        var pages = ParseOggPages(result);
        pages.Should().NotBeEmpty();
        pages[0].Payload.Should().StartWith("OpusHead"u8.ToArray());
    }

    // Note: AutoWrap_allocation_bounded is omitted — GC behavior is non-deterministic
    // and the wave doc specifies this as "verified in benchmark, not a hard test gate".

    [Fact]
    public void WrapToOgg_drops_partial_final_packet()
    {
        // 2.5 packets worth of data (100 bytes)
        var raw = new byte[100];
        Random.Shared.NextBytes(raw);

        var result = OpusOggWrapper.WrapToOgg(raw, OpusFraming.FixedPacket40);
        var pages = ParseOggPages(result);

        // Should have exactly 2 data packets (80 bytes consumed, 20 bytes dropped)
        var dataPages = pages.Skip(2).Where(p => p.Payload.Length > 0).ToList();
        dataPages.Count.Should().Be(2);
    }

    [Fact]
    public void WrapToOgg_throws_on_invalid_channel_count()
    {
        var raw = new byte[40];

        var act = () => OpusOggWrapper.WrapToOgg(raw, OpusFraming.FixedPacket40, channels: 0);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("channels");
    }

    [Fact]
    public void WrapToOgg_throws_on_invalid_packet_size()
    {
        var raw = new byte[40];

        var act = () => OpusOggWrapper.WrapToOgg(raw, OpusFraming.FixedPacket40, packetSize: 0);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("packetSize");
    }

    [Fact]
    public void WrapToOgg_length_prefixed_u16_le_extracts_packets_correctly()
    {
        var packets = new byte[][] {
            new byte[60],
            new byte[80],
            new byte[40]
        };
        Random.Shared.NextBytes(packets[0]);
        Random.Shared.NextBytes(packets[1]);
        Random.Shared.NextBytes(packets[2]);

        var stream = BuildLengthPrefixedU16Le(packets);
        var result = OpusOggWrapper.WrapToOgg(stream, OpusFraming.LengthPrefixedU16Le);
        var pages = ParseOggPages(result);

        var dataPages = pages.Skip(2).Where(p => p.Payload.Length > 0).ToList();
        dataPages.Count.Should().Be(3);
        dataPages[0].Payload.Should().Equal(packets[0]);
        dataPages[1].Payload.Should().Equal(packets[1]);
        dataPages[2].Payload.Should().Equal(packets[2]);
    }

    [Fact]
    public void AutoWrap_detects_and_wraps_FixedPacket40()
    {
        var raw = new byte[40 * 3];
        Random.Shared.NextBytes(raw);

        var result = OpusOggWrapper.AutoWrap(raw);

        result.Should().StartWith(new byte[] { 0x4F, 0x67, 0x67, 0x53 });
        var pages = ParseOggPages(result);
        pages.Should().NotBeEmpty();
    }

    [Fact]
    public void OpusHead_contains_correct_sample_rate()
    {
        var raw = new byte[40];
        const int sampleRate = 16000;

        var result = OpusOggWrapper.WrapToOgg(raw, OpusFraming.FixedPacket40, sampleRate: sampleRate);
        var pages = ParseOggPages(result);

        var headPayload = pages[0].Payload;
        // OpusHead layout: magic(8) + version(1) + channels(1) + pre-skip(2) + input_sample_rate(4) + ...
        var inputSampleRate = BitConverter.ToUInt32(headPayload, 12);
        inputSampleRate.Should().Be((uint)sampleRate);
    }

    [Fact]
    public void OpusTags_contains_BodyCam_vendor()
    {
        var raw = new byte[40];

        var result = OpusOggWrapper.WrapToOgg(raw, OpusFraming.FixedPacket40);
        var pages = ParseOggPages(result);

        var tagsPayload = pages[1].Payload;
        // OpusTags layout: magic(8) + vendor_length(4) + vendor_string + ...
        var vendorLength = BitConverter.ToUInt32(tagsPayload, 8);
        var vendor = System.Text.Encoding.UTF8.GetString(tagsPayload, 12, (int)vendorLength);
        vendor.Should().Be("BodyCam");
    }

    [Fact]
    public void Fixture_FixedPacket40_DetectsCorrectly()
    {
        var fixtureBytes = LoadFixture("fixed-packet-40.bin");
        fixtureBytes.Length.Should().Be(4000, "fixture should be 100 * 40 bytes");

        var framing = OpusOggWrapper.Detect(fixtureBytes);

        framing.Should().Be(OpusFraming.FixedPacket40);
    }

    [Fact]
    public void Fixture_FixedPacket40_WrapsSuccessfully()
    {
        var fixtureBytes = LoadFixture("fixed-packet-40.bin");

        var result = OpusOggWrapper.AutoWrap(fixtureBytes);

        result.Should().StartWith(new byte[] { 0x4F, 0x67, 0x67, 0x53 });
        var pages = ParseOggPages(result);
        pages.Should().NotBeEmpty();
        pages[0].Payload.Should().StartWith("OpusHead"u8.ToArray());
    }

    [Fact]
    public void Fixture_LenPrefixU16Le_DetectsCorrectly()
    {
        var fixtureBytes = LoadFixture("len-prefix-u16le.bin");

        var framing = OpusOggWrapper.Detect(fixtureBytes);

        framing.Should().Be(OpusFraming.LengthPrefixedU16Le);
    }

    [Fact]
    public void Fixture_LenPrefixU16Le_ExtractsPacketsCorrectly()
    {
        var fixtureBytes = LoadFixture("len-prefix-u16le.bin");

        var result = OpusOggWrapper.WrapToOgg(fixtureBytes, OpusFraming.LengthPrefixedU16Le);
        var pages = ParseOggPages(result);

        // Skip headers (OpusHead + OpusTags)
        var dataPages = pages.Skip(2).Where(p => p.Payload.Length > 0).ToList();
        dataPages.Count.Should().Be(5, "fixture has 5 packets");
    }

    // === Test Helpers ===

    private static byte[] BuildLengthPrefixedU16Le(byte[][] packets)
    {
        using var ms = new MemoryStream();
        foreach (var p in packets)
        {
            ms.Write(BitConverter.GetBytes((ushort)p.Length));
            ms.Write(p);
        }
        return ms.ToArray();
    }

    private static byte[] BuildLengthPrefixedU16Be(byte[][] packets)
    {
        using var ms = new MemoryStream();
        foreach (var p in packets)
        {
            ushort len = (ushort)p.Length;
            ms.WriteByte((byte)(len >> 8));
            ms.WriteByte((byte)(len & 0xFF));
            ms.Write(p);
        }
        return ms.ToArray();
    }

    private static byte[] BuildLengthPrefixedU8(byte[][] packets)
    {
        using var ms = new MemoryStream();
        foreach (var p in packets)
        {
            if (p.Length > 255)
                throw new ArgumentException("U8 length-prefixed packets must be <= 255 bytes");
            ms.WriteByte((byte)p.Length);
            ms.Write(p);
        }
        return ms.ToArray();
    }

    private static List<OggPage> ParseOggPages(byte[] data)
    {
        var pages = new List<OggPage>();
        int pos = 0;

        while (pos + 27 <= data.Length)
        {
            // Verify capture pattern
            if (data[pos] != 0x4F || data[pos + 1] != 0x67 ||
                data[pos + 2] != 0x67 || data[pos + 3] != 0x53)
            {
                throw new InvalidOperationException($"Invalid Ogg page at offset {pos}");
            }

            var version = data[pos + 4];
            var headerType = (OggHeaderType)data[pos + 5];
            var granulePosition = BitConverter.ToInt64(data, pos + 6);
            var serialNumber = BitConverter.ToUInt32(data, pos + 14);
            var sequenceNumber = BitConverter.ToUInt32(data, pos + 18);
            var checksumStored = BitConverter.ToUInt32(data, pos + 22);
            var segmentCount = data[pos + 26];

            int headerSize = 27 + segmentCount;
            if (pos + headerSize > data.Length)
                throw new InvalidOperationException("Truncated Ogg page header");

            var segmentTable = data.AsSpan(pos + 27, segmentCount).ToArray();
            int payloadSize = segmentTable.Sum(s => (int)s);

            if (pos + headerSize + payloadSize > data.Length)
                throw new InvalidOperationException("Truncated Ogg page payload");

            var payload = data.AsSpan(pos + headerSize, payloadSize).ToArray();

            // Validate CRC
            var pageForCrc = data.AsSpan(pos, headerSize + payloadSize).ToArray();
            Array.Clear(pageForCrc, 22, 4); // Zero out CRC field
            var computedCrc = ComputeOggCrc(pageForCrc);
            bool crcValid = computedCrc == checksumStored;

            pages.Add(new OggPage(
                headerType,
                granulePosition,
                serialNumber,
                sequenceNumber,
                payload,
                crcValid));

            pos += headerSize + payloadSize;
        }

        return pages;
    }

    private static uint ComputeOggCrc(byte[] data)
    {
        const uint polynomial = 0x04C11DB7;
        uint crc = 0;

        foreach (byte b in data)
        {
            crc ^= (uint)b << 24;

            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x80000000) != 0)
                    crc = (crc << 1) ^ polynomial;
                else
                    crc <<= 1;
            }
        }

        return crc;
    }

    private record OggPage(
        OggHeaderType HeaderType,
        long GranulePosition,
        uint SerialNumber,
        uint SequenceNumber,
        byte[] Payload,
        bool CrcValid);

    [Flags]
    private enum OggHeaderType : byte
    {
        None = 0x00,
        Continued = 0x01,
        BOS = 0x02,
        EOS = 0x04
    }

    private static byte[] LoadFixture(string fileName)
    {
        var assembly = typeof(OpusOggWrapperTests).Assembly;
        var resourceName = $"BodyCam.Tests.Fixtures.HeyCyan.Media.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new FileNotFoundException($"Fixture not found: {resourceName}");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
