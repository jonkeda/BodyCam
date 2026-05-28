using BodyCam.ViewModels.Settings;
using FluentAssertions;

namespace BodyCam.Tests.ViewModels.Settings;

public class AddDevicesViewModelTests
{
    [Fact]
    public void Title_IsAddDevices()
    {
        var vm = new AddDevicesViewModel();

        vm.Title.Should().Be("Add Devices");
    }

    [Fact]
    public void DeviceOptions_IncludesAddCyanGlasses()
    {
        var vm = new AddDevicesViewModel();

        vm.DeviceOptions.Should().ContainSingle(option =>
            option.Title == "Add Cyan Glasses"
            && option.Description.Contains("camera")
            && option.AutomationId == "AddCyanGlassesButton"
            && option.Command == vm.AddCyanGlassesCommand);
    }

    [Fact]
    public void DeviceOptions_IncludesAddA9Camera()
    {
        var vm = new AddDevicesViewModel();

        vm.DeviceOptions.Should().ContainSingle(option =>
            option.Title == "Add A9 Camera"
            && option.Description.Contains("iLnkP2P")
            && option.AutomationId == "AddA9CameraButton"
            && option.Command == vm.AddA9CameraCommand);
    }

    [Fact]
    public async Task AddCyanGlassesAsync_NavigatesToGlassesRoute()
    {
        var routes = new List<string>();
        var vm = new AddDevicesViewModel(route =>
        {
            routes.Add(route);
            return Task.CompletedTask;
        });

        await vm.AddCyanGlassesAsync();

        routes.Should().Equal(AddDevicesViewModel.CyanGlassesRoute);
    }

    [Fact]
    public async Task AddA9CameraAsync_NavigatesToA9CameraSettingsRoute()
    {
        var routes = new List<string>();
        var vm = new AddDevicesViewModel(route =>
        {
            routes.Add(route);
            return Task.CompletedTask;
        });

        await vm.AddA9CameraAsync();

        routes.Should().Equal(AddDevicesViewModel.A9CameraRoute);
    }
}
