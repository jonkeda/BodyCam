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
    private readonly IHeyCyanAudioEndpointActivationService _audioEndpointActivation;
    private CancellationTokenSource? _scanCts;

    public GlassesViewModel(
        HeyCyanGlassesDeviceManager glasses,
        ISettingsService settings,
        IHeyCyanAudioEndpointActivationService audioEndpointActivation)
    {
        _glasses = glasses;
        _settings = settings;
        _audioEndpointActivation = audioEndpointActivation;
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

    private string? _connectionDetailStatus;
    public string? ConnectionDetailStatus
    {
        get => _connectionDetailStatus;
        private set
        {
            if (SetProperty(ref _connectionDetailStatus, value))
                OnPropertyChanged(nameof(ShowWindowsAudioActivationStatus));
        }
    }

    private bool _isAudioActivationRunning;
    public bool IsAudioActivationRunning
    {
        get => _isAudioActivationRunning;
        private set
        {
            if (SetProperty(ref _isAudioActivationRunning, value))
                OnPropertyChanged(nameof(ShowWindowsAudioActivationStatus));
        }
    }

    public bool ShowWindowsAudioActivationStatus =>
        _audioEndpointActivation.IsSupported
        && (IsAudioActivationRunning || !string.IsNullOrWhiteSpace(ConnectionDetailStatus));

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
            RunOnMainThreadOrInline(() =>
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

        var selectedDevice = SelectedDevice;
        var shouldReturnToSettings = true;

        try
        {
            if (_audioEndpointActivation.RequiresActivationBeforeBleConnect)
            {
                SetConnectionDetailStatus(
                    $"Connecting Windows Bluetooth audio for {selectedDevice.Name} before BLE...");
                IsAudioActivationRunning = true;

                HeyCyanAudioEndpointSnapshot audioSnapshot;
                try
                {
                    audioSnapshot = await RunAudioActivationAsync(selectedDevice);
                }
                finally
                {
                    IsAudioActivationRunning = false;
                }

                if (!audioSnapshot.IsReady)
                {
                    SetConnectionDetailStatus(BuildPreBleAudioPendingMessage(audioSnapshot));
                    return;
                }
            }

            SetConnectionDetailStatus($"Connecting to {selectedDevice.Name}...");
            await _glasses.ConnectAsync(selectedDevice, CancellationToken.None);

            if (_glasses.State == GlassesConnectionState.Connected
                && _audioEndpointActivation.IsSupported)
            {
                shouldReturnToSettings = false;
                IsAudioActivationRunning = true;
                try
                {
                    SetConnectionDetailStatus("Checking Windows Bluetooth audio...");
                    var snapshot = _audioEndpointActivation.RequiresActivationBeforeBleConnect
                        ? await _audioEndpointActivation.RefreshAsync(CancellationToken.None)
                        : await RunAudioActivationAsync(selectedDevice);

                    SetConnectionDetailStatus(snapshot.Summary);
                    shouldReturnToSettings = snapshot.IsReady;
                }
                finally
                {
                    IsAudioActivationRunning = false;
                }
            }

            if (_glasses.State == GlassesConnectionState.Connected && shouldReturnToSettings)
                await TryNavigateBackAsync();
        }
        catch (Exception ex)
        {
            IsAudioActivationRunning = false;
            SetConnectionDetailStatus($"Connection failed: {ex.Message}");
        }
    }

    private Task DisconnectAsync()
        => _glasses.DisconnectAsync(CancellationToken.None);

    private async Task<HeyCyanAudioEndpointSnapshot> RunAudioActivationAsync(
        HeyCyanDeviceInfo selectedDevice)
    {
        void OnActivationUpdated(object? _, HeyCyanAudioEndpointSnapshot snapshot)
            => SetConnectionDetailStatus(snapshot.Summary);

        _audioEndpointActivation.Updated += OnActivationUpdated;
        try
        {
            return await _audioEndpointActivation.BeginActivationAsync(
                selectedDevice,
                CancellationToken.None);
        }
        finally
        {
            _audioEndpointActivation.Updated -= OnActivationUpdated;
        }
    }

    private static string BuildPreBleAudioPendingMessage(
        HeyCyanAudioEndpointSnapshot snapshot)
    {
        return snapshot.Summary
            + " The app has not connected BLE yet, so Windows can still connect Classic Bluetooth. "
            + "Click Connect in Windows Bluetooth settings, then press Connect here again.";
    }

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
        OnPropertyChanged(nameof(ShowWindowsAudioActivationStatus));
        DisconnectCommand.RaiseCanExecuteChanged();
    }

    private void SetConnectionDetailStatus(string? status)
    {
        try
        {
            if (MainThread.IsMainThread)
            {
                ConnectionDetailStatus = status;
                return;
            }

            MainThread.BeginInvokeOnMainThread(() => ConnectionDetailStatus = status);
        }
        catch
        {
            ConnectionDetailStatus = status;
        }
    }

    private static void RunOnMainThreadOrInline(Action action)
    {
        try
        {
            if (MainThread.IsMainThread)
                action();
            else
                MainThread.BeginInvokeOnMainThread(action);
        }
        catch
        {
            action();
        }
    }

    private static async Task TryNavigateBackAsync()
    {
        var shell = Shell.Current;
        if (shell is null)
            return;

        try
        {
            await shell.GoToAsync("..");
        }
        catch
        {
            // Unit tests and some shell states do not have a navigation stack.
        }
    }
}
