using BodyCam.Services.Camera.Usb;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using BodyCam.ViewModels.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BodyCam.Tests.ViewModels.Settings;

public sealed class UsbCameraSettingsViewModelTests
{
    [Fact]
    public void Constructor_LoadsSettingsOrDefaultMatch()
    {
        var vm = CreateVm(new FakeSettingsService());

        vm.Title.Should().Be("USB Camera");
        vm.DeviceMatch.Should().Be(UsbCameraProvider.DefaultDeviceMatch);
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public void Constructor_LoadsSavedMatch()
    {
        var vm = CreateVm(new FakeSettingsService { UsbCameraDeviceMatch = "HD camera" });

        vm.DeviceMatch.Should().Be("HD camera");
    }

    [Fact]
    public async Task SaveAsync_PersistsTrimmedMatch()
    {
        var settings = new FakeSettingsService();
        var vm = CreateVm(settings);
        vm.DeviceMatch = " HD camera ";

        await vm.SaveAsync();

        settings.UsbCameraDeviceMatch.Should().Be("HD camera");
        vm.Status.Should().Be("Saved");
    }

    [Fact]
    public async Task TestCaptureAsync_WithBlankMatch_ShowsValidationStatus()
    {
        var called = false;
        var vm = CreateVm(new FakeSettingsService(), (_, _) =>
        {
            called = true;
            return Task.FromResult("ok");
        });
        vm.DeviceMatch = "";

        await vm.TestCaptureAsync();

        called.Should().BeFalse();
        vm.Status.Should().Be("Enter a device name or VID/PID.");
    }

    [Fact]
    public async Task TestCaptureAsync_WhenTesterSucceeds_PersistsAndReportsSuccess()
    {
        var settings = new FakeSettingsService();
        UsbCameraConnectionSettings? tested = null;
        var vm = CreateVm(settings, (connectionSettings, _) =>
        {
            tested = connectionSettings;
            return Task.FromResult("HD camera, 77,538 bytes");
        });
        vm.DeviceMatch = "VID_349C&PID_0411";

        await vm.TestCaptureAsync();

        tested.Should().NotBeNull();
        tested!.DeviceMatch.Should().Be("VID_349C&PID_0411");
        settings.UsbCameraDeviceMatch.Should().Be("VID_349C&PID_0411");
        vm.Status.Should().Be("Capture test succeeded: HD camera, 77,538 bytes");
        vm.IsTesting.Should().BeFalse();
    }

    [Fact]
    public async Task TestCaptureAsync_WhenTesterFails_ReportsFailure()
    {
        var vm = CreateVm(new FakeSettingsService(), (_, _) =>
            throw new InvalidOperationException("camera unavailable"));

        await vm.TestCaptureAsync();

        vm.Status.Should().Be("Capture test failed: camera unavailable");
        vm.IsTesting.Should().BeFalse();
    }

    [Fact]
    public async Task DefaultTester_UsesUsbCameraClient()
    {
        var client = Substitute.For<IUsbCameraClient>();
        client.IsSupported.Returns(true);
        client.CaptureJpegAsync(Arg.Any<UsbCameraCaptureOptions>(), Arg.Any<CancellationToken>())
            .Returns(new UsbCameraCaptureResult(
                true,
                [0xff, 0xd8, 0xff, 0xd9],
                "USB\\VID_349C&PID_0411",
                "HD camera",
                null));

        var settings = new FakeSettingsService();
        var vm = new UsbCameraSettingsViewModel(
            settings,
            client,
            NullLogger<UsbCameraSettingsViewModel>.Instance);

        await vm.TestCaptureAsync();

        vm.Status.Should().Be("Capture test succeeded: HD camera, 4 bytes");
    }

    private static UsbCameraSettingsViewModel CreateVm(
        FakeSettingsService settings,
        Func<UsbCameraConnectionSettings, CancellationToken, Task<string>>? testCaptureAsync = null)
    {
        return new UsbCameraSettingsViewModel(
            settings,
            Substitute.For<IUsbCameraClient>(),
            NullLogger<UsbCameraSettingsViewModel>.Instance,
            testCaptureAsync);
    }
}

