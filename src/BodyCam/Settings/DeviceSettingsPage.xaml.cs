using BodyCam.ViewModels;

namespace BodyCam.Settings;

public partial class DeviceSettingsPage : ContentPage
{
    public DeviceSettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
