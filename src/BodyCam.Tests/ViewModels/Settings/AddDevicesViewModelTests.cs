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
    public void DeviceOptions_IncludesAddVue990Camera()
    {
        var vm = new AddDevicesViewModel();

        vm.DeviceOptions.Should().ContainSingle(option =>
            option.Title == "Add Vue990 Camera"
            && option.Description.Contains("managed C#")
            && option.AutomationId == "AddVue990CameraButton"
            && option.Command == vm.AddVue990CameraCommand);
    }

    [Fact]
    public void DeviceOptions_IncludesAddUsbCamera()
    {
        var vm = new AddDevicesViewModel();

        vm.DeviceOptions.Should().ContainSingle(option =>
            option.Title == "Add USB Camera"
            && option.Description.Contains("USB/UVC")
            && option.AutomationId == "AddUsbCameraButton"
            && option.Command == vm.AddUsbCameraCommand);
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

    [Fact]
    public async Task AddVue990CameraAsync_NavigatesToVue990CameraSettingsRoute()
    {
        var routes = new List<string>();
        var vm = new AddDevicesViewModel(route =>
        {
            routes.Add(route);
            return Task.CompletedTask;
        });

        await vm.AddVue990CameraAsync();

        routes.Should().Equal(AddDevicesViewModel.Vue990CameraRoute);
    }

    [Fact]
    public async Task AddUsbCameraAsync_NavigatesToUsbCameraSettingsRoute()
    {
        var routes = new List<string>();
        var vm = new AddDevicesViewModel(route =>
        {
            routes.Add(route);
            return Task.CompletedTask;
        });

        await vm.AddUsbCameraAsync();

        routes.Should().Equal(AddDevicesViewModel.UsbCameraRoute);
    }
}
