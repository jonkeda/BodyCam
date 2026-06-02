using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;
using System.Text;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

public sealed class HeyCyanDirectBleProtocolTests
{
    [Fact]
    public void BuildSessionPayload_maps_battery_frame_to_session_payload()
    {
        var frame = HeyCyanCommands.BuildFrame(HeyCyanCommands.ActionBattery, [87, 1]);

        var payload = HeyCyanDirectBleProtocol.BuildSessionPayload(frame);

        payload.Should().Equal(87, 1);
    }

    [Fact]
    public void BuildSessionPayload_maps_device_info_frame_to_comma_version_payload()
    {
        var firmware = Encoding.UTF8.GetBytes("FW2.3");
        var hardware = Encoding.UTF8.GetBytes("HW1.0");
        var wifiFirmware = Encoding.UTF8.GetBytes("WiFiFW2.0");
        var wifiHardware = Encoding.UTF8.GetBytes("WiFiHW1.1");
        var responsePayload = new List<byte>
        {
            0,
            (byte)firmware.Length, 0,
            (byte)hardware.Length, 0,
            (byte)wifiFirmware.Length, 0,
            (byte)wifiHardware.Length, 0
        };
        responsePayload.AddRange(firmware);
        responsePayload.AddRange(hardware);
        responsePayload.AddRange(wifiFirmware);
        responsePayload.AddRange(wifiHardware);
        var frame = HeyCyanCommands.BuildFrame(HeyCyanCommands.ActionDeviceInfo, responsePayload.ToArray());

        var payload = HeyCyanDirectBleProtocol.BuildSessionPayload(frame);
        var version = HeyCyanFrameParser.ParseVersion(payload);

        version.Firmware.Should().Be("FW2.3");
        version.Hardware.Should().Be("HW1.0");
        version.WifiFirmware.Should().Be("WiFiFW2.0");
        version.WifiHardware.Should().Be("WiFiHW1.1");
    }

    [Fact]
    public void BuildSessionPayload_maps_media_count_frame_to_legacy_count_payload()
    {
        var frame = HeyCyanCommands.BuildFrame(
            HeyCyanCommands.ActionGlassesControl,
            [0, 4, 5, 0, 6, 0, 7, 0, 0]);

        var payload = HeyCyanDirectBleProtocol.BuildSessionPayload(frame);
        var counts = HeyCyanFrameParser.ParseMediaCounts(payload);

        counts.Photos.Should().Be(5);
        counts.Videos.Should().Be(6);
        counts.AudioFiles.Should().Be(7);
    }

    [Fact]
    public void TryBuildRawNotify_maps_control_ip_response_to_transfer_ip_notify()
    {
        var frame = HeyCyanCommands.BuildFrame(
            HeyCyanCommands.ActionGlassesControl,
            [0, 3, 0, 0, 49, 183]);

        var result = HeyCyanDirectBleProtocol.TryBuildRawNotify(frame, out var loadData);

        result.Should().BeTrue();
        loadData.Should().HaveCount(11);
        loadData[6].Should().Be(0x08);
        loadData.Skip(7).Should().Equal(192, 168, 49, 183);
    }
}
