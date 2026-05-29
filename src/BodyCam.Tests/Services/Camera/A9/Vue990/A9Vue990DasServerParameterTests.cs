using BodyCam.Services.Camera.A9.Vue990;
using FluentAssertions;

namespace BodyCam.Tests.Services.Camera.A9.Vue990;

public class A9Vue990DasServerParameterTests
{
    private const string CurrentCameraServer =
        "DAS-8ED76A3380D998ECDA94D6D805A36877C3D92D7F487DDF241063960174721BD6B3095F18FCFF7A65C4FD562D0707E31D254384C6093C03919E7C1CC11A26912D457B9DE4A9A01BBB4A2092EB392929F1A179B880B893019B7627C43F90D4760B";

    [Fact]
    public void TryParse_ParsesCurrentCameraDasShape()
    {
        var parsed = A9Vue990DasServerParameter.TryParse(
            CurrentCameraServer,
            out var parameter,
            out var error);

        parsed.Should().BeTrue(error);
        parameter.Should().NotBeNull();
        parameter!.HexPayload.Should().HaveLength(192);
        parameter.ByteLength.Should().Be(96);
        parameter.IsKnownLength.Should().BeTrue();
        parameter.HasKnownMagic.Should().BeTrue();
        parameter.MagicHex.Should().Be(A9Vue990DasServerParameter.KnownMagicHex);
        parameter.LooksPlainText.Should().BeFalse();
        parameter.PrintableAsciiPreview.Should().NotBeNullOrWhiteSpace();
        parameter.HasDecodedPayload.Should().BeTrue();
        parameter.DecodedPayload.KeyAscii.Should().Be("E2A482A389696467");
        parameter.DecodedPayload.IvAscii.Should().Be("B79ABF5C5F224495");
        parameter.DecodedPayload.PrintableAscii.Should()
            .StartWith(@"53BAH050-\x13\x11r/=K=00011,a+a+a,47.98.128.117-120.78.3.33-47.109.80.221,BKGD,9047F8F88");
        parameter.DecodedPayload.Tokens.Should().ContainInOrder(
            @"a+a+a",
            @"47.98.128.117-120.78.3.33-47.109.80.221",
            "BKGD",
            "9047F8F88");
        parameter.DecodedPayload.ConnectDescriptor.Should().NotBeNull();
        parameter.DecodedPayload.ConnectDescriptor!.HasNativeConnectByServerShape.Should().BeTrue();
        parameter.DecodedPayload.ConnectDescriptor.Tokens.Should().HaveCount(5);
        parameter.DecodedPayload.ConnectDescriptor.OpaqueToken!.Hex.Should()
            .Be("35334241483035302D1311722F3D4B3D3030303131");
        parameter.DecodedPayload.ConnectDescriptor.OpaqueToken.Bytes[9].Should().Be(0x13);
        parameter.DecodedPayload.ConnectDescriptor.OpaqueToken.Bytes[10].Should().Be(0x11);
        parameter.DecodedPayload.ConnectDescriptor.ModeParts.Should().Equal("a", "a", "a");
        parameter.DecodedPayload.ConnectDescriptor.RelayHosts.Should().Equal(
            "47.98.128.117",
            "120.78.3.33",
            "47.109.80.221");
        parameter.DecodedPayload.ConnectDescriptor.RelayName.Should().Be("BKGD");
        parameter.DecodedPayload.ConnectDescriptor.Selector.Should().Be("9047F8F88");
        parameter.DecodedPayload.RelayHosts.Should().Equal(
            "47.98.128.117",
            "120.78.3.33",
            "47.109.80.221");
    }

    [Fact]
    public void EncodeDecodedPayload_RoundTripsCurrentCameraPayload()
    {
        A9Vue990DasServerParameter.TryParse(
            CurrentCameraServer,
            out var parameter,
            out var error).Should().BeTrue(error);

        var encoded = A9Vue990DasServerParameter.EncodeDecodedPayload(
            parameter!.DecodedPayload.PlainBytes);

        encoded.Should().Be(CurrentCameraServer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("XYZ-8ED7")]
    [InlineData("DAS-8ED")]
    [InlineData("DAS-8EDZ")]
    public void TryParse_RejectsInvalidDasValues(string value)
    {
        var parsed = A9Vue990DasServerParameter.TryParse(
            value,
            out var parameter,
            out var error);

        parsed.Should().BeFalse();
        parameter.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }
}
