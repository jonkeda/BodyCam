using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using BodyCam.ViewModels.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BodyCam.Tests.ViewModels.Settings;

public class DeviceViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    private DeviceViewModel CreateVm()
    {
        var selector = new DefaultCameraSelector();
        var camMgr = new CameraManager([], _settings, selector, NullLogger<CameraManager>.Instance, null);
        var audioIn = new AudioInputManager([], _settings, NullLogger<AudioInputManager>.Instance);
        var audioOut = new AudioOutputManager([], _settings, new AppSettings(), NullLogger<AudioOutputManager>.Instance);
        var glassesCameraSection = new GlassesCameraSectionViewModel(null, null, NullLogger<GlassesCameraSectionViewModel>.Instance);
        var glasses = CreateGlassesManager();
        return new DeviceViewModel(camMgr, audioIn, audioOut, glassesCameraSection, glasses, _settings, new AppSettings());
    }

    private static HeyCyanGlassesDeviceManager CreateGlassesManager()
    {
        var session = new FakeHeyCyanSessionWithVersion();
        var fakeTransfer = new FakeHeyCyanMediaTransfer();
        var fakeBtInput = new FakeBluetoothAudioInputProvider(new[] { "AA:BB:CC:DD:EE:FF" });
        var fakeBtOutput = new FakeBluetoothAudioOutputProvider(new[] { "AA:BB:CC:DD:EE:FF" });
        var camera = new HeyCyanCameraProvider(session, fakeTransfer, NullLogger<HeyCyanCameraProvider>.Instance);
        var mic = new HeyCyanAudioInputProvider(session, fakeBtInput, NullLogger<HeyCyanAudioInputProvider>.Instance);
        var speaker = new HeyCyanAudioOutputProvider(session, fakeBtOutput, NullLogger<HeyCyanAudioOutputProvider>.Instance);
        var button = new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);
        return new HeyCyanGlassesDeviceManager(session, camera, mic, speaker, button, fakeTransfer,
            new FakeSettingsService(), NullLogger<HeyCyanGlassesDeviceManager>.Instance);
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
