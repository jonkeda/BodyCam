using BodyCam.ViewModels;

namespace BodyCam;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private void OnOpenAiChecked(object? sender, CheckedChangedEventArgs e)
    {
        if (e.Value && BindingContext is SettingsViewModel vm)
            vm.SelectedProvider = OpenAiProvider.OpenAi;
    }

    private void OnAzureChecked(object? sender, CheckedChangedEventArgs e)
    {
        if (e.Value && BindingContext is SettingsViewModel vm)
            vm.SelectedProvider = OpenAiProvider.Azure;
    }
}
