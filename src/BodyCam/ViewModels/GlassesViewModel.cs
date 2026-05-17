using System.Collections.ObjectModel;
using BodyCam.Mvvm;
using BodyCam.Services;
using BodyCam.Services.Glasses;
using BodyCam.Services.Glasses.HeyCyan;

namespace BodyCam.ViewModels;

public sealed class GlassesViewModel : ViewModelBase
{
    private readonly HeyCyanGlassesDeviceManager _glasses;
    private readonly ISettingsService _settings;
    private CancellationTokenSource? _scanCts;

    public GlassesViewModel(
        HeyCyanGlassesDeviceManager glasses,
        ISettingsService settings)
    {
        _glasses = glasses;
        _settings = settings;
        _glasses.StateChanged += (_, _) => RefreshAll();
        _glasses.StatusChanged += (_, _) => RefreshAll();

        ScanCommand = new AsyncRelayCommand(ScanAsync);
        StopScanCommand = new RelayCommand(StopScan);
        ConnectCommand = new AsyncRelayCommand(
            ConnectAsync, () => SelectedDevice is not null);
        DisconnectCommand = new AsyncRelayCommand(
            DisconnectAsync, () => IsConnected);
        ForgetDeviceCommand = new RelayCommand(
            ForgetDevice, () => _settings.LastHeyCyanDeviceAddress is not null);
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set => SetProperty(ref _isScanning, value);
    }

    public ObservableCollection<HeyCyanDeviceInfo> Devices { get; } = new();

    private HeyCyanDeviceInfo? _selectedDevice;
    public HeyCyanDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
                ConnectCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsConnected => _glasses.State == GlassesConnectionState.Connected;
    public int BatteryPct => _glasses.Battery?.Percentage ?? 0;
    public bool IsCharging => _glasses.Battery?.IsCharging ?? false;
    public string Mac => _glasses.MacAddress ?? "—";
    public string Firmware => _glasses.Version?.Firmware ?? "—";
    public string Hardware => _glasses.Version?.Hardware ?? "—";
    public int Photos => _glasses.MediaCount?.Photos ?? 0;
    public int Videos => _glasses.MediaCount?.Videos ?? 0;
    public int AudioFiles => _glasses.MediaCount?.AudioFiles ?? 0;

    public string StatusText => _glasses.State switch
    {
        GlassesConnectionState.Disconnected => "Not connected",
        GlassesConnectionState.Scanning => "Scanning…",
        GlassesConnectionState.Connecting => "Connecting…",
        GlassesConnectionState.Connected =>
            $"Connected — {BatteryPct}%{(IsCharging ? " ⚡" : string.Empty)}",
        _ => "Unknown",
    };

    public string? SavedDeviceName => _settings.LastHeyCyanDeviceName;

    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand StopScanCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public RelayCommand ForgetDeviceCommand { get; }

    private async Task ScanAsync()
    {
        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        try
        {
            Devices.Clear();
            var found = await _glasses.ScanAsync(TimeSpan.FromSeconds(8), _scanCts.Token);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var d in found) Devices.Add(d);
            });
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private void StopScan()
    {
        _scanCts?.Cancel();
    }

    private async Task ConnectAsync()
    {
        if (SelectedDevice is null) return;
        await _glasses.ConnectAsync(SelectedDevice, CancellationToken.None);
        // Auto-return to Devices page after successful connect
        if (_glasses.State == GlassesConnectionState.Connected)
            await Shell.Current.GoToAsync("..");
    }

    private Task DisconnectAsync()
        => _glasses.DisconnectAsync(CancellationToken.None);

    /// <summary>
    /// Delegates to the manager's auto-reconnect. Fire-and-forget from page init.
    /// </summary>
    public Task TryAutoReconnectAsync() => _glasses.TryAutoReconnectAsync();

    private void ForgetDevice()
    {
        _settings.LastHeyCyanDeviceAddress = null;
        _settings.LastHeyCyanDeviceName = null;
        OnPropertyChanged(nameof(SavedDeviceName));
        ForgetDeviceCommand.RaiseCanExecuteChanged();
    }

    private void RefreshAll()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(BatteryPct));
        OnPropertyChanged(nameof(IsCharging));
        OnPropertyChanged(nameof(Mac));
        OnPropertyChanged(nameof(Firmware));
        OnPropertyChanged(nameof(Hardware));
        OnPropertyChanged(nameof(Photos));
        OnPropertyChanged(nameof(Videos));
        OnPropertyChanged(nameof(AudioFiles));
        OnPropertyChanged(nameof(StatusText));
        DisconnectCommand.RaiseCanExecuteChanged();
    }
}
