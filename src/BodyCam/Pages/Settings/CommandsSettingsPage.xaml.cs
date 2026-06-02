using BodyCam.ViewModels.Settings;

namespace BodyCam.Pages.Settings;

public partial class CommandsSettingsPage : ContentPage
{
    public CommandsSettingsPage(CommandsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnCommandSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not CommandListItem item)
            return;

        if (sender is CollectionView list)
            list.SelectedItem = null;

        await Shell.Current.GoToAsync(
            $"{nameof(CommandDetailSettingsPage)}?id={Uri.EscapeDataString(item.Id)}");
    }
}
