using BodyCam.ViewModels.Settings;

namespace BodyCam.Pages.Settings;

public partial class VoiceSettingsPage : ContentPage
{
    public VoiceSettingsPage(VoiceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
