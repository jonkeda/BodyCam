using BodyCam.ViewModels;

namespace BodyCam.Settings;

public partial class AdvancedSettingsPage : ContentPage
{
    public AdvancedSettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
