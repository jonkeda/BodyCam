using System.Net;
using BodyCam.Services.Camera.A9.Vue990;
using FluentAssertions;

namespace BodyCam.Tests.Services.Camera.A9.Vue990;

public class A9Vue990TcpRelayPacketBuilderTests
{
    private const string ClientId = "BKGD00000100FMQLN";
    private const string Vuid = "BK0025644WBPD";
    private const string RelayName = "BKGD";

    [Fact]
    public void BuildTcpRlyReqPlain_MatchesNativeStackLayout()
    {
        var address = A9Vue990TcpRelayPacketBuilder.BuildSockaddrCs2Network(
            IPAddress.Parse("127.0.0.1"),
            65527);

        var plain = A9Vue990TcpRelayPacketBuilder.BuildTcpRlyReqPlain(Vuid, RelayName, address);

        plain.Should().Equal(Convert.FromHexString(
            "F1530034424B3030323536000000000D424B4744000000000002F7FF0100007F" +
            "000000000000000000000000000000000000000000000000"));
    }

    [Fact]
    public void BuildTcpRsLgnPlain_MatchesNativeStackLayout()
    {
        var address = A9Vue990TcpRelayPacketBuilder.BuildSockaddrCs2Network(
            IPAddress.Parse("127.0.0.1"),
            65527);

        var plain = A9Vue990TcpRelayPacketBuilder.BuildTcpRsLgnPlain(Vuid, RelayName, address);

        plain.Should().Equal(Convert.FromHexString(
            "F1500038424B3030323536000000000D424B474400000000000000000000000000000000" +
            "0002F7FF0100007F00000000000000000000000000000000"));
    }

    [Fact]
    public void BuildTcpRlyReq_MatchesStableNativeTcpSendVector()
    {
        var address = A9Vue990TcpRelayPacketBuilder.BuildSockaddrCs2Network(
            IPAddress.Parse("127.0.0.1"),
            65527);

        var packet = A9Vue990TcpRelayPacketBuilder.BuildTcpRlyReq(
            ClientId,
            Vuid,
            RelayName,
            address,
            seed: [0x67, 0xc6]);

        packet.Should().Equal(Convert.FromHexString(
            "0038680067C66A808F614AF5D88B17996C556454C0301CFDBBB8233FBA0D94576" +
            "E80BD55712FAB0532EA5DDFF1505C5B7CED8919AE6F98F9BCB401275D252AD2"));
    }

    [Fact]
    public void BuildTcpRsLgn_MatchesStableNativeTcpSendVector()
    {
        var address = A9Vue990TcpRelayPacketBuilder.BuildSockaddrCs2Network(
            IPAddress.Parse("127.0.0.1"),
            65527);

        var packet = A9Vue990TcpRelayPacketBuilder.BuildTcpRsLgn(
            ClientId,
            Vuid,
            RelayName,
            address,
            seed: [0x67, 0xc6]);

        packet.Should().Equal(Convert.FromHexString(
            "003C680067C6B05B8F624AC5C00EAE99B83C2568E834BFA164D8D93A9F799347" +
            "FCC40BD1F9EDE3021406E467E1734A2926E730DF1C9E0DB579B57CED8919AE6F98F9BCB4"));
    }

    [Fact]
    public void CalculateTcpRelayCrc_MatchesNativeVector()
    {
        var encryptedPayload = Convert.FromHexString(
            "8F614AF5D88B17996C556454C0301CFDBBB8233FBA0D94576E80BD55712FAB053" +
            "2EA5DDFF1505C5B7CED8919AE6F98F9BCB401275D252AD2");

        A9Vue990TcpRelayPacketBuilder.CalculateTcpRelayCrc(encryptedPayload)
            .Should().Equal(0x6a, 0x80);
    }
}
