using BodyCam.Services.Camera.A9.Vue990;
using FluentAssertions;

namespace BodyCam.Tests.Services.Camera.A9.Vue990;

public class A9Vue990PpcsControlCommandBuilderTests
{
    [Fact]
    public void BuildStartVideo_WritesDrwControlPacket()
    {
        ushort sequence = 5;

        var packet = A9Vue990PpcsControlCommandBuilder.BuildStartVideo(ref sequence, [0, 0, 0, 0]);

        packet.Should().Equal(
            0xf1, 0xd0, 0x00, 0x10,
            0xd1, 0x00, 0x05, 0x00,
            0x11, 0x0a, 0x10, 0x30,
            0x04, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00);
        sequence.Should().Be(6);
    }

    [Fact]
    public void BuildConnectUser_WritesExpectedHeaderAndEncryptedPayload()
    {
        ushort sequence = 0;

        var packet = A9Vue990PpcsControlCommandBuilder.BuildConnectUser(
            ref sequence,
            [0, 0, 0, 0],
            "admin",
            "888888");

        packet.Should().HaveCount(180);
        packet[..20].Should().Equal(
            0xf1, 0xd0, 0x00, 0xb0,
            0xd1, 0x00, 0x00, 0x00,
            0x11, 0x0a, 0x20, 0x10,
            0xa4, 0x00, 0xff, 0x00,
            0x00, 0x00, 0x00, 0x00);
        sequence.Should().Be(1);

        A9Vue990PpcsPacket.TryParse(packet, out var parsed).Should().BeTrue();
        parsed.TryReadDrw(out _, out _, out var drwPayload).Should().BeTrue();
        A9Vue990PpcsControlCommandBuilder.TryReadControlHeader(
                drwPayload.Span,
                out var command,
                out var payloadLength,
                out var destination)
            .Should().BeTrue();
        command.Should().Be(A9Vue990PpcsControlCommandBuilder.ConnectUser);
        payloadLength.Should().Be(164);
        destination.Should().Be(0xff00);
    }

    [Fact]
    public void XqBytes_RoundTripsPayload()
    {
        byte[] payload = [0x61, 0x64, 0x6d, 0x69, 0x6e, 0x00, 0x88, 0xff];
        var encoded = payload.ToArray();

        A9Vue990PpcsControlCommandBuilder.XqBytesEnc(encoded, 4);
        encoded.Should().NotEqual(payload);

        A9Vue990PpcsControlCommandBuilder.XqBytesDec(encoded, 4);
        encoded.Should().Equal(payload);
    }
}
