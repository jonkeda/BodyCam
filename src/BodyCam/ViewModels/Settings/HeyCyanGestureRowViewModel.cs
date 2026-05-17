namespace BodyCam.ViewModels.Settings;

using BodyCam.Mvvm;
using BodyCam.Services.Input;

/// <summary>
/// View model for a single HeyCyan gesture-to-action mapping row.
/// </summary>
public sealed class HeyCyanGestureRowViewModel : ViewModelBase
{
    private readonly IButtonMappingStore _store;
    private readonly string _provider;
    private readonly string _button;
    private readonly ButtonGesture _gesture;
    private ButtonAction _action;

    public HeyCyanGestureRowViewModel(
        IButtonMappingStore store, string provider, string button, ButtonGesture gesture)
    {
        _store = store;
        _provider = provider;
        _button = button;
        _gesture = gesture;
        _action = store.Get(provider, button, gesture);
    }

    public string Label => _gesture switch
    {
        ButtonGesture.SingleTap => "Single Tap",
        ButtonGesture.DoubleTap => "Double Tap",
        ButtonGesture.LongPress => "Long Press",
        _ => _gesture.ToString(),
    };

    public ButtonAction Action
    {
        get => _action;
        set
        {
            if (SetProperty(ref _action, value))
                _store.Set(_provider, _button, _gesture, value);
        }
    }
}
