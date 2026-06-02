using BodyCam.ViewModels.Settings;

namespace BodyCam.Pages.Settings;

public partial class ConnectionSettingsPage : ContentPage
{
    public ConnectionSettingsPage(ConnectionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private void OnProviderChecked(object? sender, CheckedChangedEventArgs e)
    {
        if (!e.Value)
            return;

        if (sender is RadioButton { BindingContext: ProviderOptionViewModel option }
            && BindingContext is ConnectionViewModel vm)
        {
            vm.SelectedProviderId = option.Id;
        }
    }
}
