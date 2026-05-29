using BodyCam.Services.Camera.A9.Vue990;
using FluentAssertions;

namespace BodyCam.Tests.Services.Camera.A9.Vue990;

public sealed class A9Vue990Hlp2pDirectPacketTests
{
    [Fact]
    public void BuildLanHoleProbe_MatchesSocketHookVector()
    {
        var seed = new A9Vue990Hlp2pLanHoleSeed(
            Convert.FromHexString("C1F3ECE4"),
            Convert.FromHexString("8AB8F6F4"),
            Convert.FromHexString("7A46F89D"),
            Convert.FromHexString("B85C64E8"),
            Convert.FromHexString("2EEA4A01"),
            0x29E669CB);

        A9Vue990Hlp2pDirectPacket.BuildLanHoleProbe(seed)
            .Should().Equal(Convert.FromHexString("02C1F3ECE48AB8F6F47A46F89DB85C64E82EEA4A01D6253501"));
    }

    [Fact]
    public void ParseLanHoleResponse_AndBuildAck_MatchesSocketHookVector()
    {
        var bytes = Convert.FromHexString("11B836B4252EEA4A0175006B253501");

        A9Vue990Hlp2pDirectPacket.TryParseLanHoleResponse(bytes, out var response)
            .Should().BeTrue();
        response.AidLittleEndian.Should().Be(0x25B436B8);
        response.SessionToken.Should().Equal(Convert.FromHexString("2EEA4A01"));
        response.Status.Should().Be(0x75);
        response.StatusDetail.Should().Be(0x00);

        A9Vue990Hlp2pDirectPacket.BuildLanHoleAck(response, 0x29E669CB)
            .Should().Equal(Convert.FromHexString("1175B836B4252EEA4A01D6253501CB69E629"));
    }

    [Fact]
    public void ParseLanHoleReady_MatchesSocketHookVector()
    {
        var bytes = Convert.FromHexString("15B836B4256B253501");

        A9Vue990Hlp2pDirectPacket.TryParseLanHoleReady(bytes, out var ready)
            .Should().BeTrue();
        ready.AidLittleEndian.Should().Be(0x25B436B8);
    }

    [Fact]
    public void IsAliveProbe_AcceptsObservedCompactProbeVariants()
    {
        A9Vue990Hlp2pDirectPacket.IsAliveProbe(Convert.FromHexString("0B0000"))
            .Should().BeTrue();
        A9Vue990Hlp2pDirectPacket.IsAliveProbe(Convert.FromHexString("0B0002"))
            .Should().BeTrue();
    }

    [Fact]
    public void ParseDirectVideoHeader_AndBuildAck_MatchesSocketHookVector()
    {
        var bytes = Convert.FromHexString(
            "0D0002010000000029000000010055AA15A8030300007295495AEB33010078250000811603028CD100020C000002");

        A9Vue990Hlp2pDirectPacket.TryParseDirectDataPacket(bytes, out var packet)
            .Should().BeTrue();
        packet.Sequence.Should().Be(2);
        packet.Operation.Should().Be(A9Vue990Hlp2pDirectPacket.DirectDataOperation);
        packet.MessageId.Should().Be(0);
        packet.TailLength.Should().Be(0x29);
        packet.FragmentIndex.Should().Be(0);
        packet.Kind.Should().Be(1);
        packet.Channel.Should().Be(0);
        packet.Payload.Should().StartWith(A9Vue990VideoFrameAssembler.VideoChunkMarker.ToArray());

        A9Vue990Hlp2pDirectPacket.BuildDirectAck(3, packet)
            .Should().Equal(Convert.FromHexString("0D00030800000000020021"));
    }

    [Fact]
    public void ParseDirectJpegFragment_AndBuildAck_MatchesSocketHookVector()
    {
        var bytes = new byte[5 + 0x0454];
        Convert.FromHexString("0D00030100000104540000000900FFD8FFE000104A46494600010101012C012C0000")
            .CopyTo(bytes.AsSpan());

        A9Vue990Hlp2pDirectPacket.TryParseDirectDataPacket(bytes, out var packet)
            .Should().BeTrue();
        packet.Sequence.Should().Be(3);
        packet.MessageId.Should().Be(1);
        packet.TailLength.Should().Be(0x0454);
        packet.Kind.Should().Be(9);
        packet.Payload.Should().StartWith([0xff, 0xd8, 0xff, 0xe0]);

        A9Vue990Hlp2pDirectPacket.BuildDirectAck(4, packet)
            .Should().Equal(Convert.FromHexString("0D0004080000010003044C"));
    }
}
