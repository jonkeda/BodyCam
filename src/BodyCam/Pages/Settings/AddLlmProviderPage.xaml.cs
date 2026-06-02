using BodyCam.Services.AiProviders;
using BodyCam.ViewModels.Settings;

namespace BodyCam.Pages.Settings;

public partial class AddLlmProviderPage : ContentPage
{
    private readonly IAiProviderInstanceStore _store;

    public AddLlmProviderPage(AddLlmProviderViewModel viewModel, IAiProviderInstanceStore store)
    {
        InitializeComponent();
        _store = store;
        BindingContext = viewModel;
    }

    private async void OnProviderTapped(object? sender, EventArgs e)
    {
        if (sender is not VisualElement { BindingContext: AddLlmProviderChoiceViewModel choice })
            return;

        await _store.EnsureInstanceAsync(choice.ProviderId);
        await Shell.Current.GoToAsync(
            $"{nameof(LlmProviderSettingsPage)}?providerId={Uri.EscapeDataString(choice.ProviderId)}");
    }
}
