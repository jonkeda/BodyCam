using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.ViewModels.Settings;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.ViewModels.Settings;

public class DeviceViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    private DeviceViewModel CreateVm()
    {
        var camMgr = new CameraManager([], _settings);
        var audioIn = new AudioInputManager([], _settings);
        var audioOut = new AudioOutputManager([], _settings, new AppSettings());
        return new DeviceViewModel(camMgr, audioIn, audioOut);
    }

    [Fact]
    public void CameraProviders_WithNoProviders_ReturnsEmpty()
    {
        var vm = CreateVm();
        vm.CameraProviders.Should().BeEmpty();
    }

    [Fact]
    public void AudioInputProviders_WithNoProviders_ReturnsEmpty()
    {
        var vm = CreateVm();
        vm.AudioInputProviders.Should().BeEmpty();
    }

    [Fact]
    public void AudioOutputProviders_WithNoProviders_ReturnsEmpty()
    {
        var vm = CreateVm();
        vm.AudioOutputProviders.Should().BeEmpty();
    }

    [Fact]
    public void Title_IsDevices()
    {
        var vm = CreateVm();
        vm.Title.Should().Be("Devices");
    }
}
