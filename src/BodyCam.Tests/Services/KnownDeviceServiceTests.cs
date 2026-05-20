using BodyCam.Models;
using BodyCam.Services;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Services;

public class KnownDeviceServiceTests
{
    private readonly ISettingsService _settings;
    private readonly KnownDeviceService _sut;
    private DeviceSettings _deviceSettings = new();

    public KnownDeviceServiceTests()
    {
        _settings = Substitute.For<ISettingsService>();
        _settings.DeviceSettings.Returns(_ => _deviceSettings);
        _settings.When(s => s.DeviceSettings = Arg.Any<DeviceSettings>())
            .Do(ci => _deviceSettings = ci.Arg<DeviceSettings>());
        _sut = new KnownDeviceService(_settings);
    }

    [Fact]
    public void Devices_Empty_ReturnsEmpty()
    {
        _sut.Devices.Should().BeEmpty();
    }

    [Fact]
    public void AddOrUpdate_NewDevice_AddsToList()
    {
        _sut.AddOrUpdate("AA:BB:CC:DD:EE:FF", "HeyCyan Glasses", "heycyan-glasses");

        _sut.Devices.Should().ContainSingle()
            .Which.DeviceId.Should().Be("AA:BB:CC:DD:EE:FF");
    }

    [Fact]
    public void AddOrUpdate_ExistingDevice_UpdatesIt()
    {
        _sut.AddOrUpdate("AA:BB:CC:DD:EE:FF", "Old Name", "heycyan-glasses");
        _sut.AddOrUpdate("AA:BB:CC:DD:EE:FF", "New Name", "heycyan-glasses");

        _sut.Devices.Should().ContainSingle()
            .Which.DisplayName.Should().Be("New Name");
    }

    [Fact]
    public void AddOrUpdate_CaseInsensitiveId()
    {
        _sut.AddOrUpdate("aa:bb:cc:dd:ee:ff", "Device", "heycyan-glasses");
        _sut.AddOrUpdate("AA:BB:CC:DD:EE:FF", "Updated", "heycyan-glasses");

        _sut.Devices.Should().ContainSingle()
            .Which.DisplayName.Should().Be("Updated");
    }

    [Fact]
    public void AddOrUpdate_PersistsToSettings()
    {
        _sut.AddOrUpdate("device1", "Test Device", "bluetooth-audio");

        _settings.Received(1).DeviceSettings = Arg.Is<DeviceSettings>(
            ds => ds.KnownDevices.Count == 1);
    }

    [Fact]
    public void AddOrUpdate_RaisesDevicesChanged()
    {
        var raised = false;
        _sut.DevicesChanged += (_, _) => raised = true;

        _sut.AddOrUpdate("device1", "Test", "bluetooth-audio");

        raised.Should().BeTrue();
    }

    [Fact]
    public void AddOrUpdate_MergesProperties()
    {
        _sut.AddOrUpdate("device1", "Test", "glasses",
            new Dictionary<string, string> { ["firmware"] = "1.0" });

        _sut.AddOrUpdate("device1", "Test", "glasses",
            new Dictionary<string, string> { ["hardware"] = "2.0" });

        var device = _sut.Get("device1");
        device!.Properties.Should().ContainKeys("firmware", "hardware");
    }

    [Fact]
    public void Remove_ExistingDevice_ReturnsTrue()
    {
        _sut.AddOrUpdate("device1", "Test", "bluetooth-audio");

        _sut.Remove("device1").Should().BeTrue();
        _sut.Devices.Should().BeEmpty();
    }

    [Fact]
    public void Remove_NonExisting_ReturnsFalse()
    {
        _sut.Remove("nonexistent").Should().BeFalse();
    }

    [Fact]
    public void Remove_RaisesDevicesChanged()
    {
        _sut.AddOrUpdate("device1", "Test", "bluetooth-audio");
        var raised = false;
        _sut.DevicesChanged += (_, _) => raised = true;

        _sut.Remove("device1");

        raised.Should().BeTrue();
    }

    [Fact]
    public void Get_ExistingDevice_ReturnsIt()
    {
        _sut.AddOrUpdate("device1", "Test", "bluetooth-audio");

        _sut.Get("device1").Should().NotBeNull();
        _sut.Get("DEVICE1").Should().NotBeNull(); // case insensitive
    }

    [Fact]
    public void Get_NonExisting_ReturnsNull()
    {
        _sut.Get("nonexistent").Should().BeNull();
    }

    [Fact]
    public void AutoReconnectDevices_FiltersCorrectly()
    {
        _sut.AddOrUpdate("device1", "Auto", "bt");
        _sut.AddOrUpdate("device2", "Manual", "bt");
        _sut.SetAutoReconnect("device2", false);

        _sut.AutoReconnectDevices.Should().ContainSingle()
            .Which.DeviceId.Should().Be("device1");
    }

    [Fact]
    public void SetAutoReconnect_PersistsChange()
    {
        _sut.AddOrUpdate("device1", "Test", "bt");
        _sut.SetAutoReconnect("device1", false);

        _sut.Get("device1")!.AutoReconnect.Should().BeFalse();
    }

    [Fact]
    public void Devices_OrderedByLastConnected_MostRecentFirst()
    {
        _sut.AddOrUpdate("old-device", "Old", "bt");
        System.Threading.Thread.Sleep(50); // ensure different timestamp
        _sut.AddOrUpdate("new-device", "New", "bt");

        _sut.Devices[0].DeviceId.Should().Be("new-device");
        _sut.Devices[1].DeviceId.Should().Be("old-device");
    }

    [Fact]
    public void AddOrUpdate_SetsAutoReconnectTrue_ByDefault()
    {
        _sut.AddOrUpdate("device1", "Test", "bt");

        _sut.Get("device1")!.AutoReconnect.Should().BeTrue();
    }
}
