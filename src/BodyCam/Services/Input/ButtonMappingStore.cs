namespace BodyCam.Services.Input;

using System.Text.Json;

/// <summary>
/// Button mapping persistence using Preferences.
/// Wraps ActionMap and provides save/load to Preferences.
/// </summary>
public sealed class ButtonMappingStore : IButtonMappingStore
{
    private const string PreferencesKey = "button_mappings_v1";
    private readonly ActionMap _actionMap;

    public ButtonMappingStore(ActionMap actionMap)
    {
        _actionMap = actionMap;
    }

    public ButtonAction Get(string providerId, string buttonId, ButtonGesture gesture)
    {
        var compositeKey = $"{providerId}:{buttonId}";
        return _actionMap.GetAction(compositeKey, gesture);
    }

    public void Set(string providerId, string buttonId, ButtonGesture gesture, ButtonAction action)
    {
        var compositeKey = $"{providerId}:{buttonId}";
        _actionMap.SetAction(compositeKey, gesture, action);
        _ = SaveAsync(); // Fire and forget — save immediately on every change
    }

    public void Clear(string providerId, string buttonId, ButtonGesture gesture)
    {
        // ActionMap doesn't have a Remove method, so we set to None
        Set(providerId, buttonId, gesture, ButtonAction.None);
    }

    public Task LoadAsync()
    {
        var json = Preferences.Get(PreferencesKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            return Task.CompletedTask;

        try
        {
            var mappings = JsonSerializer.Deserialize<List<ButtonMapping>>(json);
            if (mappings is not null)
                _actionMap.LoadMappings(mappings);
        }
        catch
        {
            // Corrupt data — ignore and use defaults
        }

        return Task.CompletedTask;
    }

    public Task SaveAsync()
    {
        var mappings = _actionMap.ExportMappings();
        var json = JsonSerializer.Serialize(mappings);
        Preferences.Set(PreferencesKey, json);
        return Task.CompletedTask;
    }
}
