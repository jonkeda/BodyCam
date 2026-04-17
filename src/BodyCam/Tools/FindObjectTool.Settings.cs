using BodyCam.Services;

namespace BodyCam.Tools;

public partial class FindObjectTool : IToolSettings
{
    public string SettingsDisplayName => "Find Object";
    public string SettingsDescription => "Configure object detection scanning behavior.";

    public IReadOnlyList<ToolSettingDescriptor> GetSettingDescriptors() => new[]
    {
        new ToolSettingDescriptor
        {
            Key = "FindObject.ScanInterval",
            Label = "Scan Interval (seconds)",
            Type = ToolSettingType.Integer,
            DefaultValue = 3,
            GetValue = () => ScanIntervalSeconds,
            SetValue = v => ScanIntervalSeconds = Convert.ToInt32(v)
        },
        new ToolSettingDescriptor
        {
            Key = "FindObject.Timeout",
            Label = "Timeout (seconds)",
            Type = ToolSettingType.Integer,
            DefaultValue = 30,
            GetValue = () => TimeoutSeconds,
            SetValue = v => TimeoutSeconds = Convert.ToInt32(v)
        }
    };

    public void LoadSettings(ISettingsService service)
    {
        foreach (var d in GetSettingDescriptors())
        {
            var stored = Preferences.Get(d.Key, d.DefaultValue?.ToString() ?? "");
            if (d.Type == ToolSettingType.Integer && int.TryParse(stored, out var intVal))
                d.SetValue?.Invoke(intVal);
            else if (d.Type == ToolSettingType.Boolean && bool.TryParse(stored, out var boolVal))
                d.SetValue?.Invoke(boolVal);
            else if (d.Type == ToolSettingType.Text)
                d.SetValue?.Invoke(stored);
        }
    }

    public void SaveSettings(ISettingsService service)
    {
        foreach (var d in GetSettingDescriptors())
        {
            var value = d.GetValue?.Invoke()?.ToString() ?? "";
            Preferences.Set(d.Key, value);
        }
    }
}
