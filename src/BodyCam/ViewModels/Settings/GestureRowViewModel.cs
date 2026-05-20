namespace BodyCam.ViewModels.Settings;

using BodyCam.Mvvm;
using BodyCam.Services.Input;

/// <summary>
/// View model for a single gesture-to-action mapping row.
/// Works for any button device (glasses, keyboard, BT remote, etc.).
/// </summary>
public sealed class GestureRowViewModel : ViewModelBase
{
    private readonly IButtonMappingStore _store;
    private readonly string _provider;
    private readonly string _button;
    private readonly ButtonGesture _gesture;
    private readonly string? _buttonDisplayName;
    private ButtonAction _action;

    public GestureRowViewModel(
        IButtonMappingStore store, string provider, string button,
        ButtonGesture gesture, string? buttonDisplayName = null)
    {
        _store = store;
        _provider = provider;
        _button = button;
        _gesture = gesture;
        _buttonDisplayName = buttonDisplayName;
        _action = store.Get(provider, button, gesture);
    }

    public string Label
    {
        get
        {
            var gestureName = _gesture switch
            {
                ButtonGesture.SingleTap => "Single Tap",
                ButtonGesture.DoubleTap => "Double Tap",
                ButtonGesture.TripleTap => "Triple Tap",
                ButtonGesture.LongPress => "Long Press",
                ButtonGesture.LongPressRelease => "Long Press Release",
                _ => _gesture.ToString(),
            };

            return _buttonDisplayName is not null
                ? $"{_buttonDisplayName} — {gestureName}"
                : gestureName;
        }
    }

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
