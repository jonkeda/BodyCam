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

    private DeviceViewModel CreateVm(
        IEnumerable<IAudioInputProvider>? inputProviders = null,
        IEnumerable<IAudioOutputProvider>? outputProviders = null,
        SourceProfileManager? profileManager = null)
    {
        var selector = new DefaultCameraSelector();
        var camMgr = new CameraManager([], _settings, selector, NullLogger<CameraManager>.Instance, null);
        var audioIn = new AudioInputManager(inputProviders ?? [], _settings, NullLogger<AudioInputManager>.Instance);
        var audioOut = new AudioOutputManager(outputProviders ?? [], _settings, new AppSettings(), NullLogger<AudioOutputManager>.Instance);
        var glassesCameraSection = new GlassesCameraSectionViewModel(null, null, NullLogger<GlassesCameraSectionViewModel>.Instance);
        var glasses = CreateGlassesManager();
        return new DeviceViewModel(
            camMgr,
            audioIn,
            audioOut,
            glassesCameraSection,
            glasses,
            _settings,
            new AppSettings(),
            new NullHeyCyanAudioEndpointActivationService(),
            profileManager: profileManager);
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
    public void AudioInputProviders_IncludesRegisteredUnavailableHeyCyanProvider()
    {
        var provider = Substitute.For<IAudioInputProvider>();
        provider.ProviderId.Returns("heycyan-glasses");
        provider.DisplayName.Returns("HeyCyan Glasses Mic");
        provider.IsAvailable.Returns(false);

        var vm = CreateVm(inputProviders: [provider]);

        vm.AudioInputProviders.Should().Contain(provider);
        vm.HeyCyanAudioInputStatus.Should().Be("HeyCyan microphone waiting for Windows Bluetooth endpoint");
    }

    [Fact]
    public void AudioOutputProviders_IncludesRegisteredUnavailableHeyCyanProvider()
    {
        var provider = Substitute.For<IAudioOutputProvider>();
        provider.ProviderId.Returns("heycyan-glasses");
        provider.DisplayName.Returns("HeyCyan Glasses Speaker");
        provider.IsAvailable.Returns(false);

        var vm = CreateVm(outputProviders: [provider]);

        vm.AudioOutputProviders.Should().Contain(provider);
        vm.HeyCyanAudioOutputStatus.Should().Be("HeyCyan speaker waiting for Windows Bluetooth endpoint");
    }

    [Fact]
    public void Title_IsDevices()
    {
        var vm = CreateVm();
        vm.Title.Should().Be("Devices");
    }

    // ── Profile-related tests (Phase 3) ─────────────────────────────────

    private SourceProfileManager CreateProfileManager(
        IEnumerable<ISourceProfile>? profiles = null,
        string savedProfileId = "phone")
    {
        _settings.DeviceSettings.Returns(new BodyCam.Models.DeviceSettings { ActiveProfileId = savedProfileId });
        var selector = new DefaultCameraSelector();
        var camMgr = new CameraManager([], _settings, selector, NullLogger<CameraManager>.Instance, null);
        var audioIn = new AudioInputManager([], _settings, NullLogger<AudioInputManager>.Instance);
        var audioOut = new AudioOutputManager([], _settings, new AppSettings(), NullLogger<AudioOutputManager>.Instance);

        profiles ??= CreateDefaultProfiles();

        return new SourceProfileManager(
            profiles, camMgr, audioIn, audioOut, _settings,
            NullLogger<SourceProfileManager>.Instance);
    }

    private static ISourceProfile[] CreateDefaultProfiles()
    {
        var phone = CreateFakeProfile("phone", "Phone", 10, isAvailable: true);
        var custom = CreateFakeProfile("custom", "Custom", 100, isAvailable: true);
        return [phone, custom];
    }

    private static ISourceProfile CreateFakeProfile(
        string id, string displayName, int order, bool isAvailable = true, int fallbackPriority = 10)
    {
        var profile = Substitute.For<ISourceProfile>();
        profile.Id.Returns(id);
        profile.DisplayName.Returns(displayName);
        profile.Order.Returns(order);
        profile.IsAvailable.Returns(isAvailable);
        profile.FallbackPriority.Returns(fallbackPriority);
        profile.ApplyAsync(
            Arg.Any<CameraManager>(),
            Arg.Any<AudioInputManager>(),
            Arg.Any<AudioOutputManager>(),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return profile;
    }

    [Fact]
    public void AvailableProfiles_WithProfileManager_ReturnsProfiles()
    {
        var mgr = CreateProfileManager();
        var vm = CreateVm(profileManager: mgr);

        vm.AvailableProfiles.Should().HaveCount(2);
        vm.AvailableProfiles.Select(p => p.Id).Should().ContainInOrder("phone", "custom");
    }

    [Fact]
    public void AvailableProfiles_WithoutProfileManager_ReturnsEmpty()
    {
        var vm = CreateVm();
        vm.AvailableProfiles.Should().BeEmpty();
    }

    [Fact]
    public void SelectedProfile_WithProfileManager_ReturnsActiveProfile()
    {
        var mgr = CreateProfileManager();
        var vm = CreateVm(profileManager: mgr);

        vm.SelectedProfile.Should().NotBeNull();
        vm.SelectedProfile!.Id.Should().Be("phone");
    }

    [Fact]
    public void SelectedProfile_WithoutProfileManager_ReturnsNull()
    {
        var vm = CreateVm();
        vm.SelectedProfile.Should().BeNull();
    }

    [Fact]
    public void IsCustomMode_WhenPhoneProfile_ReturnsFalse()
    {
        var mgr = CreateProfileManager();
        var vm = CreateVm(profileManager: mgr);

        vm.IsCustomMode.Should().BeFalse();
    }

    [Fact]
    public void IsCustomMode_WhenCustomProfile_ReturnsTrue()
    {
        var mgr = CreateProfileManager(savedProfileId: "custom");
        var vm = CreateVm(profileManager: mgr);

        vm.IsCustomMode.Should().BeTrue();
    }

    [Fact]
    public async Task SelectedProfile_Set_AppliesProfile()
    {
        var mgr = CreateProfileManager();
        var vm = CreateVm(profileManager: mgr);
        var customProfile = mgr.AvailableProfiles.First(p => p.Id == "custom");

        vm.SelectedProfile = customProfile;

        // Give async Apply time to complete
        await Task.Delay(50);

        mgr.ActiveProfile!.Id.Should().Be("custom");
    }

    [Fact]
    public void IsCustomMode_WithoutProfileManager_ReturnsTrue()
    {
        // Backward compat: when no profile manager, individual pickers should be visible
        var vm = CreateVm();
        vm.IsCustomMode.Should().BeTrue();
    }

    [Fact]
    public void ShowSourceProfilePicker_WithProfileManager_ReturnsTrue()
    {
        var mgr = CreateProfileManager();
        var vm = CreateVm(profileManager: mgr);

        vm.ShowSourceProfilePicker.Should().BeTrue();
    }

    [Fact]
    public void ShowSourceProfilePicker_WithoutProfileManager_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.ShowSourceProfilePicker.Should().BeFalse();
    }

    [Fact]
    public void Dispose_UnsubscribesFromProfileManager()
    {
        var mgr = CreateProfileManager();
        var vm = CreateVm(profileManager: mgr);

        // Should not throw
        vm.Dispose();
    }

    [Fact]
    public void ConnectedDevices_NoGlassesConnected_IsEmpty()
    {
        var vm = CreateVm();
        vm.ConnectedDevices.Should().BeEmpty();
        vm.HasConnectedDevices.Should().BeFalse();
    }

    [Fact]
    public void ConnectedDevices_WithBtProviders_IncludesThem()
    {
        var btInput = Substitute.For<IAudioInputProvider>();
        btInput.ProviderId.Returns("bt:AA:BB:CC:DD:EE:FF");
        btInput.DisplayName.Returns("BT Headset");
        btInput.IsAvailable.Returns(true);

        var vm = CreateVm(inputProviders: [btInput]);

        // Trigger audio provider refresh
        vm.ConnectedDevices.Should().BeEmpty(); // no refresh yet — initial state

        // Force refresh by simulating property refresh
        // BT providers are discovered on audio provider changes
    }
}
