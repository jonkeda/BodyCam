using BodyCam.ViewModels.Settings;

namespace BodyCam.Pages.Settings;

[QueryProperty(nameof(ProviderId), "providerId")]
public partial class LlmProviderSettingsPage : ContentPage
{
    private readonly LlmProviderDetailViewModel _viewModel;
    private string _providerId = string.Empty;

    public LlmProviderSettingsPage(LlmProviderDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    public string ProviderId
    {
        get => _providerId;
        set
        {
            _providerId = value;
            _ = _viewModel.LoadProviderAsync(Uri.UnescapeDataString(value));
        }
    }
}
