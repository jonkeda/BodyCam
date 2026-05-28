using BodyCam.Services.Camera.A9;
using FluentAssertions;

namespace BodyCam.Tests.Services.Camera.A9;

public class A9ProtocolTests
{
    [Fact]
    public void XqBytesEncDec_RoundTripsPayload()
    {
        var payload = new byte[] { 0x10, 0x21, 0x32, 0x43, 0x54, 0x65, 0x76, 0x87 };
        var original = payload.ToArray();

        A9Protocol.XqBytesEnc(payload, 4);
        payload.Should().NotEqual(original);

        A9Protocol.XqBytesDec(payload, 4);
        payload.Should().Equal(original);
    }

    [Fact]
    public void BuildLanSearch_UsesCommandAndZeroLength()
    {
        var packet = A9Protocol.BuildLanSearch();

        packet.Should().Equal(0xf1, 0x30, 0x00, 0x00);
        A9Protocol.ReadCommandId(packet).Should().Be(A9Protocol.CmdLanSearch);
    }

    [Fact]
    public void BuildP2pAliveAck_UsesCommandAndZeroLength()
    {
        var packet = A9Protocol.BuildP2pAliveAck();

        packet.Should().Equal(0xf1, 0xe1, 0x00, 0x00);
        A9Protocol.ReadCommandId(packet).Should().Be(A9Protocol.CmdP2pAliveAck);
    }

    [Fact]
    public void BuildDrwAck_ContainsStreamAndPacketId()
    {
        var packet = A9Protocol.BuildDrwAck(streamId: 1, packetId: 0x1234);

        A9Protocol.ReadCommandId(packet).Should().Be(A9Protocol.CmdDrwAck);
        A9Protocol.ReadU16BE(packet, 2).Should().Be(6);
        packet[4].Should().Be(0xd2);
        packet[5].Should().Be(1);
        A9Protocol.ReadU16BE(packet, 8).Should().Be(0x1234);
    }

    [Fact]
    public void BuildP2pRdy_EchoesPunchPayload()
    {
        var punchPayload = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();

        var packet = A9Protocol.BuildP2pRdy(punchPayload);

        A9Protocol.ReadCommandId(packet).Should().Be(A9Protocol.CmdP2pRdy);
        A9Protocol.ReadU16BE(packet, 2).Should().Be(20);
        packet.Skip(4).Should().Equal(punchPayload);
    }

    [Fact]
    public void BuildConnectUser_EncryptsCredentialsAndIncrementsCommandId()
    {
        var commandId = 7;
        var ticket = new byte[] { 1, 2, 3, 4 };

        var packet = A9Protocol.BuildConnectUser(ref commandId, ticket, "admin", "secret");

        commandId.Should().Be(8);
        A9Protocol.ReadCommandId(packet).Should().Be(A9Protocol.CmdDrw);
        A9Protocol.ReadU16BE(packet, 6).Should().Be(7);
        A9Protocol.ReadU16BE(packet, 10).Should().Be(A9Protocol.CtrlConnectUser);
        A9Protocol.ReadU16LE(packet, 12).Should().Be(164);
        packet.Skip(16).Take(4).Should().Equal(ticket);

        var credentials = packet.Skip(20).Take(160).ToArray();
        A9Protocol.XqBytesDec(credentials, 4);
        System.Text.Encoding.ASCII.GetString(credentials, 0, 5).Should().Be("admin");
        System.Text.Encoding.ASCII.GetString(credentials, 0x20, 6).Should().Be("secret");
    }

    [Fact]
    public void ReadCommandId_WithMalformedPacket_Throws()
    {
        var packet = new byte[] { 0xf1 };

        var act = () => A9Protocol.ReadCommandId(packet);

        act.Should().Throw<IndexOutOfRangeException>();
    }
}
