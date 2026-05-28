using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using BodyCam.ViewModels.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.ViewModels.Settings;

public class A9CameraSettingsViewModelTests
{
    [Fact]
    public void Constructor_LoadsSettingsAndDefaultsCredentials()
    {
        var settings = new FakeSettingsService
        {
            A9CameraIp = "192.168.1.1",
            A9CameraUid = "ABC123",
        };

        var vm = CreateVm(settings);

        vm.Title.Should().Be("A9 Camera");
        vm.A9CameraIp.Should().Be("192.168.1.1");
        vm.A9CameraUid.Should().Be("ABC123");
        vm.A9CameraUsername.Should().Be("admin");
        vm.A9CameraPassword.Should().Be("admin");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task SaveAsync_PersistsTrimmedSettings()
    {
        var settings = new FakeSettingsService();
        var vm = CreateVm(settings);
        vm.A9CameraIp = " 10.0.0.20 ";
        vm.A9CameraUid = " uid-1 ";
        vm.A9CameraUsername = " user ";
        vm.A9CameraPassword = " pass ";

        await vm.SaveAsync();

        settings.A9CameraIp.Should().Be("10.0.0.20");
        settings.A9CameraUid.Should().Be("uid-1");
        settings.A9CameraUsername.Should().Be("user");
        settings.A9CameraPassword.Should().Be("pass");
        vm.Status.Should().Be("Saved");
    }

    [Fact]
    public async Task SaveAsync_BlankCredentials_DefaultsToAdmin()
    {
        var settings = new FakeSettingsService();
        var vm = CreateVm(settings);
        vm.A9CameraIp = "192.168.1.1";
        vm.A9CameraUsername = "";
        vm.A9CameraPassword = " ";

        await vm.SaveAsync();

        settings.A9CameraUsername.Should().Be("admin");
        settings.A9CameraPassword.Should().Be("admin");
        vm.A9CameraUsername.Should().Be("admin");
        vm.A9CameraPassword.Should().Be("admin");
    }

    [Fact]
    public async Task TestConnectionAsync_WithBlankIp_ShowsValidationStatus()
    {
        var called = false;
        var vm = CreateVm(new FakeSettingsService(), testConnectionAsync: (_, _) =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await vm.TestConnectionAsync();

        called.Should().BeFalse();
        vm.Status.Should().Be("Enter an IP address.");
    }

    [Fact]
    public async Task TestConnectionAsync_WhenTesterSucceeds_PersistsAndReportsSuccess()
    {
        var settings = new FakeSettingsService();
        A9CameraConnectionSettings? tested = null;
        var vm = CreateVm(settings, (connectionSettings, _) =>
        {
            tested = connectionSettings;
            return Task.CompletedTask;
        });
        vm.A9CameraIp = "192.168.1.1";

        await vm.TestConnectionAsync();

        tested.Should().NotBeNull();
        tested!.Ip.Should().Be("192.168.1.1");
        settings.A9CameraIp.Should().Be("192.168.1.1");
        vm.Status.Should().Be("Connection test succeeded.");
        vm.IsTesting.Should().BeFalse();
    }

    [Fact]
    public async Task TestConnectionAsync_WhenTesterFails_ReportsFailure()
    {
        var vm = CreateVm(new FakeSettingsService(), (_, _) =>
            throw new InvalidOperationException("camera unavailable"));
        vm.A9CameraIp = "192.168.1.1";

        await vm.TestConnectionAsync();

        vm.Status.Should().Be("Connection test failed: camera unavailable");
        vm.IsTesting.Should().BeFalse();
    }

    private static A9CameraSettingsViewModel CreateVm(
        FakeSettingsService settings,
        Func<A9CameraConnectionSettings, CancellationToken, Task>? testConnectionAsync = null)
    {
        return new A9CameraSettingsViewModel(
            settings,
            NullLogger<A9CameraSettingsViewModel>.Instance,
            testConnectionAsync);
    }
}
