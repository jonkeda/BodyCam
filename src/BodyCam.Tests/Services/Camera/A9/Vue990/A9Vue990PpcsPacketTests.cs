using BodyCam.Services.Camera.A9.Vue990;
using FluentAssertions;

namespace BodyCam.Tests.Services.Camera.A9.Vue990;

public class A9Vue990PpcsPacketTests
{
    [Fact]
    public void BuildLanSearch_WritesClassicPpcsHeader()
    {
        A9Vue990PpcsPacket.BuildLanSearch().ToArray()
            .Should().Equal(0xf1, 0x30, 0x00, 0x00);
    }

    [Fact]
    public void Xor1Encode_MatchesAioppppDiscoveryReference()
    {
        var encoded = A9Vue990PpcsEncryptionCodec.Xor1Encode(
            A9Vue990PpcsPacket.BuildLanSearch().ToArray());

        encoded.Should().Equal(0x2c, 0xba, 0x5f, 0x5d);
        A9Vue990PpcsEncryptionCodec.ProprietaryEncodeWithKeyBytes(
                [0x69, 0x97, 0xcc, 0x19],
                A9Vue990PpcsPacket.BuildLanSearch().ToArray())
            .Should().Equal(encoded);
        A9Vue990PpcsEncryptionCodec.Xor1Decode(encoded)
            .Should().Equal(0xf1, 0x30, 0x00, 0x00);
    }

    [Fact]
    public void ProprietaryEncryption_RoundTripsWithDerivedKey()
    {
        var plain = Convert.FromHexString("F1D00010110A10300400000000000000");
        var encoded = A9Vue990PpcsEncryptionCodec.ProprietaryEncode("9047F8F88", plain);

        encoded.Should().NotEqual(plain);
        A9Vue990PpcsEncryptionCodec.ProprietaryDecode("9047F8F88", encoded)
            .Should().Equal(plain);
        A9Vue990PpcsEncryptionCodec.DeriveProprietaryKeyBytes("9047F8F88")
            .Should().HaveCount(4);
    }

    [Fact]
    public void TcpRelayEncryption_UsesTwoByteHexSeedKey()
    {
        var plain = Convert.FromHexString("00386800424B4744");
        var encoded = A9Vue990PpcsEncryptionCodec.TcpRelayEncode([0x42, 0x4b], plain);

        encoded.Should().NotEqual(plain);
        A9Vue990PpcsEncryptionCodec.TcpRelayDecode([0x42, 0x4b], encoded)
            .Should().Equal(plain);
        A9Vue990PpcsEncryptionCodec.TcpRelayEncode([0x42, 0x4b], [])
            .Should().BeEmpty();
    }

    [Fact]
    public void TryDecode_ReadsPlainAndXor1Packets()
    {
        A9Vue990PpcsPacket.TryDecode(
                [0xf1, 0x30, 0x00, 0x00],
                out var plainEncryption,
                out var plain)
            .Should().BeTrue();
        plainEncryption.Should().Be(A9Vue990PpcsEncryption.None);
        plain.Type.Should().Be(A9Vue990PpcsPacketType.LanSearch);

        A9Vue990PpcsPacket.TryDecode(
                [0x2c, 0xba, 0x5f, 0x5d],
                out var xorEncryption,
                out var xor)
            .Should().BeTrue();
        xorEncryption.Should().Be(A9Vue990PpcsEncryption.Xor1);
        xor.Type.Should().Be(A9Vue990PpcsPacketType.LanSearch);
    }

    [Fact]
    public void BuildDrw_WrapsChannelIndexAndPayload()
    {
        var packet = A9Vue990PpcsPacket
            .BuildDrw(A9Vue990PpcsPacket.VideoChannel, 0x002a, [0x01, 0x02])
            .ToArray();

        packet.Should().Equal(0xf1, 0xd0, 0x00, 0x06, 0xd1, 0x01, 0x2a, 0x00, 0x01, 0x02);

        A9Vue990PpcsPacket.TryParse(packet, out var parsed).Should().BeTrue();
        parsed.TryReadDrw(out var channel, out var index, out var payload).Should().BeTrue();
        channel.Should().Be(A9Vue990PpcsPacket.VideoChannel);
        index.Should().Be(0x002a);
        payload.ToArray().Should().Equal(0x01, 0x02);
    }

    [Fact]
    public void BuildDrwAck_WritesAioppppAckShape()
    {
        var packet = A9Vue990PpcsPacket
            .BuildDrwAck(A9Vue990PpcsPacket.VideoChannel, 0x002a)
            .ToArray();

        packet.Should().Equal(0xf1, 0xd1, 0x00, 0x06, 0xd2, 0x01, 0x00, 0x01, 0x2a, 0x00);

        A9Vue990PpcsPacket.TryParse(packet, out var parsed).Should().BeTrue();
        parsed.TryReadDrwAck(out var channel, out var index).Should().BeTrue();
        channel.Should().Be(A9Vue990PpcsPacket.VideoChannel);
        index.Should().Be(0x002a);
    }

    [Fact]
    public void VideoFrameAssembler_ReassemblesChunksBetweenBoundaryMarkers()
    {
        var assembler = new A9Vue990VideoFrameAssembler();
        var header = new byte[0x20];
        A9Vue990VideoFrameAssembler.VideoChunkMarker.CopyTo(header);

        assembler.AddVideoDrwChunk(7, [.. header, 0x61, 0x62, 0x63])
            .Should().BeEmpty();
        assembler.AddVideoDrwChunk(8, [0x64, 0x65, 0x66])
            .Should().BeEmpty();

        var frames = assembler.AddVideoDrwChunk(9, [.. header, 0x67, 0x68, 0x69]);

        frames.Should().ContainSingle()
            .Which.Should().Equal(0x61, 0x62, 0x63, 0x64, 0x65, 0x66);
    }
}
