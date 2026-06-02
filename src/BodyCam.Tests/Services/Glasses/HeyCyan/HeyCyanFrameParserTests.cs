using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

public class HeyCyanFrameParserTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x02, 0x01 }, HeyCyanButtonGesture.Tap)]
    [InlineData(new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x03, 0x01 }, HeyCyanButtonGesture.DoubleTap)]
    public void Button_frames_decoded_correctly(byte[] frame, HeyCyanButtonGesture expected)
    {
        var result = HeyCyanFrameParser.TryParseButton(frame, out var gesture);

        result.Should().BeTrue();
        gesture.Should().Be(expected);
    }

    [Fact]
    public void Button_frame_with_wrong_type_returns_false()
    {
        var frame = new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x05, 0x50 }; // battery notify

        var result = HeyCyanFrameParser.TryParseButton(frame, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void Button_frame_too_short_returns_false()
    {
        var frame = new byte[] { 0xFF, 0, 0, 0, 0, 0 };

        var result = HeyCyanFrameParser.TryParseButton(frame, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void Ip_frame_loadData6_eq_08_extracts_ipv4_from_bytes_7_to_10()
    {
        var frame = new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x08, 192, 168, 49, 1 };

        var result = HeyCyanFrameParser.TryParseTransferIp(frame, out var ip);

        result.Should().BeTrue();
        ip.Should().NotBeNull();
        ip!.ToString().Should().Be("192.168.49.1");
    }

    [Fact]
    public void Ip_frame_with_wrong_type_returns_false()
    {
        var frame = new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x02, 192, 168, 49, 1 };

        var result = HeyCyanFrameParser.TryParseTransferIp(frame, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void Ip_frame_too_short_returns_false()
    {
        var frame = new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x08, 192, 168 };

        var result = HeyCyanFrameParser.TryParseTransferIp(frame, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void Normalize_transfer_ip_text_preserves_valid_p2p_ip()
    {
        var result = HeyCyanFrameParser.TryNormalizeTransferIpText("192.168.49.183", out var ip);

        result.Should().BeTrue();
        ip.Should().NotBeNull();
        ip!.ToString().Should().Be("192.168.49.183");
    }

    [Fact]
    public void Normalize_transfer_ip_text_repairs_vendor_shifted_p2p_ip()
    {
        var result = HeyCyanFrameParser.TryNormalizeTransferIpText("49.183.0.0", out var ip);

        result.Should().BeTrue();
        ip.Should().NotBeNull();
        ip!.ToString().Should().Be("192.168.49.183");
    }

    [Fact]
    public void Normalize_transfer_ip_text_rejects_invalid_text()
    {
        var result = HeyCyanFrameParser.TryNormalizeTransferIpText("not-an-ip", out var ip);

        result.Should().BeFalse();
        ip.Should().BeNull();
    }

    [Fact]
    public void P2p_error_frame_loadData6_eq_09_value_FF_is_classified_noisy()
    {
        var frame = new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x09, 0xFF };

        var kind = HeyCyanFrameParser.ClassifyP2pError(frame);

        kind.Should().Be(HeyCyanP2pErrorKind.Noisy);
    }

    [Fact]
    public void P2p_error_frame_loadData6_eq_09_value_01_is_classified_fatal()
    {
        var frame = new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x09, 0x01 };

        var kind = HeyCyanFrameParser.ClassifyP2pError(frame);

        kind.Should().Be(HeyCyanP2pErrorKind.Fatal);
    }

    [Fact]
    public void P2p_error_frame_with_wrong_type_returns_None()
    {
        var frame = new byte[] { 0xFF, 0, 0, 0, 0, 0, 0x08, 0xFF };

        var kind = HeyCyanFrameParser.ClassifyP2pError(frame);

        kind.Should().Be(HeyCyanP2pErrorKind.None);
    }

    [Fact]
    public void ParseBattery_extracts_percentage_and_charging_flag()
    {
        var payload = new byte[] { 75, 1 }; // 75%, charging

        var bat = HeyCyanFrameParser.ParseBattery(payload);

        bat.Percentage.Should().Be(75);
        bat.IsCharging.Should().BeTrue();
    }

    [Fact]
    public void ParseBattery_with_short_payload_returns_zero()
    {
        var payload = new byte[] { 75 };

        var bat = HeyCyanFrameParser.ParseBattery(payload);

        bat.Percentage.Should().Be(0);
        bat.IsCharging.Should().BeFalse();
    }

    [Fact]
    public void ParseMediaCounts_extracts_photos_videos_audio()
    {
        Span<byte> payload = stackalloc byte[12];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload[0..4], 10);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload[4..8], 5);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload[8..12], 3);

        var counts = HeyCyanFrameParser.ParseMediaCounts(payload.ToArray());

        counts.Photos.Should().Be(10);
        counts.Videos.Should().Be(5);
        counts.AudioFiles.Should().Be(3);
    }

    [Fact]
    public void ParseMediaCounts_with_short_payload_returns_zero()
    {
        var payload = new byte[8];

        var counts = HeyCyanFrameParser.ParseMediaCounts(payload);

        counts.Photos.Should().Be(0);
        counts.Videos.Should().Be(0);
        counts.AudioFiles.Should().Be(0);
    }

    [Fact]
    public void ParseVersion_with_comma_separated_values_parses_correctly()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(
            "HW1.0,FW2.3,WiFiHW1.1,WiFiFW2.0,AA:BB:CC:DD:EE:FF\0");

        var ver = HeyCyanFrameParser.ParseVersion(payload);

        ver.Hardware.Should().Be("HW1.0");
        ver.Firmware.Should().Be("FW2.3");
        ver.WifiHardware.Should().Be("WiFiHW1.1");
        ver.WifiFirmware.Should().Be("WiFiFW2.0");
        ver.MacAddress.Should().Be("AA:BB:CC:DD:EE:FF");
    }

    [Fact]
    public void ParseVersion_with_short_payload_returns_unknown()
    {
        var payload = new byte[10];

        var ver = HeyCyanFrameParser.ParseVersion(payload);

        ver.Hardware.Should().Be("unknown");
        ver.Firmware.Should().Be("unknown");
    }
}
