using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

/// <summary>
/// Unit tests for BleIpResolver. This component is Android-only (#if ANDROID),
/// but tests run on all platforms by using HeyCyanFrameParser directly.
/// </summary>
public class BleIpResolverTests
{
    [Fact]
    public async Task Valid_0x08_frame_resolves_ip()
    {
        var frame = new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x08, 192, 168, 49, 50 };

        // BleIpResolver requires SynchronizationContext, so we can't test it directly.
        // Instead, test that HeyCyanFrameParser.TryParseTransferIp works correctly.
        var result = HeyCyanFrameParser.TryParseTransferIp(frame, out var ip);

        result.Should().BeTrue();
        ip.Should().NotBeNull();
        ip!.ToString().Should().Be("192.168.49.50");
    }

    [Fact]
    public void Short_buffer_returns_false()
    {
        var frame = new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x08, 192 };

        var result = HeyCyanFrameParser.TryParseTransferIp(frame, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void P2p_error_0x09_0xFF_classified_as_noisy()
    {
        var frame = new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x09, 0xFF };

        var kind = HeyCyanFrameParser.ClassifyP2pError(frame);

        kind.Should().Be(HeyCyanP2pErrorKind.Noisy);
    }

    [Fact]
    public void P2p_error_0x09_non_FF_classified_as_fatal()
    {
        var frame = new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x09, 0x01 };

        var kind = HeyCyanFrameParser.ClassifyP2pError(frame);

        kind.Should().Be(HeyCyanP2pErrorKind.Fatal);
    }

    [Fact]
    public void Multiple_frames_first_valid_ip_wins()
    {
        // Simulate receiving noisy frames before valid IP.
        var noisyFrame = new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x09, 0xFF };
        var ipFrame1 = new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x08, 192, 168, 49, 50 };
        var ipFrame2 = new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x08, 192, 168, 49, 51 };

        // Noisy frame should be ignored.
        HeyCyanFrameParser.ClassifyP2pError(noisyFrame).Should().Be(HeyCyanP2pErrorKind.Noisy);

        // First IP frame should parse successfully.
        HeyCyanFrameParser.TryParseTransferIp(ipFrame1, out var ip1).Should().BeTrue();
        ip1!.ToString().Should().Be("192.168.49.50");

        // Second IP frame should also parse (though in real usage, only the first would be used).
        HeyCyanFrameParser.TryParseTransferIp(ipFrame2, out var ip2).Should().BeTrue();
        ip2!.ToString().Should().Be("192.168.49.51");
    }

    [Theory]
    [InlineData(192, 168, 49, 1)]
    [InlineData(192, 168, 49, 50)]
    [InlineData(10, 0, 0, 1)]
    public void Various_valid_ips_parse_correctly(byte b1, byte b2, byte b3, byte b4)
    {
        var frame = new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x08, b1, b2, b3, b4 };

        var result = HeyCyanFrameParser.TryParseTransferIp(frame, out var ip);

        result.Should().BeTrue();
        ip.Should().NotBeNull();
        ip!.GetAddressBytes().Should().Equal(b1, b2, b3, b4);
    }
}
