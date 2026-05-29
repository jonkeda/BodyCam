using System.Net;
using BodyCam.Services.Camera.A9.Vue990;
using FluentAssertions;

namespace BodyCam.Tests.Services.Camera.A9.Vue990;

public sealed class A9Vue990ConnectByServerStateTests
{
    private const string CurrentCameraServer =
        "DAS-8ED76A3380D998ECDA94D6D805A36877C3D92D7F487DDF241063960174721BD6B3095F18FCFF7A65C4FD562D0707E31D254384C6093C03919E7C1CC11A26912D457B9DE4A9A01BBB4A2092EB392929F1A179B880B893019B7627C43F90D4760B";

    [Fact]
    public void TryCreate_PreservesCurrentCameraConnectState()
    {
        var state = BuildCurrentCameraState();

        state.ConnectType.Should().Be(0x3f);
        state.P2pType.Should().Be(1);
        state.ClientId.Should().Be("BKGD00000100FMQLN");
        state.Vuid.Should().Be("BK0025644WBPD");
        state.User.Should().Be("admin");
        state.Password.Should().Be("888888");
        state.LocalEndpoint.Should().Be(new IPEndPoint(IPAddress.Parse("192.168.168.101"), 65529));

        state.Tokens.Should().HaveCount(5);
        state.OpaqueToken.Hex.Should().Be("35334241483035302D1311722F3D4B3D3030303131");
        state.OpaqueToken.Bytes[9].Should().Be(0x13);
        state.OpaqueToken.Bytes[10].Should().Be(0x11);
        state.ModeToken.EscapedAscii.Should().Be("a+a+a");
        state.ModeParts.Should().Equal("a", "a", "a");
        state.RelayHostToken.EscapedAscii.Should()
            .Be("47.98.128.117-120.78.3.33-47.109.80.221");
        state.RelayHosts.Should().Equal(
            "47.98.128.117",
            "120.78.3.33",
            "47.109.80.221");
        state.RelayNameToken.EscapedAscii.Should().Be("BKGD");
        state.Selector.Should().Be("9047F8F88");
        state.CandidateDasConnectText.Should()
            .Be("das,1,a+a+a,47.98.128.117-120.78.3.33-47.109.80.221,BKGD");
    }

    [Fact]
    public void TryCreate_BuildsNativeStructuredP2pIds()
    {
        var state = BuildCurrentCameraState();

        state.VuidP2pId.Should().Equal(
            0x42, 0x4b, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x64, 0x2c,
            0x57, 0x42, 0x50, 0x44, 0x00, 0x00, 0x00, 0x00);
        state.ClientP2pId.Should().Equal(
            0x42, 0x4b, 0x47, 0x44, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x64,
            0x46, 0x4d, 0x51, 0x4c, 0x4e, 0x00, 0x00, 0x00);
    }

    [Fact]
    public void BuildPrimaryLanHoleOpenPackets_MatchesNativeHelperVectors()
    {
        var state = BuildCurrentCameraState();

        var packets = state.BuildPrimaryLanHoleOpenPackets();

        packets.Should().Contain(packet =>
            packet.Name == "vuid-list-request" &&
            Convert.ToHexString(packet.Bytes) ==
            "F1670014424B0000000000000000642C5742504400000000");
        packets.Should().Contain(packet =>
            packet.Name == "vuid-punch-packet" &&
            Convert.ToHexString(packet.Bytes) ==
            "F1410014424B0000000000000000642C5742504400000000");
        packets.Should().Contain(packet =>
            packet.Name == "vuid-p2p-ready" &&
            Convert.ToHexString(packet.Bytes) ==
            "F1420014424B0000000000000000642C5742504400000000");
        packets.Should().Contain(packet =>
            packet.Name == "vuid-p2p-request4" &&
            Convert.ToHexString(packet.Bytes) ==
            "F1200024424B0000000000000000642C57425044000000000002F9FF65A8A8C00000000000000000");
    }

    [Fact]
    public void BuildClientIdLanHoleOpenPackets_MatchesNativeHelperVectors()
    {
        var state = BuildCurrentCameraState();

        var packets = state.BuildClientIdLanHoleOpenPackets();

        packets.Should().Contain(packet =>
            packet.Name == "client-list-request" &&
            Convert.ToHexString(packet.Bytes) ==
            "F1670014424B47440000000000000064464D514C4E000000");
        packets.Should().Contain(packet =>
            packet.Name == "client-punch-packet" &&
            Convert.ToHexString(packet.Bytes) ==
            "F1410014424B47440000000000000064464D514C4E000000");
        packets.Should().Contain(packet =>
            packet.Name == "client-p2p-ready" &&
            Convert.ToHexString(packet.Bytes) ==
            "F1420014424B47440000000000000064464D514C4E000000");
        packets.Should().Contain(packet =>
            packet.Name == "client-p2p-request4" &&
            Convert.ToHexString(packet.Bytes) ==
            "F1200024424B47440000000000000064464D514C4E0000000002F9FF65A8A8C00000000000000000");
    }

    [Fact]
    public void BuildNativeClientSessionSetupPackets_MatchesMappedSessionSetupSubset()
    {
        var state = BuildCurrentCameraState();

        var packets = state.BuildNativeClientSessionSetupPackets();

        packets.Select(packet => packet.Name).Should().Equal(
            "native-client-list-request",
            "native-client-p2p-request4",
            "native-lan-search");
        Convert.ToHexString(packets[0].Bytes).Should()
            .Be("F1670014424B47440000000000000064464D514C4E000000");
        Convert.ToHexString(packets[1].Bytes).Should()
            .Be("F1200024424B47440000000000000064464D514C4E0000000002F9FF65A8A8C00000000000000000");
        Convert.ToHexString(packets[2].Bytes).Should().Be("F1300000");
    }

    [Fact]
    public void BuildNativeAlivePackets_MatchesNativeHeaderOnlyPackets()
    {
        var state = BuildCurrentCameraState();

        var packets = state.BuildNativeAlivePackets();

        packets.Select(packet => Convert.ToHexString(packet.Bytes))
            .Should().Equal("F1E00000", "F1E10000");
    }

    [Fact]
    public void TryCreate_RejectsInvalidLocalEndpoint()
    {
        A9Vue990DasServerParameter.TryParse(
            CurrentCameraServer,
            out var das,
            out var parseError).Should().BeTrue(parseError);

        var created = A9Vue990ConnectByServerState.TryCreate(
            das!,
            "BKGD00000100FMQLN",
            "BK0025644WBPD",
            new IPEndPoint(IPAddress.IPv6Loopback, 65529),
            out var state,
            out var error);

        created.Should().BeFalse();
        state.Should().BeNull();
        error.Should().Contain("IPv4");
    }

    private static A9Vue990ConnectByServerState BuildCurrentCameraState()
    {
        A9Vue990DasServerParameter.TryParse(
            CurrentCameraServer,
            out var das,
            out var parseError).Should().BeTrue(parseError);

        A9Vue990ConnectByServerState.TryCreate(
            das!,
            "BKGD00000100FMQLN",
            "BK0025644WBPD",
            new IPEndPoint(IPAddress.Parse("192.168.168.101"), 65529),
            out var state,
            out var error).Should().BeTrue(error);

        return state!;
    }
}
