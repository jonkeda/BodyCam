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
            && option.Command == vm.AddCyanGlassesCommand);
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
}
