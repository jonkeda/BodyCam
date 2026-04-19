using BodyCam.ViewModels.Settings;

namespace BodyCam.Pages.Settings;

public partial class DeviceSettingsPage : ContentPage
{
    public DeviceSettingsPage(DeviceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
