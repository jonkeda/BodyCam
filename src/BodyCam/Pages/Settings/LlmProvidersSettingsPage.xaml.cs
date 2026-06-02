using BodyCam.ViewModels.Settings;

namespace BodyCam.Pages.Settings;

public partial class LlmProvidersSettingsPage : ContentPage
{
    private readonly LlmProvidersViewModel _viewModel;

    public LlmProvidersSettingsPage(LlmProvidersViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.RefreshAsync();
    }

    private async void OnAddProviderClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(AddLlmProviderPage));

    private async void OnEditProviderClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: LlmProviderRowViewModel row })
        {
            await Shell.Current.GoToAsync(
                $"{nameof(LlmProviderSettingsPage)}?providerId={Uri.EscapeDataString(row.ProviderId)}");
        }
    }
}
