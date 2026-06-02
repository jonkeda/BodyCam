using BodyCam.ViewModels.Settings;

namespace BodyCam.Pages.Settings;

[QueryProperty(nameof(CommandId), "id")]
public partial class CommandDetailSettingsPage : ContentPage
{
    private readonly CommandDetailViewModel _viewModel;

    public CommandDetailSettingsPage(CommandDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    public string? CommandId
    {
        set => _viewModel.Load(value);
    }
}
