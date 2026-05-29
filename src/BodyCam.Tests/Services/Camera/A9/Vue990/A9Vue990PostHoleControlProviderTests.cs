using BodyCam.Services.Camera.A9.Vue990;
using FluentAssertions;

namespace BodyCam.Tests.Services.Camera.A9.Vue990;

public sealed class A9Vue990PostHoleControlProviderTests
{
    [Fact]
    public void GetControls_ReturnsNamedScopedVectorsInNativePacedOrder()
    {
        var controls = A9Vue990PostHoleControlProvider.GetControls();

        controls.Should().HaveCount(4);
        controls.Select(control => control.Kind).Should().Equal(
            A9Vue990PostHoleControlKind.InitialShortRequest,
            A9Vue990PostHoleControlKind.InitialLongRequest,
            A9Vue990PostHoleControlKind.MediaShortRequest,
            A9Vue990PostHoleControlKind.MediaLongRequest);
        controls.Select(control => control.Length).Should().Equal(30, 110, 30, 126);
        controls.Should().OnlyContain(control => control.Name.Length > 0);
        controls.Should().OnlyContain(control => control.ObservedRole.Length > 0);
        controls.Should().OnlyContain(control => control.ExpectedResponse.Length > 0);
    }

    [Fact]
    public void GetControl_ReturnsDefensivePacketCopies()
    {
        var control = A9Vue990PostHoleControlProvider.GetControl(A9Vue990PostHoleControlKind.InitialShortRequest);

        var first = control.Bytes;
        first[0] = 0xff;

        control.Bytes[0].Should().Be(A9Vue990Hlp2pDirectPacket.DirectPacketType);
        control.ToArray()[0].Should().Be(A9Vue990Hlp2pDirectPacket.DirectPacketType);
    }

    [Theory]
    [InlineData(A9Vue990PostHoleControlKind.InitialShortRequest, 0, 0x0000, 0x0019)]
    [InlineData(A9Vue990PostHoleControlKind.InitialLongRequest, 1, 0x0001, 0x0069)]
    [InlineData(A9Vue990PostHoleControlKind.MediaShortRequest, 2, 0x0002, 0x0019)]
    [InlineData(A9Vue990PostHoleControlKind.MediaLongRequest, 3, 0x0003, 0x0079)]
    public void ScopedVectors_AreValidDirectCommandPackets(
        A9Vue990PostHoleControlKind kind,
        int expectedSequence,
        int expectedMessageId,
        int expectedTailLength)
    {
        var control = A9Vue990PostHoleControlProvider.GetControl(kind);

        A9Vue990Hlp2pDirectPacket.TryParseDirectDataPacket(control.Bytes, out var packet)
            .Should().BeTrue();

        ((int)packet.Sequence).Should().Be(expectedSequence);
        packet.Operation.Should().Be(A9Vue990Hlp2pDirectPacket.DirectCommandOperation);
        ((int)packet.MessageId).Should().Be(expectedMessageId);
        ((int)packet.TailLength).Should().Be(expectedTailLength);
        packet.FragmentIndex.Should().Be(0);
        packet.Kind.Should().Be(1);
        packet.Channel.Should().Be(1);
    }
}
