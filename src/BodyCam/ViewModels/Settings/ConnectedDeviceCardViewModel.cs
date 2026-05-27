using System.Windows.Input;
using BodyCam.Mvvm;

namespace BodyCam.ViewModels.Settings;

/// <summary>
/// View model for one card in the Settings > Devices connected-device list.
/// </summary>
public sealed class ConnectedDeviceCardViewModel : ViewModelBase
{
    private readonly Action<ConnectedDeviceCardViewModel, bool>? _expandedChanged;
    private bool _isExpanded;

    public ConnectedDeviceCardViewModel(
        string deviceId,
        string displayName,
        string deviceType,
        string icon,
        string summary,
        IReadOnlyList<ConnectedDeviceDetailRow>? detailRows = null,
        IReadOnlyList<string>? slotTags = null,
        int? batteryPct = null,
        bool isCharging = false,
        bool isExpanded = false,
        ICommand? disconnectCommand = null,
        ICommand? removeCommand = null,
        Action<ConnectedDeviceCardViewModel, bool>? expandedChanged = null)
    {
        DeviceId = deviceId;
        DisplayName = displayName;
        DeviceType = deviceType;
        Icon = icon;
        Summary = summary;
        DetailRows = detailRows ?? [];
        SlotTags = slotTags ?? [];
        BatteryPct = batteryPct;
        IsCharging = isCharging;
        DisconnectCommand = disconnectCommand;
        RemoveCommand = removeCommand;
        _isExpanded = isExpanded;
        _expandedChanged = expandedChanged;
        ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public string DeviceId { get; }
    public string DisplayName { get; }
    public string DeviceType { get; }
    public string Icon { get; }
    public string Summary { get; }
    public IReadOnlyList<ConnectedDeviceDetailRow> DetailRows { get; }
    public IReadOnlyList<string> SlotTags { get; }
    public int? BatteryPct { get; }
    public bool IsCharging { get; }
    public ICommand? DisconnectCommand { get; }
    public ICommand? RemoveCommand { get; }
    public RelayCommand ToggleExpandedCommand { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ExpandGlyph));
                _expandedChanged?.Invoke(this, value);
            }
        }
    }

    public string AutomationId => $"ConnectedDeviceCard_{SanitizeAutomationId(DeviceId)}";
    public string ExpandGlyph => IsExpanded ? "v" : ">";
    public string BatteryText => BatteryPct is int pct ? $"Battery {pct}%" : string.Empty;
    public string ChargingText => IsCharging ? "Charging" : string.Empty;
    public string SlotSummary => SlotTags.Count == 0 ? string.Empty : $"Slots: {string.Join(", ", SlotTags)}";
    public bool HasBattery => BatteryPct.HasValue;
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
    public bool HasDetails => DetailRows.Count > 0;
    public bool HasSlotTags => SlotTags.Count > 0;
    public bool HasActions => DisconnectCommand is not null || RemoveCommand is not null;
    public bool CanDisconnect => DisconnectCommand is not null;
    public bool CanRemove => RemoveCommand is not null;

    private static string SanitizeAutomationId(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars);
    }
}

public sealed record ConnectedDeviceDetailRow(string Label, string Value);
