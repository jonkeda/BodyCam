using BodyCam.ViewModels;

namespace BodyCam;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnConnectionTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(Settings.ConnectionSettingsPage));

    private async void OnVoiceTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(Settings.VoiceSettingsPage));

    private async void OnDevicesTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(Settings.DeviceSettingsPage));

    private async void OnAdvancedTapped(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(Settings.AdvancedSettingsPage));
}
