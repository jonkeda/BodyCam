using BodyCam.ViewModels.Settings;

namespace BodyCam.Pages.Settings;

public partial class A9CameraSettingsPage : ContentPage
{
    public A9CameraSettingsPage(A9CameraSettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
