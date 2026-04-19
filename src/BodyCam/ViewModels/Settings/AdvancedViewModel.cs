using System.Collections.ObjectModel;
using BodyCam.Mvvm;
using BodyCam.Services;
using BodyCam.Tools;

namespace BodyCam.ViewModels.Settings;

public class AdvancedViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;

    public AdvancedViewModel(ISettingsService settings, IEnumerable<ITool> tools)
    {
        _settings = settings;
        Title = "Advanced";
        LoadToolSettings(tools);
    }

    // --- Debug ---

    public bool DebugMode
    {
        get => _settings.DebugMode;
        set => SetProperty(_settings.DebugMode, value, v => _settings.DebugMode = v);
    }

    public bool ShowTokenCounts
    {
        get => _settings.ShowTokenCounts;
        set => SetProperty(_settings.ShowTokenCounts, value, v => _settings.ShowTokenCounts = v);
    }

    public bool ShowCostEstimate
    {
        get => _settings.ShowCostEstimate;
        set => SetProperty(_settings.ShowCostEstimate, value, v => _settings.ShowCostEstimate = v);
    }

    // --- Diagnostics & Telemetry ---

    public bool SendDiagnosticData
    {
        get => _settings.SendDiagnosticData;
        set => SetProperty(_settings.SendDiagnosticData, value, v => _settings.SendDiagnosticData = v);
    }

    public string? AzureMonitorConnectionString
    {
        get => _settings.AzureMonitorConnectionString;
        set => SetProperty(_settings.AzureMonitorConnectionString, value, v => _settings.AzureMonitorConnectionString = v);
    }

    public bool SendCrashReports
    {
        get => _settings.SendCrashReports;
        set => SetProperty(_settings.SendCrashReports, value, v => _settings.SendCrashReports = v);
    }

    public string? SentryDsn
    {
        get => _settings.SentryDsn;
        set => SetProperty(_settings.SentryDsn, value, v => _settings.SentryDsn = v);
    }

    public bool SendUsageData
    {
        get => _settings.SendUsageData;
        set => SetProperty(_settings.SendUsageData, value, v => _settings.SendUsageData = v);
    }

    // --- Tool Settings ---

    public ObservableCollection<ToolSettingsSection> ToolSettingsSections { get; } = new();

    private void LoadToolSettings(IEnumerable<ITool> tools)
    {
        foreach (var tool in tools.OfType<IToolSettings>())
        {
            tool.LoadSettings(_settings);
            var section = new ToolSettingsSection
            {
                DisplayName = tool.SettingsDisplayName,
                Description = tool.SettingsDescription
            };

            foreach (var descriptor in tool.GetSettingDescriptors())
            {
                var item = new ToolSettingItem(descriptor);
                item.LoadFromDescriptor();
                section.Items.Add(item);
            }

            ToolSettingsSections.Add(section);
        }
    }

    public void SaveToolSettings()
    {
        // Settings are already applied via SetValue callbacks
    }
}
