using BodyCam.Services.Camera.A9.Vue990;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using BodyCam.ViewModels.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.ViewModels.Settings;

public sealed class Vue990CameraSettingsViewModelTests
{
    [Fact]
    public void Constructor_LoadsSettingsOrDefaultHost()
    {
        var vm = CreateVm(new FakeSettingsService());

        vm.Title.Should().Be("Vue990 Camera");
        vm.CameraHost.Should().Be(Vue990CameraProvider.DefaultHost);
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public void Constructor_LoadsSavedHost()
    {
        var vm = CreateVm(new FakeSettingsService { Vue990CameraIp = "10.0.0.5" });

        vm.CameraHost.Should().Be("10.0.0.5");
    }

    [Fact]
    public async Task SaveAsync_PersistsTrimmedHost()
    {
        var settings = new FakeSettingsService();
        var vm = CreateVm(settings);
        vm.CameraHost = " 192.168.168.1 ";

        await vm.SaveAsync();

        settings.Vue990CameraIp.Should().Be("192.168.168.1");
        vm.Status.Should().Be("Saved");
    }

    [Fact]
    public async Task TestConnectionAsync_WithBlankHost_ShowsValidationStatus()
    {
        var called = false;
        var vm = CreateVm(new FakeSettingsService(), (_, _) =>
        {
            called = true;
            return Task.FromResult("ok");
        });
        vm.CameraHost = "";

        await vm.TestConnectionAsync();

        called.Should().BeFalse();
        vm.Status.Should().Be("Enter a host.");
    }

    [Fact]
    public async Task TestConnectionAsync_WhenTesterSucceeds_PersistsAndReportsSuccess()
    {
        var settings = new FakeSettingsService();
        Vue990CameraConnectionSettings? tested = null;
        var vm = CreateVm(settings, (connectionSettings, _) =>
        {
            tested = connectionSettings;
            return Task.FromResult("BK7252N BK0025644WBPD");
        });
        vm.CameraHost = "192.168.168.1";

        await vm.TestConnectionAsync();

        tested.Should().NotBeNull();
        tested!.Host.Should().Be("192.168.168.1");
        settings.Vue990CameraIp.Should().Be("192.168.168.1");
        vm.Status.Should().Be("Connection test succeeded: BK7252N BK0025644WBPD");
        vm.IsTesting.Should().BeFalse();
    }

    [Fact]
    public async Task TestConnectionAsync_WhenTesterFails_ReportsFailure()
    {
        var vm = CreateVm(new FakeSettingsService(), (_, _) =>
            throw new InvalidOperationException("camera unavailable"));

        await vm.TestConnectionAsync();

        vm.Status.Should().Be("Connection test failed: camera unavailable");
        vm.IsTesting.Should().BeFalse();
    }

    private static Vue990CameraSettingsViewModel CreateVm(
        FakeSettingsService settings,
        Func<Vue990CameraConnectionSettings, CancellationToken, Task<string>>? testConnectionAsync = null)
    {
        return new Vue990CameraSettingsViewModel(
            settings,
            NullLogger<Vue990CameraSettingsViewModel>.Instance,
            testConnectionAsync);
    }
}
