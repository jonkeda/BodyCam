namespace BodyCam.ViewModels.Settings;

using BodyCam.Mvvm;
using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Services.Input;

/// <summary>
/// View model for the HeyCyan glasses button mapping section.
/// Exposes the three fixed gestures with a picker of ButtonAction values.
/// </summary>
public sealed class HeyCyanButtonMappingsViewModel : ViewModelBase
{
    public HeyCyanButtonMappingsViewModel(IButtonMappingStore store)
    {
        AvailableActions = Enum.GetValues<ButtonAction>();
        Rows = HeyCyanButtonDefaults.SupportedGestures
            .Select(g => new GestureRowViewModel(
                store,
                HeyCyanButtonDefaults.ProviderId,
                HeyCyanButtonDefaults.ButtonId,
                g))
            .ToList();
    }

    public IReadOnlyList<ButtonAction> AvailableActions { get; }
    public IReadOnlyList<GestureRowViewModel> Rows { get; }
}
