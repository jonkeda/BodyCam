using BodyCam.ViewModels.Settings;

namespace BodyCam.Pages.Settings;

public partial class AdvancedSettingsPage : ContentPage
{
    public AdvancedSettingsPage(AdvancedViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
