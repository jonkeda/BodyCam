using BodyCam.ViewModels;

namespace BodyCam.Settings;

public partial class VoiceSettingsPage : ContentPage
{
    public VoiceSettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
