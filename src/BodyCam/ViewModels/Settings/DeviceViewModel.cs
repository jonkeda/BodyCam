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
    public const string AddDevicesRoute = "AddDevicesPage";

    private readonly CameraManager _cameraManager;
    private readonly AudioInputManager _audioInputManager;
    private readonly AudioOutputManager _audioOutputManager;
    private readonly HeyCyanGlassesDeviceManager _glasses;
    private readonly ISettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly IHeyCyanAudioEndpointActivationService _audioEndpointActivation;
    private readonly SourceProfileManager? _profileManager;
    private readonly KnownDeviceService? _knownDeviceService;
    private readonly ButtonInputManager? _buttonInputManager;
    private readonly Func<string, Task> _navigateAsync;
    private readonly HashSet<string> _expandedConnectedDeviceIds = new(StringComparer.OrdinalIgnoreCase);

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
        KnownDeviceService? knownDeviceService = null,
        Func<string, Task>? navigateAsync = null)
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
        _buttonInputManager = buttonInputManager;
        _navigateAsync = navigateAsync ?? (route => Shell.Current.GoToAsync(route));
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

        ConnectDeviceCommand = new AsyncRelayCommand(OpenAddDevicesAsync);
        ConnectGlassesCommand = ConnectDeviceCommand;
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

        RefreshConnectedDevices();
    }

    // ── Commands ────────────────────────────────────────────────────────────

    public AsyncRelayCommand ConnectGlassesCommand { get; }
    public AsyncRelayCommand ConnectDeviceCommand { get; }
    public AsyncRelayCommand DisconnectGlassesCommand { get; }
    public AsyncRelayCommand RemoveGlassesCommand { get; }
    public AsyncRelayCommand TakePictureCommand { get; }
    public AsyncRelayCommand TestSoundCommand { get; }
    public AsyncRelayCommand TestRecordingCommand { get; }
    public AsyncRelayCommand RetryHeyCyanAudioCommand { get; }
    public AsyncRelayCommand RefreshHeyCyanAudioStatusCommand { get; }
    public RelayCommand ToggleGlassesDetailCommand { get; }

    public Task OpenAddDevicesAsync() => _navigateAsync(AddDevicesRoute);

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

    private IReadOnlyList<ConnectedDeviceCardViewModel> _connectedDevices = [];
    public IReadOnlyList<ConnectedDeviceCardViewModel> ConnectedDevices
    {
        get => _connectedDevices;
        private set => SetProperty(ref _connectedDevices, value);
    }

    /// <summary>True when at least one connected device card is available.</summary>
    public bool HasConnectedDevices => ConnectedDevices.Count > 0;

    /// <summary>True when the connected-devices list should show its empty state.</summary>
    public bool HasNoConnectedDevices => !HasConnectedDevices;

    private void RefreshConnectedDevices()
    {
        var devices = new List<ConnectedDeviceCardViewModel>();

        if (_glasses.State == GlassesConnectionState.Connected)
        {
            var mac = _glasses.MacAddress ?? "unknown";

            _knownDeviceService?.AddOrUpdate(mac, _settingsService.LastHeyCyanDeviceName ?? "HeyCyan Glasses",
                "heycyan-glasses", new Dictionary<string, string>
                {
                    ["firmware"] = _glasses.Version?.Firmware ?? "",
                    ["hardware"] = _glasses.Version?.Hardware ?? "",
                });

            devices.Add(CreateGlassesDeviceCard());
        }

        AddAudioDeviceCards(devices);
        AddButtonDeviceCards(devices);
        AddCameraDeviceCards(devices);

        ConnectedDevices = devices;
        OnPropertyChanged(nameof(HasConnectedDevices));
        OnPropertyChanged(nameof(HasNoConnectedDevices));
    }

    private ConnectedDeviceCardViewModel CreateGlassesDeviceCard()
    {
        var mac = GlassesMac;
        var deviceId = $"glasses:{mac}";
        return new ConnectedDeviceCardViewModel(
            deviceId: deviceId,
            displayName: _settingsService.LastHeyCyanDeviceName ?? "HeyCyan Glasses",
            deviceType: "heycyan-glasses",
            icon: "#glasses",
            summary: "Camera + mic + speaker + buttons",
            batteryPct: _glasses.Battery?.Percentage,
            isCharging: _glasses.Battery?.IsCharging ?? false,
            isExpanded: IsConnectedDeviceExpanded(deviceId),
            detailRows:
            [
                new("MAC", mac),
                new("Firmware", GlassesFirmware),
                new("Hardware", GlassesHardware),
                new("Media", $"{GlassesPhotos} photos, {GlassesVideos} videos, {GlassesAudioFiles} audio")
            ],
            slotTags: ["Camera", "Microphone", "Speaker", "Buttons"],
            disconnectCommand: DisconnectGlassesCommand,
            removeCommand: RemoveGlassesCommand,
            expandedChanged: OnConnectedDeviceExpansionChanged);
    }

    private void AddAudioDeviceCards(List<ConnectedDeviceCardViewModel> devices)
    {
        var audioDevices = new Dictionary<string, AudioDeviceAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in _audioInputManager.Providers.Where(p => p.IsAvailable))
            AddAudioProvider(audioDevices, provider.ProviderId, provider.DisplayName, isInput: true);

        foreach (var provider in _audioOutputManager.Providers.Where(p => p.IsAvailable))
            AddAudioProvider(audioDevices, provider.ProviderId, provider.DisplayName, isInput: false);

        foreach (var audio in audioDevices.Values.OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var slotTags = new List<string>();
            if (audio.HasInput) slotTags.Add("Microphone");
            if (audio.HasOutput) slotTags.Add("Speaker");

            var deviceId = $"audio:{audio.DeviceId}";
            devices.Add(new ConnectedDeviceCardViewModel(
                deviceId: deviceId,
                displayName: audio.DisplayName,
                deviceType: "audio",
                icon: "#headphones",
                summary: audio.GetSummary(),
                detailRows:
                [
                    new("Type", "Bluetooth audio"),
                    new("Providers", string.Join(", ", audio.ProviderIds.Distinct(StringComparer.OrdinalIgnoreCase)))
                ],
                slotTags: slotTags,
                isExpanded: IsConnectedDeviceExpanded(deviceId),
                expandedChanged: OnConnectedDeviceExpansionChanged));
        }
    }

    private static void AddAudioProvider(
        IDictionary<string, AudioDeviceAccumulator> audioDevices,
        string providerId,
        string displayName,
        bool isInput)
    {
        if (!IsConnectedAudioProvider(providerId))
            return;

        var deviceId = NormalizeAudioDeviceId(providerId);
        if (!audioDevices.TryGetValue(deviceId, out var audio))
        {
            audio = new AudioDeviceAccumulator(deviceId, displayName);
            audioDevices[deviceId] = audio;
        }

        audio.ProviderIds.Add(providerId);
        if (isInput) audio.HasInput = true;
        else audio.HasOutput = true;
    }

    private void AddButtonDeviceCards(List<ConnectedDeviceCardViewModel> devices)
    {
        if (_buttonInputManager is null)
            return;

        foreach (var provider in _buttonInputManager.Providers
                     .Where(p => p.IsAvailable && p.Buttons.Count > 0)
                     .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (IsGlassesProvider(provider.ProviderId) && IsGlassesConnected)
                continue;

            var buttonCount = provider.Buttons.Count;
            var gestures = provider.Buttons
                .SelectMany(b => b.SupportedGestures)
                .Distinct()
                .Select(FormatGesture);

            var deviceId = $"buttons:{provider.ProviderId}";
            devices.Add(new ConnectedDeviceCardViewModel(
                deviceId: deviceId,
                displayName: provider.DisplayName,
                deviceType: provider.ProviderId == "keyboard" ? "keyboard" : "button-device",
                icon: provider.ProviderId == "keyboard" ? "#keyboard" : "#buttons",
                summary: $"Button device - {buttonCount} {Pluralize(buttonCount, "button")}",
                detailRows:
                [
                    new("Buttons", string.Join(", ", provider.Buttons.Select(b => b.DisplayName))),
                    new("Gestures", string.Join(", ", gestures))
                ],
                slotTags: ["Buttons"],
                isExpanded: IsConnectedDeviceExpanded(deviceId),
                expandedChanged: OnConnectedDeviceExpansionChanged));
        }
    }

    private void AddCameraDeviceCards(List<ConnectedDeviceCardViewModel> devices)
    {
        foreach (var provider in _cameraManager.Providers
                     .Where(p => p.IsAvailable && IsConnectedCameraProvider(p.ProviderId))
                     .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var deviceId = $"camera:{provider.ProviderId}";
            devices.Add(new ConnectedDeviceCardViewModel(
                deviceId: deviceId,
                displayName: provider.DisplayName,
                deviceType: "camera",
                icon: "#camera",
                summary: provider.SupportsVideoRecording
                    ? "External camera - photo + video"
                    : "External camera - photo",
                detailRows:
                [
                    new("Provider", provider.ProviderId),
                    new("Video", provider.SupportsVideoRecording ? "Supported" : "Not supported")
                ],
                slotTags: ["Camera"],
                isExpanded: IsConnectedDeviceExpanded(deviceId),
                expandedChanged: OnConnectedDeviceExpansionChanged));
        }
    }

    private bool IsConnectedDeviceExpanded(string deviceId)
        => _expandedConnectedDeviceIds.Contains(deviceId);

    private void OnConnectedDeviceExpansionChanged(ConnectedDeviceCardViewModel device, bool isExpanded)
    {
        if (isExpanded)
            _expandedConnectedDeviceIds.Add(device.DeviceId);
        else
            _expandedConnectedDeviceIds.Remove(device.DeviceId);
    }

    private static bool IsConnectedAudioProvider(string providerId)
    {
        if (IsGlassesProvider(providerId))
            return false;

        return providerId.StartsWith("bt:", StringComparison.OrdinalIgnoreCase)
            || providerId.StartsWith("bt-out:", StringComparison.OrdinalIgnoreCase)
            || providerId.Contains("bluetooth", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAudioDeviceId(string providerId)
    {
        if (providerId.StartsWith("bt-out:", StringComparison.OrdinalIgnoreCase))
            return providerId["bt-out:".Length..];
        if (providerId.StartsWith("bt:", StringComparison.OrdinalIgnoreCase))
            return providerId["bt:".Length..];
        return providerId;
    }

    private static bool IsConnectedCameraProvider(string providerId)
        => !IsGlassesProvider(providerId)
           && !string.Equals(providerId, "phone", StringComparison.OrdinalIgnoreCase);

    private static bool IsGlassesProvider(string providerId)
        => string.Equals(providerId, "heycyan-glasses", StringComparison.OrdinalIgnoreCase);

    private static string Pluralize(int count, string singular)
        => count == 1 ? singular : $"{singular}s";

    private static string FormatGesture(ButtonGesture gesture) => gesture switch
    {
        ButtonGesture.SingleTap => "single press",
        ButtonGesture.DoubleTap => "double press",
        ButtonGesture.TripleTap => "triple press",
        ButtonGesture.LongPress => "long press",
        _ => gesture.ToString()
    };

    private sealed class AudioDeviceAccumulator
    {
        public AudioDeviceAccumulator(string deviceId, string displayName)
        {
            DeviceId = deviceId;
            DisplayName = displayName;
        }

        public string DeviceId { get; }
        public string DisplayName { get; }
        public bool HasInput { get; set; }
        public bool HasOutput { get; set; }
        public List<string> ProviderIds { get; } = [];

        public string GetSummary() => (HasInput, HasOutput) switch
        {
            (true, true) => "Bluetooth microphone + speaker",
            (true, false) => "Bluetooth microphone",
            (false, true) => "Bluetooth speaker",
            _ => "Bluetooth audio"
        };
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
                _ = SetCameraProviderAsync(value.ProviderId);
                SwitchToCustomIfNeeded();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowRecordVideo));
            }
        }
    }

    private async Task SetCameraProviderAsync(string providerId)
    {
        try
        {
            await _cameraManager.SetActiveAsync(providerId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set camera provider '{providerId}': {ex.Message}");
        }
        finally
        {
            RunOnMainThreadOrInline(() =>
            {
                OnPropertyChanged(nameof(SelectedCameraProvider));
                OnPropertyChanged(nameof(ShowRecordVideo));
                RefreshConnectedDevices();
            });
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
                if (_profileManager is null)
                    await AutoSelectGlassesProvidersAsync();
                if (_audioEndpointActivation.IsSupported)
                    await RefreshHeyCyanAudioStatusAsync();
            }
            else if (state == GlassesConnectionState.Disconnected)
            {
                if (_profileManager is null)
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
