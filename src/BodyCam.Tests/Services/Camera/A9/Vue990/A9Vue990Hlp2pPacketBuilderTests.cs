using System.Net;
using BodyCam.Services.Camera.A9.Vue990;
using FluentAssertions;

namespace BodyCam.Tests.Services.Camera.A9.Vue990;

public sealed class A9Vue990Hlp2pPacketBuilderTests
{
    [Fact]
    public void BuildLanSearch_MatchesNativeHeader()
    {
        A9Vue990Hlp2pPacketBuilder.BuildLanSearch()
            .Should().Equal(0xf1, 0x30, 0x00, 0x00);
    }

    [Fact]
    public void BuildCompactP2pId_WritesNativeStructuredId()
    {
        A9Vue990Hlp2pPacketBuilder.BuildCompactP2pId("BKGD00000100FMQLN")
            .Should().Equal(
                0x42, 0x4b, 0x47, 0x44, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x64,
                0x46, 0x4d, 0x51, 0x4c, 0x4e, 0x00, 0x00, 0x00);
    }

    [Fact]
    public void BuildDelimitedP2pId_WritesNativeStructuredId()
    {
        A9Vue990Hlp2pPacketBuilder.BuildDelimitedP2pId("BK-025644-WBPD")
            .Should().Equal(
                0x42, 0x4b, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x64, 0x2c,
                0x57, 0x42, 0x50, 0x44, 0x00, 0x00, 0x00, 0x00);
    }

    [Fact]
    public void BuildReverseAddress4_WritesNativeReversedEndpoint()
    {
        A9Vue990Hlp2pPacketBuilder.BuildReverseAddress4(IPAddress.Parse("192.168.168.101"), 65529)
            .Should().Equal(
                0x00, 0x02, 0xf9, 0xff,
                0x65, 0xa8, 0xa8, 0xc0,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00);
    }

    [Fact]
    public void BuildP2pRequest4_WritesHeaderIdAndReverseAddress()
    {
        var id = A9Vue990Hlp2pPacketBuilder.BuildCompactP2pId("BK0025644WBPD");

        A9Vue990Hlp2pPacketBuilder
            .BuildP2pRequest4(id, IPAddress.Parse("192.168.168.101"), 65529)
            .Should().Equal(
                0xf1, 0x20, 0x00, 0x24,
                0x42, 0x4b, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x64, 0x2c,
                0x57, 0x42, 0x50, 0x44, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x02, 0xf9, 0xff,
                0x65, 0xa8, 0xa8, 0xc0,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00);
    }

    [Fact]
    public void BuildP2pAlive_WritesNativeHeaderOnlyPackets()
    {
        A9Vue990Hlp2pPacketBuilder.BuildP2pAlive()
            .Should().Equal(0xf1, 0xe0, 0x00, 0x00);
        A9Vue990Hlp2pPacketBuilder.BuildP2pAliveAck()
            .Should().Equal(0xf1, 0xe1, 0x00, 0x00);
    }
}
