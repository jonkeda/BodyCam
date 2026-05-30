using System.Windows.Input;
using BodyCam.Mvvm;
using Microsoft.Maui.Controls;

namespace BodyCam.ViewModels.Settings;

public sealed class AddDevicesViewModel : ViewModelBase
{
    public const string CyanGlassesRoute = "glasses";
    public const string A9CameraRoute = "A9CameraSettingsPage";
    public const string Vue990CameraRoute = "Vue990CameraSettingsPage";

    private readonly Func<string, Task> _navigateAsync;

    public AddDevicesViewModel(Func<string, Task>? navigateAsync = null)
    {
        _navigateAsync = navigateAsync ?? (route => Shell.Current.GoToAsync(route));
        Title = "Add Devices";

        AddCyanGlassesCommand = new AsyncRelayCommand(AddCyanGlassesAsync);
        AddA9CameraCommand = new AsyncRelayCommand(AddA9CameraAsync);
        AddVue990CameraCommand = new AsyncRelayCommand(AddVue990CameraAsync);
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
                AddA9CameraCommand),
            new AddDeviceOptionViewModel(
                "camera",
                "Add Vue990 Camera",
                "Connect a Vue990/BK7252N camera over the managed C# direct path.",
                "AddVue990CameraButton",
                AddVue990CameraCommand)
        ];
    }

    public AsyncRelayCommand AddCyanGlassesCommand { get; }
    public AsyncRelayCommand AddA9CameraCommand { get; }
    public AsyncRelayCommand AddVue990CameraCommand { get; }

    public IReadOnlyList<AddDeviceOptionViewModel> DeviceOptions { get; }

    public Task AddCyanGlassesAsync() => _navigateAsync(CyanGlassesRoute);

    public Task AddA9CameraAsync() => _navigateAsync(A9CameraRoute);

    public Task AddVue990CameraAsync() => _navigateAsync(Vue990CameraRoute);
}

public sealed record AddDeviceOptionViewModel(
    string Icon,
    string Title,
    string Description,
    string AutomationId,
    ICommand Command);
