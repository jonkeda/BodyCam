using System.Collections.ObjectModel;
using BodyCam.Mvvm;
using BodyCam.Tools;

namespace BodyCam.ViewModels;

public class ToolSettingsSection : ObservableObject
{
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public ObservableCollection<ToolSettingItem> Items { get; } = new();
}

public class ToolSettingItem : ObservableObject
{
    private readonly ToolSettingDescriptor _descriptor;

    public ToolSettingItem(ToolSettingDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public string Label => _descriptor.Label;
    public ToolSettingType Type => _descriptor.Type;

    public bool IsBoolean => Type == ToolSettingType.Boolean;
    public bool IsInteger => Type == ToolSettingType.Integer;
    public bool IsText => Type == ToolSettingType.Text;

    private string _stringValue = "";
    public string StringValue
    {
        get => _stringValue;
        set
        {
            if (SetProperty(ref _stringValue, value))
            {
                if (Type == ToolSettingType.Integer && int.TryParse(value, out var intVal))
                    _descriptor.SetValue?.Invoke(intVal);
                else if (Type == ToolSettingType.Text)
                    _descriptor.SetValue?.Invoke(value);
            }
        }
    }

    private bool _boolValue;
    public bool BoolValue
    {
        get => _boolValue;
        set
        {
            if (SetProperty(ref _boolValue, value))
                _descriptor.SetValue?.Invoke(value);
        }
    }

    public void LoadFromDescriptor()
    {
        var val = _descriptor.GetValue?.Invoke();
        if (val is null) return;

        if (Type == ToolSettingType.Boolean)
            _boolValue = val is bool b && b;
        else
            _stringValue = val.ToString() ?? "";

        OnPropertyChanged(nameof(StringValue));
        OnPropertyChanged(nameof(BoolValue));
    }
}
