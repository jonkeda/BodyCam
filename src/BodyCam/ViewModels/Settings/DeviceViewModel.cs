using System.Diagnostics;
using BodyCam.Models;
using BodyCam.Mvvm;
using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Glasses;
using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Services.Input;
using Microsoft.Maui.Controls;

namespace BodyCam.ViewModels.Settings;

public class DeviceViewModel : ViewModelBase, IDisposable
{
    private readonly CameraManager _cameraManager;
    private readonly AudioInputManager _audioInputManager;
    private readonly AudioOutputManager _audioOutputManager;
    private readonly HeyCyanGlassesDeviceManager _glasses;
    private readonly ISettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly IHeyCyanAudioEndpointActivationService _audioEndpointActivation;
    private readonly SourceProfileManager? _profileManager;
    private readonly KnownDeviceService? _knownDeviceService;

    public DeviceViewModel(
        CameraManager cameraManager,
        AudioInputManager audioInputManager,
        AudioOutputManager audioOutputManager,
        GlassesCameraSectionViewModel glassesCameraSection,
        HeyCyanGlassesDeviceManager glasses,
        ISettingsService settingsService,
        AppSettings settings,
        IHeyCyanAudioEndpointActivationService audioEndpointActivation,
        ButtonInputManager? buttonInputManager = null,
        IButtonMappingStore? buttonMappingStore = null,
        SourceProfileManager? profileManager = null,
        KnownDeviceService? knownDeviceService = null)
    {
        _cameraManager = cameraManager;
        _audioInputManager = audioInputManager;
        _audioOutputManager = audioOutputManager;
        _glasses = glasses;
        _settingsService = settingsService;
        _settings = settings;
        _audioEndpointActivation = audioEndpointActivation;
        _profileManager = profileManager;
        _knownDeviceService = knownDeviceService;
        GlassesCameraSection = glassesCameraSection;
        Title = "Devices";

        // Build dynamic button device mappings from all providers that have Buttons
        if (buttonInputManager is not null && buttonMappingStore is not null)
        {
            ButtonDeviceMappings = buttonInputManager.Providers
                .Where(p => p.Buttons.Count > 0)
                .Select(p => new ButtonDeviceMappingsViewModel(p, buttonMappingStore))
                .ToList();
        }

        ConnectGlassesCommand = new AsyncRelayCommand(async () =>
            await Shell.Current.GoToAsync("glasses"));
        DisconnectGlassesCommand = new AsyncRelayCommand(
            () => _glasses.DisconnectAsync(CancellationToken.None),
            () => IsGlassesConnected);
        RemoveGlassesCommand = new AsyncRelayCommand(
            RemoveGlassesAsync,
            () => IsGlassesConnected);
        TakePictureCommand = new AsyncRelayCommand(TakePictureAsync);
        TestSoundCommand = new AsyncRelayCommand(TestSoundAsync);
        TestRecordingCommand = new AsyncRelayCommand(TestRecordingAsync);
        RetryHeyCyanAudioCommand = new AsyncRelayCommand(
            RetryHeyCyanAudioAsync,
            () => _audioEndpointActivation.IsSupported);
        RefreshHeyCyanAudioStatusCommand = new AsyncRelayCommand(
            RefreshHeyCyanAudioStatusAsync,
            () => _audioEndpointActivation.IsSupported);
        ToggleGlassesDetailCommand = new RelayCommand(
            () => IsGlassesDetailExpanded = !IsGlassesDetailExpanded);

        _heyCyanAudioEndpointSummary = _audioEndpointActivation.Current?.Summary;

        _glasses.StateChanged += OnGlassesStateChanged;
        _glasses.StatusChanged += (_, _) => RefreshGlassesInfo();
        _audioEndpointActivation.Updated += OnAudioEndpointActivationUpdated;

        _audioInputManager.ProvidersChanged += (_, _) =>
        {
            RefreshAudioProviderProperties();
        };
        _audioInputManager.ActiveProviderChanged += (_, _) =>
        {
            RefreshAudioProviderProperties();
        };

        _audioOutputManager.ProvidersChanged += (_, _) =>
        {
            RefreshAudioProviderProperties();
        };
        _audioOutputManager.ActiveProviderChanged += (_, _) =>
        {
            RefreshAudioProviderProperties();
        };

        // Wire up profile manager
        if (_profileManager is not null)
        {
            _profileManager.ProfileChanged += OnProfileChanged;
            _profileManager.AutoSwitched += OnProfileAutoSwitched;
        }
    }

    // ── Commands ────────────────────────────────────────────────────────────

    public AsyncRelayCommand ConnectGlassesCommand { get; }
    public AsyncRelayCommand DisconnectGlassesCommand { get; }
    public AsyncRelayCommand RemoveGlassesCommand { get; }
    public AsyncRelayCommand TakePictureCommand { get; }
    public AsyncRelayCommand TestSoundCommand { get; }
    public AsyncRelayCommand TestRecordingCommand { get; }
    public AsyncRelayCommand RetryHeyCyanAudioCommand { get; }
    public AsyncRelayCommand RefreshHeyCyanAudioStatusCommand { get; }
    public RelayCommand ToggleGlassesDetailCommand { get; }

    // ── Glasses info ────────────────────────────────────────────────────────

    private bool _isGlassesDetailExpanded;
    public bool IsGlassesDetailExpanded
    {
        get => _isGlassesDetailExpanded;
        set => SetProperty(ref _isGlassesDetailExpanded, value);
    }

    public bool IsGlassesConnected => _glasses.State == GlassesConnectionState.Connected;
    public string GlassesName => _glasses.State == GlassesConnectionState.Connected
        ? _glasses.MacAddress ?? "Glasses" : string.Empty;
    public int GlassesBatteryPct => _glasses.Battery?.Percentage ?? 0;
    public bool GlassesIsCharging => _glasses.Battery?.IsCharging ?? false;
    public string GlassesMac => _glasses.MacAddress ?? "—";
    public string GlassesFirmware => _glasses.Version?.Firmware ?? "—";
    public string GlassesHardware => _glasses.Version?.Hardware ?? "—";
    public int GlassesPhotos => _glasses.MediaCount?.Photos ?? 0;
    public int GlassesVideos => _glasses.MediaCount?.Videos ?? 0;
    public int GlassesAudioFiles => _glasses.MediaCount?.AudioFiles ?? 0;

    // ── Connected Devices ───────────────────────────────────────────────────

    private IReadOnlyList<ConnectedDeviceInfo> _connectedDevices = [];
    public IReadOnlyList<ConnectedDeviceInfo> ConnectedDevices
    {
        get => _connectedDevices;
        private set => SetProperty(ref _connectedDevices, value);
    }

    /// <summary>True when at least one device is connected (glasses or other).</summary>
    public bool HasConnectedDevices => IsGlassesConnected || ConnectedDevices.Count > 0;

    private void RefreshConnectedDevices()
    {
        var devices = new List<ConnectedDeviceInfo>();

        // Register glasses as known device (but don't add to the list — rendered separately)
        if (_glasses.State == GlassesConnectionState.Connected)
        {
            var mac = _glasses.MacAddress ?? "unknown";

            // Register as known device
            _knownDeviceService?.AddOrUpdate(mac, _settingsService.LastHeyCyanDeviceName ?? "HeyCyan Glasses",
                "heycyan-glasses", new Dictionary<string, string>
                {
                    ["firmware"] = _glasses.Version?.Firmware ?? "",
                    ["hardware"] = _glasses.Version?.Hardware ?? "",
                });
        }

        // BT audio devices (from providers with "bt:" prefix)
        foreach (var p in _audioInputManager.Providers.Where(
            p => p.ProviderId.StartsWith("bt:", StringComparison.OrdinalIgnoreCase) && p.IsAvailable))
        {
            var mac = p.ProviderId[3..];
            if (devices.Any(d => string.Equals(d.DeviceId, mac, StringComparison.OrdinalIgnoreCase)))
                continue;

            devices.Add(new ConnectedDeviceInfo
            {
                DeviceId = mac,
                DisplayName = p.DisplayName,
                DeviceType = "bluetooth-audio",
                StatusLine = $"Bluetooth Audio  MAC: {mac}",
                CanDisconnect = false,
            });
        }

        foreach (var p in _audioOutputManager.Providers.Where(
            p => p.ProviderId.StartsWith("bt:", StringComparison.OrdinalIgnoreCase) && p.IsAvailable))
        {
            var mac = p.ProviderId[3..];
            if (devices.Any(d => string.Equals(d.DeviceId, mac, StringComparison.OrdinalIgnoreCase)))
                continue;

            devices.Add(new ConnectedDeviceInfo
            {
                DeviceId = mac,
                DisplayName = p.DisplayName,
                DeviceType = "bluetooth-audio",
                StatusLine = $"Bluetooth Audio  MAC: {mac}",
                CanDisconnect = false,
            });
        }

        ConnectedDevices = devices;
        OnPropertyChanged(nameof(HasConnectedDevices));
    }

    // ── Glasses persistence ─────────────────────────────────────────────────

    public bool HeyCyanAutoReconnect
    {
        get => _settingsService.HeyCyanAutoReconnect;
        set
        {
            if (_settingsService.HeyCyanAutoReconnect != value)
            {
                _settingsService.HeyCyanAutoReconnect = value;
                OnPropertyChanged();
            }
        }
    }

    public string? SavedGlassesDeviceName => _settingsService.LastHeyCyanDeviceName;

    // ── Windows HeyCyan audio endpoint activation ───────────────────────────

    private string? _heyCyanAudioEndpointSummary;
    public string? HeyCyanAudioEndpointSummary
    {
        get => _heyCyanAudioEndpointSummary;
        private set
        {
            if (SetProperty(ref _heyCyanAudioEndpointSummary, value))
                OnPropertyChanged(nameof(ShowHeyCyanAudioEndpointControls));
        }
    }

    private bool _isHeyCyanAudioActivationRunning;
    public bool IsHeyCyanAudioActivationRunning
    {
        get => _isHeyCyanAudioActivationRunning;
        private set
        {
            if (SetProperty(ref _isHeyCyanAudioActivationRunning, value))
                OnPropertyChanged(nameof(ShowHeyCyanAudioEndpointControls));
        }
    }

    public bool ShowHeyCyanAudioEndpointControls =>
        _audioEndpointActivation.IsSupported
        && (IsGlassesConnected
            || IsHeyCyanAudioActivationRunning
            || !string.IsNullOrWhiteSpace(HeyCyanAudioEndpointSummary));

    // ── Audio test state ────────────────────────────────────────────────────

    private bool _isTestingSound;
    public bool IsTestingSound
    {
        get => _isTestingSound;
        private set => SetProperty(ref _isTestingSound, value);
    }

    private bool _isTestingRecording;
    public bool IsTestingRecording
    {
        get => _isTestingRecording;
        private set => SetProperty(ref _isTestingRecording, value);
    }

    private string? _testRecordingStatus;
    public string? TestRecordingStatus
    {
        get => _testRecordingStatus;
        private set => SetProperty(ref _testRecordingStatus, value);
    }

    public GlassesCameraSectionViewModel GlassesCameraSection { get; }

    // ── Take Picture state ──────────────────────────────────────────────────

    private bool _isTakingPicture;
    public bool IsTakingPicture
    {
        get => _isTakingPicture;
        private set => SetProperty(ref _isTakingPicture, value);
    }

    private string? _takePictureStatus;
    public string? TakePictureStatus
    {
        get => _takePictureStatus;
        private set => SetProperty(ref _takePictureStatus, value);
    }

    private ImageSource? _lastPictureImage;
    public ImageSource? LastPictureImage
    {
        get => _lastPictureImage;
        private set => SetProperty(ref _lastPictureImage, value);
    }

    private long? _lastPictureLatencyMs;
    public long? LastPictureLatencyMs
    {
        get => _lastPictureLatencyMs;
        private set => SetProperty(ref _lastPictureLatencyMs, value);
    }

    /// <summary>True when the selected camera provider supports video recording.</summary>
    public bool ShowRecordVideo => _cameraManager.Active?.SupportsVideoRecording ?? false;

    /// <summary>One entry per connected button device with Buttons descriptors.</summary>
    public IReadOnlyList<ButtonDeviceMappingsViewModel> ButtonDeviceMappings { get; } = [];

    // ── Source Profile ──────────────────────────────────────────────────────

    /// <summary>All registered source profiles, ordered for the dropdown.</summary>
    public IReadOnlyList<ISourceProfile> AvailableProfiles =>
        _profileManager?.AvailableProfiles ?? [];

    /// <summary>Currently selected source profile.</summary>
    public ISourceProfile? SelectedProfile
    {
        get => _profileManager?.ActiveProfile;
        set
        {
            if (value is not null && value != _profileManager?.ActiveProfile)
            {
                _ = _profileManager!.ApplyProfileAsync(value.Id);
            }
        }
    }

    /// <summary>True when the profile system is not active or profile is "custom".</summary>
    public bool ShowSourceProfilePicker => _profileManager is not null;

    /// <summary>True when individual pickers should be visible (custom mode or no profile manager).</summary>
    public bool IsCustomMode => _profileManager is null || _profileManager.ActiveProfile?.Id == "custom";

    // ── Auto-switch notification ────────────────────────────────────────────

    private string? _autoSwitchMessage;

    /// <summary>Brief notification shown when the profile auto-switches. Clears after a few seconds.</summary>
    public string? AutoSwitchMessage
    {
        get => _autoSwitchMessage;
        private set
        {
            if (SetProperty(ref _autoSwitchMessage, value))
                OnPropertyChanged(nameof(HasAutoSwitchMessage));
        }
    }

    public bool HasAutoSwitchMessage => !string.IsNullOrEmpty(AutoSwitchMessage);

    private CancellationTokenSource? _autoSwitchDismissCts;

    // ── Provider pickers ────────────────────────────────────────────────────

    public IReadOnlyList<ICameraProvider> CameraProviders => _cameraManager.Providers;

    public ICameraProvider? SelectedCameraProvider
    {
        get => _cameraManager.Active;
        set
        {
            if (value is not null && value != _cameraManager.Active)
            {
                _ = _cameraManager.SetActiveAsync(value.ProviderId);
                SwitchToCustomIfNeeded();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowRecordVideo));
            }
        }
    }

    public IReadOnlyList<IAudioInputProvider> AudioInputProviders =>
        _audioInputManager.Providers;

    public string? HeyCyanAudioInputStatus =>
        GetHeyCyanAudioStatus(
            _audioInputManager.Providers.FirstOrDefault(p => p.ProviderId == "heycyan-glasses"),
            "microphone");

    public IAudioInputProvider? SelectedAudioInputProvider
    {
        get => _audioInputManager.Active;
        set
        {
            if (value is not null && value != _audioInputManager.Active)
            {
                _pendingAudioInputProviderId = value.ProviderId;
                _ = _audioInputManager.SetActiveAsync(value.ProviderId);
                SwitchToCustomIfNeeded();
                OnPropertyChanged();
            }
        }
    }

    private string? _pendingAudioInputProviderId;

    public IReadOnlyList<IAudioOutputProvider> AudioOutputProviders =>
        _audioOutputManager.Providers;

    public string? HeyCyanAudioOutputStatus =>
        GetHeyCyanAudioStatus(
            _audioOutputManager.Providers.FirstOrDefault(p => p.ProviderId == "heycyan-glasses"),
            "speaker");

    public IAudioOutputProvider? SelectedAudioOutputProvider
    {
        get => _audioOutputManager.Active;
        set
        {
            if (value is not null && value != _audioOutputManager.Active)
            {
                _pendingAudioOutputProviderId = value.ProviderId;
                _ = _audioOutputManager.SetActiveAsync(value.ProviderId);
                SwitchToCustomIfNeeded();
                OnPropertyChanged();
            }
        }
    }

    private string? _pendingAudioOutputProviderId;

    public void Dispose()
    {
        _glasses.StateChanged -= OnGlassesStateChanged;
        _audioEndpointActivation.Updated -= OnAudioEndpointActivationUpdated;
        if (_profileManager is not null)
        {
            _profileManager.ProfileChanged -= OnProfileChanged;
            _profileManager.AutoSwitched -= OnProfileAutoSwitched;
        }
        _autoSwitchDismissCts?.Cancel();
        _autoSwitchDismissCts?.Dispose();
        GlassesCameraSection?.Dispose();
    }

    // ── Profile event handling ──────────────────────────────────────────────

    private void OnProfileChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(IsCustomMode));
            OnPropertyChanged(nameof(SelectedCameraProvider));
            OnPropertyChanged(nameof(ShowRecordVideo));
            RefreshAudioProviderProperties();
        });
    }

    private void OnProfileAutoSwitched(object? sender, ProfileSwitchNotification notification)
    {
        MainThread.BeginInvokeOnMainThread(() => ShowAutoSwitchMessage(notification.Message));
    }

    private void ShowAutoSwitchMessage(string message)
    {
        _autoSwitchDismissCts?.Cancel();
        _autoSwitchDismissCts?.Dispose();
        AutoSwitchMessage = message;

        _autoSwitchDismissCts = new CancellationTokenSource();
        var ct = _autoSwitchDismissCts.Token;

        _ = Task.Delay(TimeSpan.FromSeconds(5), ct).ContinueWith(_ =>
        {
            if (!ct.IsCancellationRequested)
                MainThread.BeginInvokeOnMainThread(() => AutoSwitchMessage = null);
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// When the user manually changes an individual picker, auto-switch profile to Custom.
    /// </summary>
    private void SwitchToCustomIfNeeded()
    {
        if (_profileManager is not null && _profileManager.ActiveProfile?.Id != "custom")
        {
            _ = _profileManager.ApplyProfileAsync("custom");
        }
    }

    // ── Glasses state handling ──────────────────────────────────────────────

    private async Task RemoveGlassesAsync()
    {
        await _glasses.DisconnectAsync(CancellationToken.None);

        var mac = _glasses.MacAddress;
        if (mac is not null)
            _knownDeviceService?.Remove(mac);

        _settingsService.LastHeyCyanDeviceName = null;
        RefreshGlassesInfo();
    }

    private void OnGlassesStateChanged(object? sender, GlassesConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            RefreshGlassesInfo();
            DisconnectGlassesCommand.RaiseCanExecuteChanged();
            RemoveGlassesCommand.RaiseCanExecuteChanged();

            RefreshAudioProviderProperties();

            if (state == GlassesConnectionState.Connected)
            {
                if (_profileManager is not null)
                    await _profileManager.HandleDeviceConnectedAsync();
                else
                    await AutoSelectGlassesProvidersAsync();
                if (_audioEndpointActivation.IsSupported)
                    await RefreshHeyCyanAudioStatusAsync();
            }
            else if (state == GlassesConnectionState.Disconnected)
            {
                if (_profileManager is not null)
                    await _profileManager.HandleDeviceDisconnectedAsync();
                else
                    await RevertToDefaultProvidersAsync();
            }
        });
    }

    private async Task AutoSelectGlassesProvidersAsync()
    {
        const string id = "heycyan-glasses";
        if (_cameraManager.Providers.Any(p => p.ProviderId == id))
            await _cameraManager.SetActiveAsync(id);
        if (_audioInputManager.Providers.Any(p => p.ProviderId == id && p.IsAvailable))
            await _audioInputManager.SetActiveAsync(id);
        if (_audioOutputManager.Providers.Any(p => p.ProviderId == id && p.IsAvailable))
            await _audioOutputManager.SetActiveAsync(id);

        OnPropertyChanged(nameof(SelectedCameraProvider));
        RefreshAudioProviderProperties();
    }

    private async Task RevertToDefaultProvidersAsync()
    {
        var defaultCam = _cameraManager.Providers.FirstOrDefault(p => p.ProviderId != "heycyan-glasses");
        var defaultMic = _audioInputManager.Providers.FirstOrDefault(p => p.ProviderId != "heycyan-glasses");
        var defaultSpk = _audioOutputManager.Providers.FirstOrDefault(p => p.ProviderId != "heycyan-glasses");

        if (defaultCam is not null) await _cameraManager.SetActiveAsync(defaultCam.ProviderId);
        if (defaultMic is not null) await _audioInputManager.SetActiveAsync(defaultMic.ProviderId);
        if (defaultSpk is not null) await _audioOutputManager.SetActiveAsync(defaultSpk.ProviderId);

        OnPropertyChanged(nameof(SelectedCameraProvider));
        RefreshAudioProviderProperties();
    }

    private void RefreshGlassesInfo()
    {
        OnPropertyChanged(nameof(IsGlassesConnected));
        OnPropertyChanged(nameof(GlassesName));
        OnPropertyChanged(nameof(GlassesBatteryPct));
        OnPropertyChanged(nameof(GlassesIsCharging));
        OnPropertyChanged(nameof(GlassesMac));
        OnPropertyChanged(nameof(GlassesFirmware));
        OnPropertyChanged(nameof(GlassesHardware));
        OnPropertyChanged(nameof(GlassesPhotos));
        OnPropertyChanged(nameof(GlassesVideos));
        OnPropertyChanged(nameof(GlassesAudioFiles));
        OnPropertyChanged(nameof(ShowHeyCyanAudioEndpointControls));
        RefreshConnectedDevices();
    }

    private void RefreshAudioProviderProperties()
    {
        OnPropertyChanged(nameof(AudioInputProviders));
        OnPropertyChanged(nameof(SelectedAudioInputProvider));
        OnPropertyChanged(nameof(HeyCyanAudioInputStatus));
        OnPropertyChanged(nameof(AudioOutputProviders));
        OnPropertyChanged(nameof(SelectedAudioOutputProvider));
        OnPropertyChanged(nameof(HeyCyanAudioOutputStatus));
        RefreshConnectedDevices();
    }

    private async Task RetryHeyCyanAudioAsync()
    {
        if (!_audioEndpointActivation.IsSupported)
            return;

        if (_audioEndpointActivation.RequiresActivationBeforeBleConnect && IsGlassesConnected)
        {
            HeyCyanAudioEndpointSummary =
                "Disconnect the glasses, then use Connect Glasses again so Windows audio can connect before BLE.";
            return;
        }

        IsHeyCyanAudioActivationRunning = true;
        try
        {
            var snapshot = await _audioEndpointActivation.BeginActivationAsync(null, CancellationToken.None);
            HeyCyanAudioEndpointSummary = snapshot.Summary;
            RefreshAudioProviderProperties();
        }
        finally
        {
            IsHeyCyanAudioActivationRunning = false;
        }
    }

    private async Task RefreshHeyCyanAudioStatusAsync()
    {
        if (!_audioEndpointActivation.IsSupported)
            return;

        IsHeyCyanAudioActivationRunning = true;
        try
        {
            var snapshot = await _audioEndpointActivation.RefreshAsync(CancellationToken.None);
            HeyCyanAudioEndpointSummary = snapshot.Summary;
            RefreshAudioProviderProperties();
        }
        finally
        {
            IsHeyCyanAudioActivationRunning = false;
        }
    }

    private void OnAudioEndpointActivationUpdated(object? sender, HeyCyanAudioEndpointSnapshot snapshot)
    {
        try
        {
            if (MainThread.IsMainThread)
            {
                ApplyAudioEndpointSnapshot(snapshot);
                return;
            }

            MainThread.BeginInvokeOnMainThread(() => ApplyAudioEndpointSnapshot(snapshot));
        }
        catch
        {
            ApplyAudioEndpointSnapshot(snapshot);
        }
    }

    private void ApplyAudioEndpointSnapshot(HeyCyanAudioEndpointSnapshot snapshot)
    {
        HeyCyanAudioEndpointSummary = snapshot.Summary;
        RefreshAudioProviderProperties();
    }

    private static string? GetHeyCyanAudioStatus(object? provider, string label)
    {
        return provider switch
        {
            IAudioInputProvider input when input.IsAvailable => $"HeyCyan {label} ready",
            IAudioOutputProvider output when output.IsAvailable => $"HeyCyan {label} ready",
            IAudioInputProvider or IAudioOutputProvider => $"HeyCyan {label} waiting for Windows Bluetooth endpoint",
            _ => null
        };
    }

    // ── Take Picture ──────────────────────────────────────────────────────

    private async Task TakePictureAsync()
    {
        var provider = _cameraManager.Active;
        if (provider is null)
        {
            TakePictureStatus = "No camera selected";
            return;
        }

        IsTakingPicture = true;
        var sw = Stopwatch.StartNew();
        try
        {
            TakePictureStatus = "Capturing…";
            var jpg = await provider.CaptureFrameAsync(CancellationToken.None);
            sw.Stop();

            if (jpg is null)
            {
                TakePictureStatus = "Error: Capture returned null";
                LastPictureImage = null;
                LastPictureLatencyMs = null;
            }
            else
            {
                LastPictureImage = ImageSource.FromStream(() => new MemoryStream(jpg));
                LastPictureLatencyMs = sw.ElapsedMilliseconds;
                TakePictureStatus = $"Captured {jpg.Length:N0} bytes in {sw.ElapsedMilliseconds} ms";
            }
        }
        catch (Exception ex)
        {
            TakePictureStatus = $"Error: {ex.Message}";
            LastPictureImage = null;
            LastPictureLatencyMs = null;
        }
        finally
        {
            IsTakingPicture = false;
        }
    }

    // ── Audio tests ─────────────────────────────────────────────────────────

    private async Task TestSoundAsync()
    {
        // Ensure the manager's active provider matches the picker selection
        if (_pendingAudioOutputProviderId is not null)
        {
            await _audioOutputManager.SetActiveAsync(_pendingAudioOutputProviderId);
            _pendingAudioOutputProviderId = null;
        }

        var provider = _audioOutputManager.Active;
        if (provider is null) return;

        IsTestingSound = true;
        try
        {
            // Generate 1-second 440 Hz sine wave, 16kHz mono PCM16
            const int sampleRate = 16000;
            const int durationMs = 1000;
            const double frequency = 440.0;
            var samples = sampleRate * durationMs / 1000;
            var pcm = new byte[samples * 2];
            for (int i = 0; i < samples; i++)
            {
                var value = (short)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * 8000);
                pcm[i * 2] = (byte)(value & 0xFF);
                pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }

            await provider.StartAsync(sampleRate);
            await provider.PlayChunkAsync(pcm);
            await Task.Delay(durationMs + 200);
            await provider.StopAsync();
        }
        finally
        {
            IsTestingSound = false;
        }
    }

    private async Task TestRecordingAsync()
    {
        // Ensure the manager's active provider matches the picker selection
        if (_pendingAudioInputProviderId is not null)
        {
            await _audioInputManager.SetActiveAsync(_pendingAudioInputProviderId);
            _pendingAudioInputProviderId = null;
        }

        var input = _audioInputManager.Active;
        var output = _audioOutputManager.Active;
        if (input is null || output is null) return;

        IsTestingRecording = true;
        var buffer = new List<byte[]>();
        void OnChunk(object? _, byte[] chunk) => buffer.Add(chunk);

        try
        {
            // Record 3 seconds
            TestRecordingStatus = "Recording…";
            input.AudioChunkAvailable += OnChunk;
            await input.StartAsync();
            await Task.Delay(3000);
            await input.StopAsync();
            input.AudioChunkAvailable -= OnChunk;

            if (buffer.Count == 0)
            {
                TestRecordingStatus = "No audio captured";
                return;
            }

            // Playback — use the same sample rate the input provider resampled to
            TestRecordingStatus = "Playing back…";
            var playbackRate = _audioInputManager.Active is not null
                ? _settings.SampleRate
                : 16000;
            await output.StartAsync(playbackRate);
            foreach (var chunk in buffer)
                await output.PlayChunkAsync(chunk);

            // Wait for the buffer to drain (recording was ~3s)
            await Task.Delay(3500);
            await output.StopAsync();
            TestRecordingStatus = $"Done — {buffer.Count} chunks recorded";
        }
        catch (Exception ex)
        {
            TestRecordingStatus = $"Error: {ex.Message}";
        }
        finally
        {
            input.AudioChunkAvailable -= OnChunk;
            IsTestingRecording = false;
        }
    }
}
