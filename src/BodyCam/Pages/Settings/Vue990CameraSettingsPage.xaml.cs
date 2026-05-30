using BodyCam.ViewModels.Settings;

namespace BodyCam.Pages.Settings;

public partial class Vue990CameraSettingsPage : ContentPage
{
    public Vue990CameraSettingsPage(Vue990CameraSettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
