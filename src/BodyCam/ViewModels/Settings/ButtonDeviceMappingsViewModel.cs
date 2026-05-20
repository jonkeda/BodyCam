namespace BodyCam.ViewModels.Settings;

using BodyCam.Mvvm;
using BodyCam.Services.Input;

/// <summary>
/// View model for a single button device's mappings section.
/// Dynamically builds rows from the provider's <see cref="IButtonInputProvider.Buttons"/>
/// descriptors — works for any device (glasses, keyboard, BT remote, etc.).
/// </summary>
public sealed class ButtonDeviceMappingsViewModel : ViewModelBase
{
    public ButtonDeviceMappingsViewModel(
        IButtonInputProvider provider,
        IButtonMappingStore store)
    {
        DeviceName = provider.DisplayName;
        ProviderId = provider.ProviderId;
        AvailableActions = Enum.GetValues<ButtonAction>();
        ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);

        var hasMultipleButtons = provider.Buttons.Count > 1;

        Rows = provider.Buttons
            .SelectMany(b => b.SupportedGestures.Select(g => new GestureRowViewModel(
                store,
                provider.ProviderId,
                b.ButtonId,
                g,
                hasMultipleButtons ? b.DisplayName : null)))
            .ToList();
    }

    public string DeviceName { get; }
    public string ProviderId { get; }
    public IReadOnlyList<ButtonAction> AvailableActions { get; }
    public IReadOnlyList<GestureRowViewModel> Rows { get; }
    public RelayCommand ToggleExpandedCommand { get; }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}
