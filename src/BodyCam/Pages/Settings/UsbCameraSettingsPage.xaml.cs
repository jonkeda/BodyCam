using BodyCam.ViewModels.Settings;

namespace BodyCam.Pages.Settings;

public partial class UsbCameraSettingsPage : ContentPage
{
    public UsbCameraSettingsPage(UsbCameraSettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

