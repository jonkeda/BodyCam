using BodyCam.Services.Camera.A9.Vue990;
using FluentAssertions;

namespace BodyCam.Tests.Services.Camera.A9.Vue990;

public class A9Vue990P2pPacketBuilderTests
{
    [Fact]
    public void BuildHeader_WritesCommandAndLengthBigEndian()
    {
        var packet = A9Vue990P2pPacketBuilder.BuildHeader(0xf210, 0x0034);

        packet.Should().Equal(0xf2, 0x10, 0x00, 0x34);
    }

    [Fact]
    public void NativeEmptyPackets_MatchRecoveredHelpers()
    {
        A9Vue990P2pPacketBuilder.BuildTcpHello()
            .Should().Equal(0xf1, 0x00, 0x00, 0x00);

        A9Vue990P2pPacketBuilder.BuildRelayHello()
            .Should().Equal(0xf1, 0x70, 0x00, 0x00);

        A9Vue990P2pPacketBuilder.BuildServerRequest()
            .Should().Equal(0xf2, 0x10, 0x00, 0x00);
    }

    [Fact]
    public void BuildTcpSendHelloOracle_MatchesNativeLoopbackOracle()
    {
        A9Vue990P2pPacketBuilder.BuildTcpSendHelloOracle()
            .Should().Equal(0x00, 0x04, 0x68, 0x00, 0x67, 0xc6, 0xfe, 0x15, 0x8f, 0x32, 0xc2, 0x84);
    }

    [Fact]
    public void BuildTcpSendSecondStageOracles_MatchNativeLoopbackOracle()
    {
        A9Vue990P2pPacketBuilder.BuildTcpSendRlyReqOracle()
            .Should().HaveCount(64)
            .And.StartWith([0x00, 0x38, 0x68, 0x00]);

        A9Vue990P2pPacketBuilder.BuildTcpSendRsLgnOracle()
            .Should().HaveCount(68)
            .And.StartWith([0x00, 0x3c, 0x68, 0x00]);
    }

    [Fact]
    public void BuildSequence_ConcatenatesPacketsInOrder()
    {
        var sequence = A9Vue990P2pPacketBuilder.BuildSequence(
            A9Vue990P2pPacketBuilder.BuildTcpHello(),
            A9Vue990P2pPacketBuilder.BuildServerRequest());

        sequence.Should().Equal(0xf1, 0x00, 0x00, 0x00, 0xf2, 0x10, 0x00, 0x00);
    }
}
