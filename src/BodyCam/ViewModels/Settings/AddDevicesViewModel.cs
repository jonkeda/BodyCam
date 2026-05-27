using System.Windows.Input;
using BodyCam.Mvvm;
using Microsoft.Maui.Controls;

namespace BodyCam.ViewModels.Settings;

public sealed class AddDevicesViewModel : ViewModelBase
{
    public const string CyanGlassesRoute = "glasses";

    private readonly Func<string, Task> _navigateAsync;

    public AddDevicesViewModel(Func<string, Task>? navigateAsync = null)
    {
        _navigateAsync = navigateAsync ?? (route => Shell.Current.GoToAsync(route));
        Title = "Add Devices";

        AddCyanGlassesCommand = new AsyncRelayCommand(AddCyanGlassesAsync);
        DeviceOptions =
        [
            new AddDeviceOptionViewModel(
                "glasses",
                "Add Cyan Glasses",
                "Connect Cyan glasses for camera, mic, speaker, and button input.",
                AddCyanGlassesCommand)
        ];
    }

    public AsyncRelayCommand AddCyanGlassesCommand { get; }

    public IReadOnlyList<AddDeviceOptionViewModel> DeviceOptions { get; }

    public Task AddCyanGlassesAsync() => _navigateAsync(CyanGlassesRoute);
}

public sealed record AddDeviceOptionViewModel(
    string Icon,
    string Title,
    string Description,
    ICommand Command);
