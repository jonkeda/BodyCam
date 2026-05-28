using System.Windows.Input;
using BodyCam.Mvvm;
using Microsoft.Maui.Controls;

namespace BodyCam.ViewModels.Settings;

public sealed class AddDevicesViewModel : ViewModelBase
{
    public const string CyanGlassesRoute = "glasses";
    public const string A9CameraRoute = "A9CameraSettingsPage";

    private readonly Func<string, Task> _navigateAsync;

    public AddDevicesViewModel(Func<string, Task>? navigateAsync = null)
    {
        _navigateAsync = navigateAsync ?? (route => Shell.Current.GoToAsync(route));
        Title = "Add Devices";

        AddCyanGlassesCommand = new AsyncRelayCommand(AddCyanGlassesAsync);
        AddA9CameraCommand = new AsyncRelayCommand(AddA9CameraAsync);
        DeviceOptions =
        [
            new AddDeviceOptionViewModel(
                "glasses",
                "Add Cyan Glasses",
                "Connect Cyan glasses for camera, mic, speaker, and button input.",
                "AddCyanGlassesButton",
                AddCyanGlassesCommand),
            new AddDeviceOptionViewModel(
                "camera",
                "Add A9 Camera",
                "Connect an A9/X5 IP camera over iLnkP2P/PPPP.",
                "AddA9CameraButton",
                AddA9CameraCommand)
        ];
    }

    public AsyncRelayCommand AddCyanGlassesCommand { get; }
    public AsyncRelayCommand AddA9CameraCommand { get; }

    public IReadOnlyList<AddDeviceOptionViewModel> DeviceOptions { get; }

    public Task AddCyanGlassesAsync() => _navigateAsync(CyanGlassesRoute);

    public Task AddA9CameraAsync() => _navigateAsync(A9CameraRoute);
}

public sealed record AddDeviceOptionViewModel(
    string Icon,
    string Title,
    string Description,
    string AutomationId,
    ICommand Command);
