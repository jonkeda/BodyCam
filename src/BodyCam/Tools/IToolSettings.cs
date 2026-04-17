namespace BodyCam.Tools;

/// <summary>
/// Implemented by tools that expose user-configurable settings.
/// </summary>
public interface IToolSettings
{
    string SettingsDisplayName { get; }
    string SettingsDescription { get; }
    IReadOnlyList<ToolSettingDescriptor> GetSettingDescriptors();
    void LoadSettings(Services.ISettingsService service);
    void SaveSettings(Services.ISettingsService service);
}

public record ToolSettingDescriptor
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required ToolSettingType Type { get; init; }
    public object? DefaultValue { get; init; }
    public Func<object>? GetValue { get; init; }
    public Action<object>? SetValue { get; init; }
}

public enum ToolSettingType
{
    Boolean,
    Integer,
    Text
}
