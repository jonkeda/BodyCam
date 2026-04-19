using BodyCam.ViewModels.Settings;

namespace BodyCam.Pages.Settings;

public partial class ConnectionSettingsPage : ContentPage
{
    public ConnectionSettingsPage(ConnectionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private void OnOpenAiChecked(object? sender, CheckedChangedEventArgs e)
    {
        if (e.Value && BindingContext is ConnectionViewModel vm)
            vm.SelectedProvider = OpenAiProvider.OpenAi;
    }

    private void OnAzureChecked(object? sender, CheckedChangedEventArgs e)
    {
        if (e.Value && BindingContext is ConnectionViewModel vm)
            vm.SelectedProvider = OpenAiProvider.Azure;
    }
}
