using System.Text.Json;
using BodyCam.Models;
using FluentAssertions;

namespace BodyCam.Tests.Models;

public sealed class DeviceSettingsTests
{
    [Fact]
    public void DefaultDeviceSettings_HasExpectedDefaults()
    {
        var ds = new DeviceSettings();

        ds.ActiveProfileId.Should().Be("phone");
        ds.Custom.Should().NotBeNull();
        ds.Active.Should().NotBeNull();
        ds.KnownDevices.Should().BeEmpty();
        ds.ProfileSettings.Should().BeEmpty();
    }

    [Fact]
    public void JsonRoundTrip_PreservesAllFields()
    {
        var original = new DeviceSettings
        {
            ActiveProfileId = "heycyan-glasses",
            Custom = new CustomSelection
            {
                CameraProviderId = "phone",
                AudioInputProviderId = "platform",
                AudioOutputProviderId = "windows-speaker",
            },
            Active = new ActiveProviders
            {
                CameraProviderId = "heycyan-glasses",
                AudioInputProviderId = "heycyan-glasses",
                AudioOutputProviderId = "heycyan-glasses",
            },
            KnownDevices =
            [
                new KnownDevice
                {
                    DeviceId = "D8:79:B8:7F:E6:C9",
                    DisplayName = "HeyCyan Glasses",
                    DeviceType = "heycyan-glasses",
                    AutoReconnect = true,
                    LastConnected = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc),
                    Properties = new Dictionary<string, string> { ["fw"] = "1.2.3" },
                },
            ],
            ProfileSettings = new Dictionary<string, ProfileOverrides>
            {
                ["bluetooth"] = new ProfileOverrides { PreferredDeviceId = "AA:BB:CC:DD:EE:FF" },
            },
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<DeviceSettings>(json)!;

        deserialized.ActiveProfileId.Should().Be("heycyan-glasses");
        deserialized.Custom.CameraProviderId.Should().Be("phone");
        deserialized.Custom.AudioInputProviderId.Should().Be("platform");
        deserialized.Custom.AudioOutputProviderId.Should().Be("windows-speaker");
        deserialized.Active.CameraProviderId.Should().Be("heycyan-glasses");
        deserialized.Active.AudioInputProviderId.Should().Be("heycyan-glasses");
        deserialized.Active.AudioOutputProviderId.Should().Be("heycyan-glasses");
        deserialized.KnownDevices.Should().HaveCount(1);
        deserialized.KnownDevices[0].DeviceId.Should().Be("D8:79:B8:7F:E6:C9");
        deserialized.KnownDevices[0].DisplayName.Should().Be("HeyCyan Glasses");
        deserialized.KnownDevices[0].DeviceType.Should().Be("heycyan-glasses");
        deserialized.KnownDevices[0].AutoReconnect.Should().BeTrue();
        deserialized.KnownDevices[0].Properties.Should().ContainKey("fw");
        deserialized.ProfileSettings.Should().ContainKey("bluetooth");
        deserialized.ProfileSettings["bluetooth"].PreferredDeviceId.Should().Be("AA:BB:CC:DD:EE:FF");
    }

    [Fact]
    public void JsonRoundTrip_EmptyObject_DeserializesToDefaults()
    {
        var ds = JsonSerializer.Deserialize<DeviceSettings>("{}")!;

        ds.ActiveProfileId.Should().Be("phone");
        ds.Custom.Should().NotBeNull();
        ds.Active.Should().NotBeNull();
        ds.KnownDevices.Should().BeEmpty();
        ds.ProfileSettings.Should().BeEmpty();
    }

    [Fact]
    public void JsonRoundTrip_NullProviderIds_ArePreserved()
    {
        var original = new DeviceSettings
        {
            Custom = new CustomSelection
            {
                CameraProviderId = null,
                AudioInputProviderId = null,
                AudioOutputProviderId = null,
            },
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<DeviceSettings>(json)!;

        deserialized.Custom.CameraProviderId.Should().BeNull();
        deserialized.Custom.AudioInputProviderId.Should().BeNull();
        deserialized.Custom.AudioOutputProviderId.Should().BeNull();
    }

    [Fact]
    public void KnownDevice_DefaultAutoReconnect_IsTrue()
    {
        var device = new KnownDevice();
        device.AutoReconnect.Should().BeTrue();
    }

    [Fact]
    public void JsonRoundTrip_MultipleKnownDevices()
    {
        var original = new DeviceSettings
        {
            KnownDevices =
            [
                new KnownDevice { DeviceId = "device-1", DisplayName = "Glasses 1", DeviceType = "heycyan-glasses" },
                new KnownDevice { DeviceId = "device-2", DisplayName = "AirPods Pro", DeviceType = "bluetooth-headset" },
                new KnownDevice { DeviceId = "device-3", DisplayName = "Jabra Elite", DeviceType = "bluetooth-headset" },
            ],
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<DeviceSettings>(json)!;

        deserialized.KnownDevices.Should().HaveCount(3);
        deserialized.KnownDevices.Select(d => d.DeviceId).Should()
            .ContainInOrder("device-1", "device-2", "device-3");
    }
}
