using BodyCam.ViewModels;

namespace BodyCam.Pages;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnConnectionTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(Pages.Settings.ConnectionSettingsPage));

    private async void OnVoiceTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(Pages.Settings.VoiceSettingsPage));

    private async void OnDevicesTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(Pages.Settings.DeviceSettingsPage));

    private async void OnAdvancedTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(Pages.Settings.AdvancedSettingsPage));
}
