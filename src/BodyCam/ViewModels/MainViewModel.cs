using System.Collections.ObjectModel;
using System.Windows.Input;
using BodyCam.Models;
using BodyCam.Mvvm;
using BodyCam.Orchestration;
using BodyCam.Services;
using BodyCam.Services.Actions;
using BodyCam.Services.Audio;
using BodyCam.Services.Audio.WebRtcApm;
using BodyCam.Services.Barcode;
using BodyCam.Services.Camera;
using BodyCam.Services.Camera.Commands;
using BodyCam.Services.Glasses;
using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Services.Input;
using BodyCam.Services.QrCode;
using BodyCam.Services.Session;
using BodyCam.Services.Transcript;
using CommunityToolkit.Maui.Views;
using Microsoft.Extensions.Logging;

namespace BodyCam.ViewModels;

public enum ListeningLayer
{
    Sleep,
    WakeWord,
    ActiveSession
}

public static class OutputModes
{
    public const string Speak = "Speak";
    public const string Silent = "Silent";

    public static string Normalize(string? value) =>
        string.Equals(value, Silent, StringComparison.OrdinalIgnoreCase) ? Silent : Speak;
}

public class MainViewModel : ViewModelBase
{
    private const string CameraActionCaptureFailedMessage = "Camera capture failed.";
    private const string CameraActionCommandFailedMessage = "Camera action failed.";

    private readonly AgentOrchestrator _orchestrator;
    private readonly IApiKeyService _apiKeyService;
    private readonly ISettingsService _settingsService;
    private readonly CameraManager _cameraManager;
    private readonly IQrCodeScanner _qrScanner;
    private readonly QrCodeService _qrCodeService;
    private readonly QrContentResolver _contentResolver;
    private readonly ICameraCommandService? _cameraCommands;
    private readonly ICameraCommandRegistry? _cameraCommandRegistry;
    private readonly IManualCameraCaptureCoordinator? _manualCapture;
    private readonly ISessionCoordinator? _sessionCoordinator;
    private readonly IAssistiveActionRegistry? _assistiveActionRegistry;
    private readonly IAssistiveActionService? _assistiveActions;
    private readonly ITranscriptStore? _transcriptStore;
    private readonly IProductBarcodeLookupWorkflow? _productLookupWorkflow;
    private readonly Func<ProductInfo, Task> _openProductDetailsAsync;
    private readonly Func<CancellationToken, Task<byte[]?>>? _cameraActionFrameCapture;
    private readonly HeyCyanGlassesDeviceManager _glasses;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IAecProcessor? _aec;
    private readonly IAudioRoutePolicyService? _audioPolicy;
    private string _debugLog = string.Empty;
    private string _aecDebugText = string.Empty;
    private string _audioPolicyDebugText = string.Empty;
    private string _aecMetricsDebugText = string.Empty;
    private string _toggleButtonText = "Start";
    private string _statusText = "Ready";
    private bool _isRunning;
    private bool _debugVisible;
    private string? _visionStatus;
    private CameraView? _cameraView;
    private bool _isTransitioning;
    private bool _isCompletingManualCapture;
    private TranscriptEntry? _activeCommandAiEntry;
    private CancellationTokenSource? _aiBusyAnimationCts;
    private string _uiSessionId = Guid.NewGuid().ToString("N");

    internal TranscriptEntry? _currentAiEntry;

    private ListeningLayer _currentLayer = ListeningLayer.Sleep;
    private bool _showTranscriptTab = true;
    private bool _showInlineCameraPreview;
    private bool _isActionsDrawerExpanded;
    private string _messageText = string.Empty;
    private string _outputMode = OutputModes.Speak;
    private ImageSource? _snapshotImage;
    private string? _snapshotCaption;
    private bool _showSnapshot;
    private CameraActionItemViewModel? _activeCameraAction;
    private bool _isExecutingCameraActionVariant;

    // Scan result overlay state
    private bool _showScanResult;
    private string _scanResultIcon = string.Empty;
    private string _scanResultTitle = string.Empty;
    private string _scanResultSummary = string.Empty;
    private IQrContentHandler? _lastScanHandler;
    private Dictionary<string, object>? _lastScanParsed;
    private string? _lastScanRawContent;

    public MainViewModel(AgentOrchestrator orchestrator, IApiKeyService apiKeyService, ISettingsService settingsService, CameraManager cameraManager, IQrCodeScanner qrScanner, QrCodeService qrCodeService, QrContentResolver contentResolver, HeyCyanGlassesDeviceManager glasses, ILogger<MainViewModel> logger, IAecProcessor? aec = null, IAudioRoutePolicyService? audioPolicy = null, ICameraCommandService? cameraCommands = null, ICameraCommandRegistry? cameraCommandRegistry = null, IManualCameraCaptureCoordinator? manualCapture = null, ISessionCoordinator? sessionCoordinator = null, IAssistiveActionRegistry? assistiveActionRegistry = null, IAssistiveActionService? assistiveActions = null, ITranscriptStore? transcriptStore = null, IProductBarcodeLookupWorkflow? productLookupWorkflow = null, Func<ProductInfo, Task>? openProductDetailsAsync = null, Func<CancellationToken, Task<byte[]?>>? cameraActionFrameCapture = null)
    {
        _orchestrator = orchestrator;
        _apiKeyService = apiKeyService;
        _settingsService = settingsService;
        _cameraManager = cameraManager;
        _qrScanner = qrScanner;
        _qrCodeService = qrCodeService;
        _contentResolver = contentResolver;
        _cameraCommands = cameraCommands;
        _cameraCommandRegistry = cameraCommandRegistry;
        _cameraActionFrameCapture = cameraActionFrameCapture;
        _manualCapture = manualCapture;
        _sessionCoordinator = sessionCoordinator;
        _assistiveActionRegistry = assistiveActionRegistry;
        _assistiveActions = assistiveActions;
        _transcriptStore = transcriptStore;
        _productLookupWorkflow = productLookupWorkflow;
        _openProductDetailsAsync = openProductDetailsAsync ?? OpenProductDetailPageAsync;
        _glasses = glasses;
        _logger = logger;
        _aec = aec;
        _audioPolicy = audioPolicy;
        Title = "BodyCam";

        _debugVisible = _settingsService.DebugMode;
        _outputMode = OutputModes.Normalize(_settingsService.OutputMode);

        // Wire AEC statistics (Phase 6.1)
        if (_aec is AecProcessor aecProc)
        {
            aecProc.StatisticsUpdated += OnAecStatisticsUpdated;
        }

        if (_audioPolicy is not null)
        {
            _audioPolicy.PolicyChanged += OnAudioRoutePolicyChanged;
            UpdateAudioPolicyDebugText(_audioPolicy.Current);
        }

        if (_manualCapture is not null)
        {
            _manualCapture.CaptureRequested += (_, _) =>
                DispatchOnMainThreadAsync(async () =>
                {
                    await RevealInlineCameraPreviewAsync();
                    OnPropertyChanged(nameof(ShowManualCaptureButton));
                });
        }

        if (_sessionCoordinator is not null)
        {
            _sessionCoordinator.StateChanged += OnSessionCoordinatorStateChanged;
        }

        // Subscribe to glasses events for shell widget
        _glasses.StateChanged += (_, _) => RefreshGlasses();
        _glasses.StatusChanged += (_, _) => RefreshGlasses();

        NavigateToGlassesCommand = new AsyncRelayCommand(async () => await Shell.Current.GoToAsync("//glasses"));
        NavigateToSettingsCommand = new AsyncRelayCommand(async () => await Shell.Current.GoToAsync(nameof(BodyCam.Pages.Settings.SettingsPage)));

        ToggleCommand = new AsyncRelayCommand(ToggleAsync);
        ClearCommand = new RelayCommand(() =>
        {
            Entries.Clear();
            _currentAiEntry = null;
            _ = ClearTranscriptSessionAsync();
        });

        SetStateCommand = new AsyncRelayCommand(async (object? param) =>
        {
            if (param is not string segment) return;
            await SetLayerAsync(segment);
        });
        SetOutputModeCommand = new AsyncRelayCommand(async (object? param) =>
        {
            if (param is not string outputMode) return;
            await SetOutputModeAsync(outputMode);
        });

        SwitchToTranscriptCommand = new RelayCommand(() =>
        {
            ShowTranscriptTab = true;
            ShowInlineCameraPreview = false;
            // Stop camera preview when switching away to save resources
            if (CurrentLayer != ListeningLayer.ActiveSession)
                _cameraView?.StopCameraPreview();
        });
        SwitchToCameraCommand = new AsyncRelayCommand(async () =>
        {
            ShowTranscriptTab = false;
            ShowInlineCameraPreview = true;
            // Start camera preview so the native control gets non-zero size
            if (_cameraView is not null)
                await _cameraView.StartCameraPreview(CancellationToken.None);
        });
        ToggleActionsDrawerCommand = new RelayCommand(() =>
        {
            IsActionsDrawerExpanded = !IsActionsDrawerExpanded;
        });
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync);
        ToggleDebugCommand = new RelayCommand(() =>
        {
            DebugVisible = !DebugVisible;
            _settingsService.DebugMode = DebugVisible;
        });
        DismissSnapshotCommand = new RelayCommand(() => ShowSnapshot = false);

        LookCommand = new AsyncRelayCommand(async () =>
        {
            await SelectCameraActionFromUiAsync(
                AssistiveActionIds.Look,
                () => ExecuteLookCommandAsync(LookDetailLevel.Overview));
        });
        LookDetailCommand = new AsyncRelayCommand(async () =>
        {
            IsActionsDrawerExpanded = false;
            await ExecuteLookCommandAsync(LookDetailLevel.Detailed);
        });
        LookSummaryCommand = new AsyncRelayCommand(async () =>
        {
            IsActionsDrawerExpanded = false;
            await ExecuteLookCommandAsync(LookDetailLevel.Summary);
        });
        ReadCommand = new AsyncRelayCommand(async () =>
        {
            await SelectCameraActionFromUiAsync(
                AssistiveActionIds.Read,
                () => ExecuteCameraCommandAsync("read", CommandTriggerOrigin.ActionsDrawer));
        });
        FindCommand = new AsyncRelayCommand(async () =>
        {
            await SelectCameraActionFromUiAsync(
                AssistiveActionIds.Find,
                () => SendVisionCommandAsync("Look around and tell me what objects you can find."));
        });
        AskCommand = new AsyncRelayCommand(async () =>
        {
            await SetLayerAsync("Active");
        });
        PhotoCommand = new AsyncRelayCommand(async () =>
        {
            if (await CompletePendingManualCaptureAsync())
                return;

            await RevealInlineCameraPreviewAsync();
            await SendVisionCommandAsync("Take a photo of what you see.");
        });
        ScanCommand = new AsyncRelayCommand(async () =>
        {
            await SelectCameraActionFromUiAsync(
                AssistiveActionIds.Scan,
                () => ExecuteCameraCommandAsync("scan", CommandTriggerOrigin.ActionsDrawer));
        });
        ProductLookupCommand = new AsyncRelayCommand(async () =>
        {
            IsActionsDrawerExpanded = false;
            await LookupProductFromUiAsync();
        });

        InitializeCameraActions();

        _orchestrator.TranscriptDelta += (_, delta) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (string.IsNullOrEmpty(delta)) return;

                if (_currentAiEntry is null)
                {
                    _currentAiEntry = new TranscriptEntry { Role = "AI", IsThinking = true };
                    Entries.Add(_currentAiEntry);
                }

                if (_currentAiEntry.IsThinking)
                    _currentAiEntry.IsThinking = false;

                _currentAiEntry.Text += delta;
            });
        };

        _orchestrator.TranscriptCompleted += (_, msg) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (msg.StartsWith("You:"))
                {
                    var userEntry = new TranscriptEntry
                    {
                        Role = "You",
                        Text = msg[4..].Trim()
                    };

                    // Insert BEFORE the current AI streaming entry so the
                    // user line appears above the response, not below it.
                    if (_currentAiEntry is not null)
                    {
                        var aiIndex = Entries.IndexOf(_currentAiEntry);
                        if (aiIndex >= 0)
                        {
                            Entries.Insert(aiIndex, userEntry);
                            TrackTranscriptEntry(userEntry);
                            return; // Don't touch _currentAiEntry — AI is still streaming
                        }
                    }

                    AddTranscriptEntry(userEntry);
                }
                else if (msg.StartsWith("AI:"))
                {
                    if (_currentAiEntry is not null)
                    {
                        _currentAiEntry.Text = msg[3..].Trim();
                        _currentAiEntry.IsThinking = false;
                        TrackTranscriptEntry(_currentAiEntry);
                    }
                    _currentAiEntry = null;
                }
            });
        };

        _orchestrator.DebugLog += (_, msg) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                DebugLog += $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}";
            });
        };

        _orchestrator.ScanResultReady += (_, e) => ShowScanResultCard(e.Handler, e.Parsed, e.RawContent);
    }

    public ObservableCollection<TranscriptEntry> Entries { get; } = [];
    public ObservableCollection<CameraActionItemViewModel> CameraActions { get; } = [];
    public ObservableCollection<CameraActionVariantViewModel> ActiveCameraActionVariants { get; } = [];

    public CameraActionItemViewModel? ActiveCameraAction
    {
        get => _activeCameraAction;
        private set
        {
            if (SetProperty(ref _activeCameraAction, value))
            {
                OnPropertyChanged(nameof(HasActiveCameraAction));
                OnPropertyChanged(nameof(HasActiveCameraActionVariants));
                OnPropertyChanged(nameof(ShowCameraActionRail));
            }
        }
    }

    public bool HasCameraActions => CameraActions.Count > 0;
    public bool ShowCameraActionRail =>
        HasCameraActions
        && ShowInlineCameraPreview
        && ActiveCameraAction is null
        && !_isExecutingCameraActionVariant;
    public bool ShowCameraActionsSection => ShowInlineCameraPreview || ShowSnapshot;
    public bool HasActiveCameraAction => ActiveCameraAction is not null;
    public bool HasActiveCameraActionVariants => ActiveCameraActionVariants.Count > 0;
    public bool ShowManualCaptureButton => _manualCapture?.IsCapturePending ?? false;

    public string DebugLog
    {
        get => _debugLog;
        set
        {
            if (SetProperty(ref _debugLog, value))
            {
                OnPropertyChanged(nameof(HasDebugLog));
                OnPropertyChanged(nameof(ShowDebugOverlay));
            }
        }
    }

    public string AecDebugText
    {
        get => _aecDebugText;
        set
        {
            if (SetProperty(ref _aecDebugText, value))
            {
                OnPropertyChanged(nameof(HasAecDebugText));
                OnPropertyChanged(nameof(ShowDebugOverlay));
            }
        }
    }

    public string ToggleButtonText
    {
        get => _toggleButtonText;
        set => SetProperty(ref _toggleButtonText, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    public bool DebugVisible
    {
        get => _debugVisible;
        set
        {
            if (SetProperty(ref _debugVisible, value))
                OnPropertyChanged(nameof(ShowDebugOverlay));
        }
    }

    public bool HasDebugLog => !string.IsNullOrWhiteSpace(DebugLog);
    public bool HasAecDebugText => !string.IsNullOrWhiteSpace(AecDebugText);
    public bool ShowDebugOverlay => DebugVisible && (HasDebugLog || HasAecDebugText);

    public string? VisionStatus
    {
        get => _visionStatus;
        set => SetProperty(ref _visionStatus, value);
    }

    public ICommand ToggleCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand SetStateCommand { get; }
    public ICommand SetOutputModeCommand { get; }
    public ICommand SwitchToTranscriptCommand { get; }
    public ICommand SwitchToCameraCommand { get; }
    public ICommand ToggleActionsDrawerCommand { get; }
    public ICommand SendMessageCommand { get; }
    public ICommand ToggleDebugCommand { get; }
    public ICommand DismissSnapshotCommand { get; }
    public ICommand LookCommand { get; }
    public ICommand LookDetailCommand { get; }
    public ICommand LookSummaryCommand { get; }
    public ICommand ReadCommand { get; }
    public ICommand FindCommand { get; }
    public ICommand AskCommand { get; }
    public ICommand PhotoCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand ProductLookupCommand { get; }

    private static readonly Color ActiveBg = Color.FromArgb("#512BD4");
    private static readonly Color InactiveBg = Colors.Transparent;
    private static readonly Color ActiveTextColor = Colors.White;
    private static readonly Color InactiveTextColor = Color.FromArgb("#999999");
    private static readonly Color ActionInactiveBg = Color.FromArgb("#F2F2F2");
    private static readonly Color ActionInactiveDarkBg = Color.FromArgb("#2D2D2D");

    public Color LookOverviewButtonColor => LookVariantColor(LookDetailLevel.Overview);
    public Color LookDetailButtonColor => LookVariantColor(LookDetailLevel.Detailed);
    public Color LookSummaryButtonColor => LookVariantColor(LookDetailLevel.Summary);
    public Color LookOverviewTextColor => LookVariantTextColor(LookDetailLevel.Overview);
    public Color LookDetailTextColor => LookVariantTextColor(LookDetailLevel.Detailed);
    public Color LookSummaryTextColor => LookVariantTextColor(LookDetailLevel.Summary);

    private Color LookVariantColor(LookDetailLevel detail) =>
        _settingsService.DefaultLookDetailLevel == detail
            ? ActiveBg
            : (Application.Current?.RequestedTheme == AppTheme.Dark ? ActionInactiveDarkBg : ActionInactiveBg);

    private Color LookVariantTextColor(LookDetailLevel detail) =>
        _settingsService.DefaultLookDetailLevel == detail
            ? ActiveTextColor
            : (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#F5F5F5") : Color.FromArgb("#222222"));

    public ListeningLayer CurrentLayer
    {
        get => _currentLayer;
        set
        {
            if (SetProperty(ref _currentLayer, value))
            {
                OnPropertyChanged(nameof(StateColor));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CanAct));
                OnPropertyChanged(nameof(SelectedStateText));
                OnPropertyChanged(nameof(SleepChipText));
                OnPropertyChanged(nameof(ListenChipText));
                OnPropertyChanged(nameof(ActiveChipText));
                OnPropertyChanged(nameof(OffSegmentColor));
                OnPropertyChanged(nameof(OffSegmentTextColor));
                OnPropertyChanged(nameof(OnSegmentColor));
                OnPropertyChanged(nameof(OnSegmentTextColor));
                OnPropertyChanged(nameof(ListeningSegmentColor));
                OnPropertyChanged(nameof(ListeningSegmentTextColor));
                OnPropertyChanged(nameof(OffIcon));
                OnPropertyChanged(nameof(OnIcon));
                OnPropertyChanged(nameof(ListeningIcon));
            }
        }
    }

    public bool CanAct => CurrentLayer != ListeningLayer.Sleep;

    public bool ShowTranscriptTab
    {
        get => _showTranscriptTab;
        set
        {
            if (SetProperty(ref _showTranscriptTab, value))
            {
                OnPropertyChanged(nameof(ShowCameraTab));
                OnPropertyChanged(nameof(TranscriptTabBackground));
                OnPropertyChanged(nameof(TranscriptTabTextColor));
                OnPropertyChanged(nameof(CameraTabBackground));
                OnPropertyChanged(nameof(CameraTabTextColor));
            }
        }
    }
    public bool ShowCameraTab => !ShowTranscriptTab;

    public bool ShowInlineCameraPreview
    {
        get => _showInlineCameraPreview;
        set
        {
            if (SetProperty(ref _showInlineCameraPreview, value))
            {
                OnPropertyChanged(nameof(ShowCameraActionRail));
                OnPropertyChanged(nameof(ShowCameraActionsSection));
            }
        }
    }

    public bool IsActionsDrawerExpanded
    {
        get => _isActionsDrawerExpanded;
        set
        {
            if (SetProperty(ref _isActionsDrawerExpanded, value))
            {
                OnPropertyChanged(nameof(ActionsDrawerButtonText));
                OnPropertyChanged(nameof(ActionsDrawerSemanticDescription));
            }
        }
    }

    public string ActionsDrawerButtonText => IsActionsDrawerExpanded ? "Actions open" : "Actions";

    public string ActionsDrawerSemanticDescription =>
        IsActionsDrawerExpanded ? "Actions menu expanded" : "Actions menu collapsed";

    public string MessageText
    {
        get => _messageText;
        set => SetProperty(ref _messageText, value);
    }

    public string OutputMode
    {
        get => _outputMode;
        private set
        {
            if (SetProperty(ref _outputMode, value))
            {
                OnPropertyChanged(nameof(SpeakChipText));
                OnPropertyChanged(nameof(SilentChipText));
                OnPropertyChanged(nameof(SpeakSegmentColor));
                OnPropertyChanged(nameof(SpeakSegmentTextColor));
                OnPropertyChanged(nameof(SilentSegmentColor));
                OnPropertyChanged(nameof(SilentSegmentTextColor));
            }
        }
    }

    private static readonly Color TabActiveBg = Color.FromArgb("#1976D2");
    private static readonly Color TabActiveTxt = Colors.White;
    private static readonly Color TabInactiveTxt = Color.FromArgb("#333333");

    public Color TranscriptTabBackground => ShowTranscriptTab ? TabActiveBg : Colors.Transparent;
    public Color TranscriptTabTextColor => ShowTranscriptTab ? TabActiveTxt : TabInactiveTxt;
    public Color CameraTabBackground => ShowCameraTab ? TabActiveBg : Colors.Transparent;
    public Color CameraTabTextColor => ShowCameraTab ? TabActiveTxt : TabInactiveTxt;

    public ImageSource? SnapshotImage
    {
        get => _snapshotImage;
        set => SetProperty(ref _snapshotImage, value);
    }
    public string? SnapshotCaption
    {
        get => _snapshotCaption;
        set => SetProperty(ref _snapshotCaption, value);
    }
    public bool ShowSnapshot
    {
        get => _showSnapshot;
        set
        {
            if (SetProperty(ref _showSnapshot, value))
                OnPropertyChanged(nameof(ShowCameraActionsSection));
        }
    }

    // --- Scan result overlay ---
    public bool ShowScanResult
    {
        get => _showScanResult;
        set => SetProperty(ref _showScanResult, value);
    }
    public string ScanResultIcon
    {
        get => _scanResultIcon;
        set => SetProperty(ref _scanResultIcon, value);
    }
    public string ScanResultTitle
    {
        get => _scanResultTitle;
        set => SetProperty(ref _scanResultTitle, value);
    }
    public string ScanResultSummary
    {
        get => _scanResultSummary;
        set => SetProperty(ref _scanResultSummary, value);
    }
    public ObservableCollection<ContentAction> ScanActions { get; } = [];

    // --- Glasses status for shell widget (M33 Phase 7 Wave 3) ---
    public bool GlassesConnected => _glasses.State == GlassesConnectionState.Connected;
    public int GlassesBatteryPct => _glasses.Battery?.Percentage ?? 0;
    public bool GlassesCharging => _glasses.Battery?.IsCharging ?? false;
    public Color GlassesBatteryColor =>
        (!GlassesCharging && GlassesBatteryPct <= 15)
            ? Colors.Red
            : Colors.White;

    public AsyncRelayCommand NavigateToGlassesCommand { get; }
    public AsyncRelayCommand NavigateToSettingsCommand { get; }

    private void RefreshGlasses()
    {
        OnPropertyChanged(nameof(GlassesConnected));
        OnPropertyChanged(nameof(GlassesBatteryPct));
        OnPropertyChanged(nameof(GlassesCharging));
        OnPropertyChanged(nameof(GlassesBatteryColor));
    }

    public Color StateColor => CurrentLayer switch
    {
        ListeningLayer.Sleep => Color.FromArgb("#666666"),
        ListeningLayer.WakeWord => Color.FromArgb("#4CAF50"),
        ListeningLayer.ActiveSession => Color.FromArgb("#2196F3"),
        _ => Color.FromArgb("#666666")
    };

    public Color OffSegmentColor => CurrentLayer == ListeningLayer.Sleep ? ActiveBg : InactiveBg;
    public Color OnSegmentColor => CurrentLayer == ListeningLayer.WakeWord ? ActiveBg : InactiveBg;
    public Color ListeningSegmentColor => CurrentLayer == ListeningLayer.ActiveSession ? ActiveBg : InactiveBg;
    public Color OffSegmentTextColor => CurrentLayer == ListeningLayer.Sleep ? ActiveTextColor : InactiveTextColor;
    public Color OnSegmentTextColor => CurrentLayer == ListeningLayer.WakeWord ? ActiveTextColor : InactiveTextColor;
    public Color ListeningSegmentTextColor => CurrentLayer == ListeningLayer.ActiveSession ? ActiveTextColor : InactiveTextColor;

    public string OffIcon => CurrentLayer == ListeningLayer.Sleep ? "mic_off_w.png" : "mic_off.png";
    public string OnIcon => CurrentLayer == ListeningLayer.WakeWord ? "mic_on_w.png" : "mic_on.png";
    public string ListeningIcon => CurrentLayer == ListeningLayer.ActiveSession ? "mic_active_w.png" : "mic_active.png";

    public string SelectedStateText => CurrentLayer switch
    {
        ListeningLayer.Sleep => "Sleep",
        ListeningLayer.WakeWord => "Listen",
        ListeningLayer.ActiveSession => "Active",
        _ => "Sleep"
    };

    public string SleepChipText =>
        CurrentLayer == ListeningLayer.Sleep ? "Sleep selected" : "Sleep";

    public string ListenChipText =>
        CurrentLayer == ListeningLayer.WakeWord ? "Listen selected" : "Listen";

    public string ActiveChipText =>
        CurrentLayer == ListeningLayer.ActiveSession ? "Active selected" : "Active";

    public string SpeakChipText =>
        OutputMode == OutputModes.Speak ? "Speak selected" : "Speak";

    public string SilentChipText =>
        OutputMode == OutputModes.Silent ? "Silent selected" : "Silent";

    public Color SpeakSegmentColor => OutputMode == OutputModes.Speak ? ActiveBg : InactiveBg;
    public Color SilentSegmentColor => OutputMode == OutputModes.Silent ? ActiveBg : InactiveBg;
    public Color SpeakSegmentTextColor => OutputMode == OutputModes.Speak ? ActiveTextColor : InactiveTextColor;
    public Color SilentSegmentTextColor => OutputMode == OutputModes.Silent ? ActiveTextColor : InactiveTextColor;

    private async Task ToggleAsync()
    {
        DebugVisible = _settingsService.DebugMode;

        if (_sessionCoordinator is not null)
        {
            await SetLayerWithCoordinatorAsync(IsRunning ? SessionLayer.Sleep : SessionLayer.ActiveSession);
            return;
        }

        if (IsRunning)
            await SetLayerAsync("Off");
        else
            await SetLayerAsync("Listening");
    }

    private async Task SetLayerAsync(string segment)
    {
        if (_sessionCoordinator is not null)
        {
            await SetLayerWithCoordinatorAsync(ParseSessionLayer(segment));
            return;
        }

        if (_isTransitioning)
        {
            _logger.LogWarning("SetLayerAsync({Segment}) skipped — already transitioning", segment);
            return;
        }
        _isTransitioning = true;
        _logger.LogInformation("SetLayerAsync({Segment}) from {Current}", segment, CurrentLayer);
        try
        {
            var target = segment switch
            {
                "Off" => ListeningLayer.Sleep,
                "On" => ListeningLayer.WakeWord,
                "Listening" => ListeningLayer.ActiveSession,
                // Legacy support
                "Sleep" => ListeningLayer.Sleep,
                "Listen" => ListeningLayer.WakeWord,
                "Active" => ListeningLayer.ActiveSession,
                _ => CurrentLayer
            };

            if (target == ListeningLayer.Sleep)
            {
                ShowInlineCameraPreview = false;
                _cameraView?.StopCameraPreview();
            }

            if (target == CurrentLayer) return;

            // De-escalate
            if (target < CurrentLayer)
            {
                if (CurrentLayer == ListeningLayer.ActiveSession)
                {
                    _cameraView?.StopCameraPreview();
                    VisionStatus = null;
                    await _orchestrator.StopAsync();
                    IsRunning = false;
                    ToggleButtonText = "Start";
                }
            }

            // Escalate to Active
            if (target == ListeningLayer.ActiveSession && CurrentLayer < ListeningLayer.ActiveSession)
            {
                try
                {
                    var key = await _apiKeyService.GetApiKeyAsync();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        key = await PromptForApiKeyAsync();
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            StatusText = "API key required";
                            return;
                        }
                        await _apiKeyService.SetApiKeyAsync(key);
                    }

                    ToggleButtonText = "Stop";
                    StatusText = "Connecting...";

                    await _orchestrator.StartAsync();
                    _orchestrator.FrameCaptureFunc = CaptureFrameFromCameraViewAsync;
                    _logger.LogInformation("Orchestrator started, CameraView={HasCamera}", _cameraView is not null);

                    if (_cameraView is not null)
                        await _cameraView.StartCameraPreview(CancellationToken.None);

                    IsRunning = true;
                    _logger.LogInformation("Active session ready");
                }
                catch (Exception ex)
                {
                    IsRunning = false;
                    ToggleButtonText = "Start";
                    DebugLog += $"[{DateTime.Now:HH:mm:ss}] Start failed: {ex.Message}{Environment.NewLine}";
                    return;
                }
            }

            CurrentLayer = target;

            StatusText = target switch
            {
                ListeningLayer.Sleep => "Sleeping",
                ListeningLayer.WakeWord => "Listening...",
                ListeningLayer.ActiveSession => "Active",
                _ => "Ready"
            };
        }
        finally { _isTransitioning = false; }
    }

    private async Task SetLayerWithCoordinatorAsync(SessionLayer target)
    {
        if (_isTransitioning)
        {
            _logger.LogWarning("Session transition to {Target} skipped — already transitioning", target);
            return;
        }

        _isTransitioning = true;
        try
        {
            if (target == SessionLayer.Sleep)
            {
                ShowInlineCameraPreview = false;
                _cameraView?.StopCameraPreview();
            }

            if (target < ToSessionLayer(CurrentLayer) && CurrentLayer == ListeningLayer.ActiveSession)
            {
                _cameraView?.StopCameraPreview();
                VisionStatus = null;
            }

            if (target == SessionLayer.ActiveSession)
            {
                ToggleButtonText = "Stop";
                StatusText = "Connecting...";
            }

            var result = await _sessionCoordinator!.SetLayerAsync(
                target,
                new SessionTransitionOptions(
                    PromptForApiKeyAsync,
                    CaptureFrameFromCameraViewAsync));

            ApplySessionTransitionResult(result);

            if (result.Success && result.CurrentLayer == SessionLayer.ActiveSession)
            {
                _logger.LogInformation("Active session ready, CameraView={HasCamera}", _cameraView is not null);
                if (_cameraView is not null)
                    await _cameraView.StartCameraPreview(CancellationToken.None);
            }

            if (!result.Success && !string.IsNullOrWhiteSpace(result.Error)
                && !string.Equals(result.Error, "API key required", StringComparison.OrdinalIgnoreCase))
            {
                DebugLog += $"[{DateTime.Now:HH:mm:ss}] {result.Error}{Environment.NewLine}";
            }
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    private void OnSessionCoordinatorStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
            ApplySessionTransitionResult(new SessionTransitionResult(
                Success: true,
                e.Layer,
                e.IsRunning,
                e.StatusText,
                e.ToggleButtonText)));
    }

    private void ApplySessionTransitionResult(SessionTransitionResult result)
    {
        CurrentLayer = ToListeningLayer(result.CurrentLayer);
        IsRunning = result.IsRunning;
        ToggleButtonText = result.ToggleButtonText;
        StatusText = result.StatusText;
    }

    private static SessionLayer ParseSessionLayer(string segment) =>
        segment switch
        {
            "Off" or "Sleep" => SessionLayer.Sleep,
            "On" or "Listen" => SessionLayer.WakeWord,
            "Listening" or "Active" => SessionLayer.ActiveSession,
            _ => SessionLayer.Sleep,
        };

    private static SessionLayer ToSessionLayer(ListeningLayer layer) =>
        layer switch
        {
            ListeningLayer.Sleep => SessionLayer.Sleep,
            ListeningLayer.WakeWord => SessionLayer.WakeWord,
            ListeningLayer.ActiveSession => SessionLayer.ActiveSession,
            _ => SessionLayer.Sleep,
        };

    private static ListeningLayer ToListeningLayer(SessionLayer layer) =>
        layer switch
        {
            SessionLayer.Sleep => ListeningLayer.Sleep,
            SessionLayer.WakeWord => ListeningLayer.WakeWord,
            SessionLayer.ActiveSession => ListeningLayer.ActiveSession,
            _ => ListeningLayer.Sleep,
        };

    private async Task SetOutputModeAsync(string outputMode)
    {
        var normalized = OutputModes.Normalize(outputMode);
        if (OutputMode == normalized)
            return;

        OutputMode = normalized;
        _settingsService.OutputMode = normalized;
        _audioPolicy?.Recompute();

        if (normalized == OutputModes.Silent)
        {
            if (_sessionCoordinator is not null)
                await _sessionCoordinator.StopSpeakingAsync();
            else
                await _orchestrator.StopSpeakingAsync();
        }
    }

    private async Task RevealInlineCameraPreviewAsync()
    {
        ShowInlineCameraPreview = true;

        if (_cameraManager.Active is { ProviderId: "phone" } phoneProvider)
        {
            try
            {
                await phoneProvider.StartAsync(CancellationToken.None);
                if (phoneProvider is not PhoneCameraProvider phoneCameraProvider
                    || phoneCameraProvider.IsStarted)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to start phone camera provider for inline preview");
            }
        }

        if (_cameraView is null)
            return;

        try
        {
            await _cameraView.StartCameraPreview(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to start inline camera preview");
        }
    }

    private void DispatchOnMainThreadAsync(Func<Task> action)
    {
        try
        {
            if (Application.Current is null || MainThread.IsMainThread)
            {
                _ = action();
                return;
            }

            MainThread.BeginInvokeOnMainThread(async () => await action());
        }
        catch
        {
            _ = action();
        }
    }

    public async Task HandleButtonActionAsync(ButtonActionEvent action)
    {
        if (_assistiveActions is not null)
        {
            await ExecuteAssistiveButtonActionAsync(action);
            return;
        }

        switch (action.Action)
        {
            case ButtonAction.Look:
                await ExecuteCameraCommandAsync("look", CommandTriggerOrigin.PhysicalButton);
                break;
            case ButtonAction.Read:
                await ExecuteCameraCommandAsync("read", CommandTriggerOrigin.PhysicalButton);
                break;
            case ButtonAction.Photo:
                if (await CompletePendingManualCaptureAsync())
                    return;

                await SendVisionCommandAsync("Take a photo of what you see.");
                break;
            case ButtonAction.ToggleSession:
            case ButtonAction.ToggleConversation:
            case ButtonAction.ToggleSleepActive:
                await ToggleAsync();
                break;
            case ButtonAction.EndSession:
                await SetLayerAsync("Off");
                break;
        }
    }

    private async Task ExecuteAssistiveButtonActionAsync(ButtonActionEvent action)
    {
        if (action.Action == ButtonAction.Photo && await CompletePendingManualCaptureAsync())
            return;

        if (IsSessionStopAction(action.Action))
        {
            ShowInlineCameraPreview = false;
            _cameraView?.StopCameraPreview();
        }

        if (IsSessionStartAction(action.Action) && !IsRunning)
        {
            ToggleButtonText = "Stop";
            StatusText = "Connecting...";
        }

        var aiEntry = CreateBusyEntryForButtonAction(action.Action);
        try
        {
            var result = await _assistiveActions!.ExecuteButtonActionAsync(
                action,
                CreateAssistiveActionContext());

            await ApplyAssistiveActionResultAsync(result, aiEntry);
        }
        catch (OperationCanceledException)
        {
            if (aiEntry is not null)
                CompleteAiBusyVisual(aiEntry, "Command canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Button action {Action} failed", action.Action);
            if (aiEntry is not null)
                CompleteAiBusyVisual(aiEntry, $"Command error: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_activeCommandAiEntry, aiEntry))
                _activeCommandAiEntry = null;
        }
    }

    private TranscriptEntry? CreateBusyEntryForButtonAction(ButtonAction action)
    {
        if (!IsCameraCommandButtonAction(action))
            return null;

        ShowTranscriptTab = true;
        var aiEntry = new TranscriptEntry { Role = "AI", IsThinking = true };
        Entries.Add(aiEntry);
        _activeCommandAiEntry = aiEntry;
        return aiEntry;
    }

    private async Task ApplyAssistiveActionResultAsync(
        AssistiveActionResult result,
        TranscriptEntry? aiEntry)
    {
        if (result.Kind == AssistiveActionResultKind.CameraCommand
            && result.CameraCommandResult is not null
            && aiEntry is not null)
        {
            ApplyCameraCommandResult(result.CameraCommandResult, aiEntry);
            return;
        }

        if (result.Kind == AssistiveActionResultKind.Session)
        {
            if (result.Data is SessionTransitionResult transition)
            {
                ApplySessionTransitionResult(transition);

                if (transition.Success && transition.CurrentLayer == SessionLayer.ActiveSession)
                {
                    _logger.LogInformation("Active session ready, CameraView={HasCamera}", _cameraView is not null);
                    if (_cameraView is not null)
                        await _cameraView.StartCameraPreview(CancellationToken.None);
                }

                if (!transition.Success
                    && !string.IsNullOrWhiteSpace(transition.Error)
                    && !string.Equals(transition.Error, "API key required", StringComparison.OrdinalIgnoreCase))
                {
                    DebugLog += $"[{DateTime.Now:HH:mm:ss}] {transition.Error}{Environment.NewLine}";
                }
            }
            return;
        }

        if (result.Kind == AssistiveActionResultKind.Photo)
        {
            await RevealInlineCameraPreviewAsync();
            await SendVisionCommandAsync("Take a photo of what you see.");
            return;
        }

        if (!result.Success && aiEntry is not null)
            CompleteAiBusyVisual(aiEntry, result.Error ?? "Action failed.");
    }

    private AssistiveActionContext CreateAssistiveActionContext() =>
        new(PromptForApiKeyAsync, CaptureFrameFromCameraViewAsync);

    private static bool IsCameraCommandButtonAction(ButtonAction action) =>
        action is ButtonAction.Look or ButtonAction.Read or ButtonAction.Find;

    private bool IsSessionStartAction(ButtonAction action) =>
        action is ButtonAction.ToggleSession or ButtonAction.ToggleConversation or ButtonAction.ToggleSleepActive
        && !IsRunning;

    private static bool IsSessionStopAction(ButtonAction action) =>
        action is ButtonAction.EndSession
            or ButtonAction.ToggleSession
            or ButtonAction.ToggleConversation
            or ButtonAction.ToggleSleepActive;

    internal async Task SelectCameraActionFromUiAsync(string actionId, Func<Task> fallbackAsync)
    {
        IsActionsDrawerExpanded = false;

        var action = CameraActions.FirstOrDefault(item =>
            string.Equals(item.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        if (action is not null)
        {
            await ActivateCameraActionAsync(action);
            return;
        }

        await fallbackAsync();
    }

    private Task ExecuteLookCommandAsync(LookDetailLevel detail) =>
        ExecuteCameraCommandAsync(
            "look",
            CommandTriggerOrigin.ActionsDrawer,
            new LookCommandOptions(detail, Focus: null, Question: null));

    private void InitializeCameraActions()
    {
        if (_cameraCommandRegistry is null)
            return;

        var actionDescriptors = GetCameraActionDescriptors();
        foreach (var descriptor in OrderCameraActionDescriptors(actionDescriptors))
        {
            if (string.IsNullOrWhiteSpace(descriptor.CameraCommandId)
                || !_cameraCommandRegistry.TryGet(descriptor.CameraCommandId, out var command)
                || !command.Capabilities.RequiresStillFrame)
            {
                continue;
            }

            var variants = BuildCameraActionVariants(descriptor, command);
            if (variants.Count == 0)
                continue;

            CameraActions.Add(new CameraActionItemViewModel(
                descriptor.Id,
                command.Id,
                descriptor.DisplayName,
                variants,
                ActivateCameraActionAsync));
        }

        OnPropertyChanged(nameof(HasCameraActions));
        OnPropertyChanged(nameof(ShowCameraActionRail));
        OnPropertyChanged(nameof(ShowCameraActionsSection));
    }

    private IReadOnlyList<AssistiveActionDescriptor> GetCameraActionDescriptors()
    {
        if (_assistiveActionRegistry is not null)
        {
            return _assistiveActionRegistry.Actions
                .Where(action =>
                    action.RequiresCamera
                    && !action.StartsOrStopsSession
                    && !string.IsNullOrWhiteSpace(action.CameraCommandId))
                .ToArray();
        }

        if (_cameraCommandRegistry is null)
            return [];

        return _cameraCommandRegistry.Commands
            .Where(command => command.Capabilities.RequiresStillFrame)
            .Select(command => new AssistiveActionDescriptor(
                ToCameraActionId(command.Id),
                command.DisplayName,
                RequiresCamera: true,
                StartsOrStopsSession: false,
                CameraCommandId: command.Id))
            .ToArray();
    }

    private IReadOnlyList<AssistiveActionDescriptor> OrderCameraActionDescriptors(
        IReadOnlyList<AssistiveActionDescriptor> descriptors)
    {
        if (_cameraCommandRegistry is null || descriptors.Count == 0)
            return descriptors;

        var commandOrder = _cameraCommandRegistry.Commands
            .Select((command, index) => new { command.Id, Index = index })
            .ToDictionary(item => item.Id, item => item.Index, StringComparer.OrdinalIgnoreCase);

        return descriptors
            .OrderBy(descriptor =>
                descriptor.CameraCommandId is not null
                && commandOrder.TryGetValue(descriptor.CameraCommandId, out var index)
                    ? index
                    : int.MaxValue)
            .ThenBy(descriptor => IsCanonicalCameraAction(descriptor) ? 0 : 1)
            .ThenBy(descriptor => descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<CameraActionVariantViewModel> BuildCameraActionVariants(
        AssistiveActionDescriptor descriptor,
        ICameraCommand command)
    {
        IReadOnlyList<CameraActionVariantDefinition> definitions;
        if (command is ICameraActionVariantProvider variantProvider
            && variantProvider.CameraActionVariants.Count > 0)
        {
            definitions = variantProvider.CameraActionVariants;
        }
        else if (command is ICommandPromptProvider promptProvider
            && promptProvider.PromptDefinitions.Count > 0)
        {
            definitions = promptProvider.PromptDefinitions
                .Select(prompt => ToActionVariantDefinition(command, prompt))
                .ToArray();
        }
        else
        {
            definitions =
            [
                new(
                    "Default",
                    command.DisplayName,
                    command.DisplayName,
                    IsDefault: true)
            ];
        }

        return definitions
            .Select(definition => ApplyActionDefaults(definition, descriptor))
            .Select(definition => new CameraActionVariantViewModel(
                descriptor.Id,
                command.Id,
                definition,
                variant => ExecuteCameraActionVariantAsync(variant)))
            .ToArray();
    }

    private static CameraActionVariantDefinition ToActionVariantDefinition(
        ICameraCommand command,
        CommandPromptDefinition prompt)
    {
        object? options = null;
        var enumOption = command.Options.FirstOrDefault(option =>
            option.PersistLastSelectedValue && option.ValueType.IsEnum);

        if (enumOption is not null
            && Enum.TryParse(enumOption.ValueType, prompt.Key, ignoreCase: true, out var value))
        {
            options = new Dictionary<string, object?>
            {
                [enumOption.Name] = value?.ToString()
            };
        }

        return new CameraActionVariantDefinition(
            prompt.Key,
            prompt.DisplayName,
            prompt.Text,
            options,
            IsDefault: string.Equals(prompt.Key, enumOption?.DefaultValue?.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private static CameraActionVariantDefinition ApplyActionDefaults(
        CameraActionVariantDefinition definition,
        AssistiveActionDescriptor descriptor)
    {
        if (descriptor.DefaultOptions is null && descriptor.DefaultQuery is null)
            return definition;

        return definition with
        {
            Options = MergeActionOptions(descriptor.DefaultOptions, definition.Options),
            Query = definition.Query ?? descriptor.DefaultQuery
        };
    }

    private static object? MergeActionOptions(object? actionDefaults, object? variantOptions)
    {
        if (actionDefaults is null)
            return variantOptions;
        if (variantOptions is null)
            return actionDefaults;
        if (actionDefaults.GetType() != variantOptions.GetType())
            return variantOptions;

        return TryMergeRecordOptions(actionDefaults, variantOptions) ?? variantOptions;
    }

    private static object? TryMergeRecordOptions(object actionDefaults, object variantOptions)
    {
        var type = actionDefaults.GetType();
        var constructor = type.GetConstructors()
            .OrderByDescending(ctor => ctor.GetParameters().Length)
            .FirstOrDefault();
        if (constructor is null)
            return null;

        var properties = type.GetProperties()
            .Where(property => property.GetMethod is not null)
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);
        var parameters = constructor.GetParameters();
        var args = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            if (!properties.TryGetValue(parameters[i].Name ?? string.Empty, out var property))
                return null;

            var variantValue = property.GetValue(variantOptions);
            args[i] = variantValue ?? property.GetValue(actionDefaults);
        }

        return constructor.Invoke(args);
    }

    private static bool IsCanonicalCameraAction(AssistiveActionDescriptor descriptor) =>
        descriptor.CameraCommandId is not null
        && string.Equals(descriptor.Id, ToCameraActionId(descriptor.CameraCommandId), StringComparison.OrdinalIgnoreCase);

    internal async Task ActivateCameraActionAsync(CameraActionItemViewModel action)
    {
        foreach (var cameraAction in CameraActions)
            cameraAction.IsActive = ReferenceEquals(cameraAction, action);

        ActiveCameraAction = action;
        ActiveCameraActionVariants.Clear();
        foreach (var variant in action.Variants)
            ActiveCameraActionVariants.Add(variant);

        OnPropertyChanged(nameof(HasActiveCameraActionVariants));
        IsActionsDrawerExpanded = false;
        await RevealInlineCameraPreviewAsync();
    }

    internal async Task ExecuteCameraActionVariantAsync(
        CameraActionVariantViewModel variant,
        CancellationToken ct = default)
    {
        if (_isExecutingCameraActionVariant)
            return;

        _isExecutingCameraActionVariant = true;
        OnPropertyChanged(nameof(ShowCameraActionRail));
        TranscriptEntry? aiEntry = null;

        try
        {
            IsActionsDrawerExpanded = false;
            await RevealInlineCameraPreviewAsync();
            ShowTranscriptTab = true;

            var action = CameraActions.FirstOrDefault(item =>
                string.Equals(item.ActionId, variant.ActionId, StringComparison.OrdinalIgnoreCase));
            var actionLabel = action?.Label ?? variant.Label;
            var caption = variant.Caption(actionLabel);
            var frame = await CaptureFrameAndCloseCameraActionSurfaceAsync(ct);

            if (frame is null)
            {
                AddTranscriptEntry(
                    new TranscriptEntry
                    {
                        Role = "AI",
                        Text = "Camera not available or no frame captured."
                    },
                    variant.ActionId,
                    ActionTriggerOrigin.ActionsDrawer);
                return;
            }

            AddCapturedFrameTranscriptEntry(variant, frame, caption);

            aiEntry = new TranscriptEntry { Role = "AI", IsThinking = true };
            Entries.Add(aiEntry);
            _activeCommandAiEntry = aiEntry;
            StartAiBusyVisual(aiEntry);

            if (_cameraCommandRegistry is null
                || !_cameraCommandRegistry.TryGet(variant.CommandId, out var command))
            {
                CompleteAiBusyVisual(
                    aiEntry,
                    $"Camera action '{variant.Label}' is unavailable.",
                    variant.ActionId,
                    ActionTriggerOrigin.ActionsDrawer);
                return;
            }

            var mode = command.Capabilities.SupportsManualAim
                ? CameraCommandMode.ManualAim
                : CameraCommandMode.FullAuto;

            if (!SupportsCameraActionMode(command, mode))
            {
                CompleteAiBusyVisual(
                    aiEntry,
                    $"{command.DisplayName} does not support {mode}.",
                    variant.ActionId,
                    ActionTriggerOrigin.ActionsDrawer);
                return;
            }

            var request = new CameraCommandRequest(
                command.Id,
                mode,
                CommandTriggerOrigin.ActionsDrawer,
                variant.Options,
                variant.Query);

            var context = new CameraCommandContext(
                request,
                mode,
                _cameraManager,
                _settingsService,
                CaptureFrame: _ => Task.FromResult<byte[]?>(frame),
                WaitForManualCapture: _ => Task.FromResult<byte[]?>(frame));

            var result = await command.ExecuteAsync(context, ct);
            ApplyCameraActionVariantResult(result, aiEntry, variant.ActionId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (aiEntry is not null)
                CompleteAiBusyVisual(aiEntry, "Command canceled.", variant.ActionId, ActionTriggerOrigin.ActionsDrawer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Camera action variant {ActionId}/{VariantKey} failed", variant.ActionId, variant.Key);
            if (aiEntry is not null)
                CompleteAiBusyVisual(aiEntry, CameraActionCommandFailedMessage, variant.ActionId, ActionTriggerOrigin.ActionsDrawer);
            else
                AddTranscriptEntry(
                    new TranscriptEntry { Role = "AI", Text = CameraActionCaptureFailedMessage },
                    variant.ActionId,
                    ActionTriggerOrigin.ActionsDrawer);
        }
        finally
        {
            if (ReferenceEquals(_activeCommandAiEntry, aiEntry))
                _activeCommandAiEntry = null;

            _isExecutingCameraActionVariant = false;
            OnPropertyChanged(nameof(ShowCameraActionRail));
        }
    }

    private async Task<byte[]?> CaptureFrameAndCloseCameraActionSurfaceAsync(CancellationToken ct)
    {
        ClearCameraActionSelection();

        try
        {
            return await CaptureFrameForCameraActionAsync(ct);
        }
        finally
        {
            await HideInlineCameraPreviewAfterCameraActionCaptureAsync();
        }
    }

    private async Task<byte[]?> CaptureFrameForCameraActionAsync(CancellationToken ct)
    {
        if (_cameraActionFrameCapture is not null)
            return await _cameraActionFrameCapture(ct);

        if (_cameraView is not null && ShowInlineCameraPreview)
            return await CaptureFrameFromCameraViewAsync(ct);

        return await _cameraManager.CaptureFrameAsync(ct);
    }

    private void CloseCameraActionSurface()
    {
        ClearCameraActionSelection();
        HideInlineCameraPreview();
    }

    private async Task HideInlineCameraPreviewAfterCameraActionCaptureAsync()
    {
        var stoppedByProvider = false;
        if (_cameraManager.Active is { ProviderId: "phone" } phoneProvider)
        {
            try
            {
                var providerWasStarted = phoneProvider is not PhoneCameraProvider phoneCameraProvider
                    || phoneCameraProvider.IsStarted;
                await phoneProvider.StopAsync();
                stoppedByProvider = providerWasStarted;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Unable to stop phone camera provider after camera action capture");
            }
        }

        HideInlineCameraPreview(stopPreview: !stoppedByProvider);
    }

    private void ClearCameraActionSelection()
    {
        foreach (var action in CameraActions)
            action.IsActive = false;

        ActiveCameraAction = null;
        ActiveCameraActionVariants.Clear();
        OnPropertyChanged(nameof(HasActiveCameraActionVariants));
    }

    private void AddCapturedFrameTranscriptEntry(
        CameraActionVariantViewModel variant,
        byte[] frame,
        string caption)
    {
        AddTranscriptEntry(
            new TranscriptEntry
            {
                Role = "You",
                Text = variant.TranscriptText,
                Image = CreateImageSource(frame),
                ImageCaption = caption
            },
            variant.ActionId,
            ActionTriggerOrigin.ActionsDrawer,
            [new TranscriptMediaReference(
                "image",
                caption,
                "image/jpeg",
                ByteLength: frame.Length)]);
    }

    private void ApplyCameraActionVariantResult(
        CameraCommandResult result,
        TranscriptEntry aiEntry,
        string actionId)
    {
        CompleteAiBusyVisual(
            aiEntry,
            result.TranscriptText,
            actionId,
            ActionTriggerOrigin.ActionsDrawer);

        TryShowScanResult(result);
    }

    private static bool SupportsCameraActionMode(ICameraCommand command, CameraCommandMode mode) =>
        mode switch
        {
            CameraCommandMode.FullAuto => command.Capabilities.SupportsFullAuto,
            CameraCommandMode.ManualAim => command.Capabilities.SupportsManualAim,
            _ => false,
        };

    internal async Task LookupProductFromUiAsync(CancellationToken ct = default)
    {
        ShowTranscriptTab = true;

        var entry = new TranscriptEntry
        {
            Role = "Product",
            Text = "Looking up product...",
            IsThinking = true
        };
        Entries.Add(entry);

        if (_productLookupWorkflow is null)
        {
            CompleteProductLookupEntry(entry, "Product lookup is unavailable.");
            return;
        }

        ProductBarcodeLookupResult result;
        try
        {
            result = await _productLookupWorkflow.LookupAsync(
                _cameraManager.CaptureFrameAsync,
                barcode: null,
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            CompleteProductLookupEntry(entry, "Product lookup canceled.");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Product lookup UI command failed");
            CompleteProductLookupEntry(entry, $"Product lookup error: {ex.Message}");
            return;
        }

        if (!result.Found)
        {
            CompleteProductLookupEntry(entry, ProductLookupTranscriptMessage(result));
            return;
        }

        var product = result.Product!;
        var label = ProductBarcodeLookupWorkflow.ProductDisplayName(product);

        entry.IsThinking = false;
        entry.Text = label;
        entry.IsActionsOnly = true;
        entry.Actions.Clear();
        entry.Actions.Add(new ContentAction
        {
            Label = label,
            Icon = "",
            Command = new AsyncRelayCommand(() => _openProductDetailsAsync(product))
        });
        entry.NotifyActionsChanged();
        TrackTranscriptEntry(entry, AssistiveActionIds.ProductLookup, ActionTriggerOrigin.ActionsDrawer);
    }

    private void CompleteProductLookupEntry(TranscriptEntry entry, string text)
    {
        entry.Actions.Clear();
        entry.NotifyActionsChanged();
        entry.IsActionsOnly = false;
        entry.Text = text;
        entry.IsThinking = false;
        TrackTranscriptEntry(entry, AssistiveActionIds.ProductLookup, ActionTriggerOrigin.ActionsDrawer);
    }

    private static string ProductLookupTranscriptMessage(ProductBarcodeLookupResult result) =>
        result.Status switch
        {
            ProductBarcodeLookupStatus.CameraUnavailable => "Product lookup: camera not available.",
            ProductBarcodeLookupStatus.NoBarcodeDetected => "Product lookup: no product barcode detected.",
            ProductBarcodeLookupStatus.UnsupportedFormat => result.Format is null
                ? "Product lookup: detected a non-product code; use Scan for QR codes."
                : $"Product lookup: detected {result.Format}; use Scan for QR codes.",
            ProductBarcodeLookupStatus.NotFound => result.Barcode is null
                ? "Product not found."
                : $"Product not found: {result.Barcode}",
            ProductBarcodeLookupStatus.Error => string.IsNullOrWhiteSpace(result.Error)
                ? "Product lookup error."
                : $"Product lookup error: {result.Error}",
            _ => result.Message,
        };

    private static Task OpenProductDetailPageAsync(ProductInfo product) =>
        MainThread.InvokeOnMainThreadAsync(() =>
            Shell.Current.GoToAsync(
                Pages.Products.ProductDetailPage.Route,
                new Dictionary<string, object>
                {
                    ["product"] = product
                }));

    private async Task ExecuteCameraCommandAsync(
        string commandId,
        CommandTriggerOrigin origin,
        object? options = null,
        string? query = null,
        CameraCommandMode? mode = null)
    {
        if (_cameraCommands is null)
        {
            await ExecuteLegacyCameraCommandAsync(commandId);
            return;
        }

        ShowTranscriptTab = true;
        var aiEntry = new TranscriptEntry { Role = "AI", IsThinking = true };
        Entries.Add(aiEntry);
        _activeCommandAiEntry = aiEntry;

        try
        {
            var result = await _cameraCommands.ExecuteAsync(
                new CameraCommandRequest(commandId, mode, origin, options, query));

            ApplyCameraCommandResult(result, aiEntry);
        }
        catch (OperationCanceledException)
        {
            CompleteAiBusyVisual(aiEntry, "Command canceled.", ToCameraActionId(commandId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Camera command {CommandId} error", commandId);
            CompleteAiBusyVisual(aiEntry, $"Command error: {ex.Message}", ToCameraActionId(commandId));
        }
        finally
        {
            if (ReferenceEquals(_activeCommandAiEntry, aiEntry))
                _activeCommandAiEntry = null;
        }
    }

    private void ApplyCameraCommandResult(CameraCommandResult result, TranscriptEntry aiEntry)
    {
        AddCommandTranscriptInput(result, aiEntry);
        CompleteAiBusyVisual(aiEntry, result.TranscriptText, ToCameraActionId(result.CommandId));

        TryShowScanResult(result);

        if (WasManualAim(result))
            HideInlineCameraPreview();
    }

    private async Task<bool> CompletePendingManualCaptureAsync()
    {
        if (_manualCapture is null)
            return false;

        if (_isCompletingManualCapture)
            return true;

        _isCompletingManualCapture = true;
        try
        {
            if (!await _manualCapture.CompletePendingCaptureAsync())
                return false;

            HideInlineCameraPreview();
            ShowTranscriptTab = true;
            StartAiBusyVisualIfNeeded();
            return true;
        }
        finally
        {
            _isCompletingManualCapture = false;
            OnPropertyChanged(nameof(ShowManualCaptureButton));
        }
    }

    private void HideInlineCameraPreview(bool stopPreview = true)
    {
        if (stopPreview)
            StopInlineCameraPreview();

        ShowInlineCameraPreview = false;
        ShowSnapshot = false;
        SnapshotImage = null;
        SnapshotCaption = null;
        if (!stopPreview)
            OnPropertyChanged(nameof(ShowManualCaptureButton));
    }

    private void StopInlineCameraPreview()
    {
        var cameraView = _cameraView;
        if (cameraView is null)
            return;

        void Stop()
        {
            try
            {
                if (cameraView.Handler?.PlatformView is null)
                    return;

                cameraView.StopCameraPreview();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Unable to stop inline camera preview");
            }
        }

        if (Application.Current is null || MainThread.IsMainThread)
            Stop();
        else
            MainThread.BeginInvokeOnMainThread(Stop);

        OnPropertyChanged(nameof(ShowManualCaptureButton));
    }

    private void StartAiBusyVisualIfNeeded()
    {
        if (_activeCommandAiEntry is not { IsThinking: true } aiEntry)
            return;

        StartAiBusyVisual(aiEntry);
    }

    private void StartAiBusyVisual(TranscriptEntry aiEntry)
    {
        StopAiBusyAnimation();
        aiEntry.IsThinking = true;

        var cts = new CancellationTokenSource();
        _aiBusyAnimationCts = cts;
        _ = AnimateAiBusyVisualAsync(aiEntry, cts);
    }

    private async Task AnimateAiBusyVisualAsync(TranscriptEntry aiEntry, CancellationTokenSource cts)
    {
        var ct = cts.Token;
        string[] frames = [".", "..", "..."];
        var index = 0;

        try
        {
            while (!ct.IsCancellationRequested && aiEntry.IsThinking)
            {
                aiEntry.Text = frames[index % frames.Length];
                index++;
                await Task.Delay(450, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private void CompleteAiBusyVisual(
        TranscriptEntry aiEntry,
        string text,
        string? actionId = null,
        ActionTriggerOrigin? triggerOrigin = null)
    {
        StopAiBusyAnimation();
        aiEntry.Text = text;
        aiEntry.IsThinking = false;
        TrackTranscriptEntry(aiEntry, actionId, triggerOrigin);
    }

    private void StopAiBusyAnimation()
    {
        var cts = _aiBusyAnimationCts;
        if (cts is null)
            return;

        _aiBusyAnimationCts = null;
        cts.Cancel();
    }

    private void AddCommandTranscriptInput(CameraCommandResult result, TranscriptEntry aiEntry)
    {
        var input = result.TranscriptInput;
        if (input is null)
            return;

        var userEntry = new TranscriptEntry
        {
            Role = "You",
            Text = input.Text,
            Image = CreateImageSource(input.ImageBytes),
            ImageCaption = input.ImageCaption
        };

        var aiIndex = Entries.IndexOf(aiEntry);
        if (aiIndex >= 0)
            Entries.Insert(aiIndex, userEntry);
        else
            Entries.Add(userEntry);

        TrackTranscriptEntry(
            userEntry,
            ToCameraActionId(result.CommandId),
            null,
            input.ImageBytes is null
                ? []
                : [new TranscriptMediaReference(
                    "image",
                    input.ImageCaption,
                    "image/jpeg",
                    ByteLength: input.ImageBytes.Length)]);
    }

    private void AddTranscriptEntry(
        TranscriptEntry entry,
        string? actionId = null,
        ActionTriggerOrigin? triggerOrigin = null,
        IReadOnlyList<TranscriptMediaReference>? media = null)
    {
        Entries.Add(entry);
        TrackTranscriptEntry(entry, actionId, triggerOrigin, media);
    }

    private void TrackTranscriptEntry(
        TranscriptEntry entry,
        string? actionId = null,
        ActionTriggerOrigin? triggerOrigin = null,
        IReadOnlyList<TranscriptMediaReference>? media = null)
    {
        if (_transcriptStore is null || entry.IsThinking || string.IsNullOrWhiteSpace(entry.Text))
            return;

        var record = new TranscriptRecord(
            _uiSessionId,
            DateTimeOffset.UtcNow,
            entry.Role,
            entry.Text,
            media ?? [],
            actionId,
            triggerOrigin,
            SourceProfileId: _settingsService.DeviceSettings.ActiveProfileId,
            ProviderId: _settingsService.ProviderId,
            ModelId: _settingsService.ChatModel);

        _ = _transcriptStore.AppendAsync(record);
    }

    private async Task ClearTranscriptSessionAsync()
    {
        var sessionId = _uiSessionId;
        _uiSessionId = Guid.NewGuid().ToString("N");

        if (_transcriptStore is not null)
            await _transcriptStore.ClearSessionAsync(sessionId);
    }

    private static string ToCameraActionId(string commandId) =>
        commandId switch
        {
            "look" => AssistiveActionIds.Look,
            "read" => AssistiveActionIds.Read,
            "scan" => AssistiveActionIds.Scan,
            _ => $"camera.{commandId}",
        };

    private static ImageSource? CreateImageSource(byte[]? imageBytes)
    {
        if (imageBytes is null)
            return null;

        try
        {
            return ImageSource.FromStream(() => new MemoryStream(imageBytes));
        }
        catch
        {
            return null;
        }
    }

    private async Task ExecuteLegacyCameraCommandAsync(string commandId)
    {
        switch (commandId)
        {
            case "read":
                await ReadTextAsync();
                break;
            case "scan":
                await ScanAsync();
                break;
            default:
                await SendVisionCommandAsync("Describe what you see in front of me.");
                break;
        }
    }

    private void TryShowScanResult(CameraCommandResult result)
    {
        if (!string.Equals(result.CommandId, "scan", StringComparison.OrdinalIgnoreCase)
            || result.Data is not IReadOnlyDictionary<string, object?> data
            || !TryGetBool(data, "found"))
        {
            return;
        }

        if (!TryGetString(data, "content", out var content))
            return;

        var handler = _contentResolver.Resolve(content);
        var parsed = handler.Parse(content);
        ShowScanResultCard(handler, parsed, content);
    }

    private static bool WasManualAim(CameraCommandResult result) =>
        result.Data is IReadOnlyDictionary<string, object?> data
        && TryGetString(data, "mode", out var mode)
        && string.Equals(mode, CameraCommandMode.ManualAim.ToString(), StringComparison.OrdinalIgnoreCase);

    private static bool TryGetBool(IReadOnlyDictionary<string, object?> data, string key)
    {
        return data.TryGetValue(key, out var value) && value switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var parsed) && parsed,
            _ => false,
        };
    }

    private static bool TryGetString(
        IReadOnlyDictionary<string, object?> data,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!data.TryGetValue(key, out var raw) || raw is null)
            return false;

        value = raw.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private async Task ScanAsync()
    {
        var frame = await _cameraManager.CaptureFrameAsync();
        if (frame is null)
        {
            AddTranscriptEntry(new TranscriptEntry { Role = "AI", Text = "Camera not available or no frame captured." });
            return;
        }

        var aiEntry = new TranscriptEntry { Role = "AI", IsThinking = true };
        Entries.Add(aiEntry);

        try
        {
            var result = await _qrScanner.ScanAsync(frame);
            if (result is null)
            {
                aiEntry.IsThinking = false;
                aiEntry.Text = "No QR code or barcode detected.";
                TrackTranscriptEntry(aiEntry, AssistiveActionIds.Scan);
                return;
            }

            _qrCodeService.Add(result);

            var handler = _contentResolver.Resolve(result.Content);
            var parsed = handler.Parse(result.Content);

            aiEntry.IsThinking = false;
            aiEntry.Text = $"{handler.Icon} {handler.DisplayName}: {handler.Summarize(parsed)}";
            TrackTranscriptEntry(aiEntry, AssistiveActionIds.Scan);

            ShowScanResultCard(handler, parsed, result.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QR scan error");
            aiEntry.IsThinking = false;
            aiEntry.Text = $"Scan error: {ex.Message}";
            TrackTranscriptEntry(aiEntry, AssistiveActionIds.Scan);
        }
    }

    private async Task ReadTextAsync()
    {
        var frame = await _cameraManager.CaptureFrameAsync();
        if (frame is null)
        {
            AddTranscriptEntry(new TranscriptEntry { Role = "AI", Text = "Camera not available or no frame captured." });
            return;
        }

        var aiEntry = new TranscriptEntry { Role = "AI", IsThinking = true };
        Entries.Add(aiEntry);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var text = await _orchestrator.Vision.DescribeFrameAsync(
                frame,
                "Extract all visible text from this image. Return ONLY the text you can read, nothing else. If no text is visible, respond with exactly: NO_TEXT",
                cts.Token);

            aiEntry.IsThinking = false;

            if (string.IsNullOrWhiteSpace(text)
                || text.Contains("NO_TEXT", StringComparison.OrdinalIgnoreCase))
            {
                aiEntry.Text = "No text detected.";
            }
            else
            {
                aiEntry.Text = text;
            }

            TrackTranscriptEntry(aiEntry, AssistiveActionIds.Read);
        }
        catch (OperationCanceledException)
        {
            aiEntry.IsThinking = false;
            aiEntry.Text = "Text reading timed out. Check your network connection and try again.";
            TrackTranscriptEntry(aiEntry, AssistiveActionIds.Read);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Text reading error");
            aiEntry.IsThinking = false;
            aiEntry.Text = $"Read error: {ex.Message}";
            TrackTranscriptEntry(aiEntry, AssistiveActionIds.Read);
        }
    }

    private async Task SendVisionCommandAsync(string prompt)
    {
        // Option B: Session is running — send through Realtime API (spoken aloud)
        if (IsRunning)
        {
            _currentAiEntry = new TranscriptEntry { Role = "AI", IsThinking = true };
            Entries.Add(_currentAiEntry);

            if (_sessionCoordinator is not null)
                await _sessionCoordinator.SendTextInputAsync(prompt);
            else
                await _orchestrator.SendTextInputAsync(prompt);
            return;
        }

        // Option A: No session — capture frame directly, call VisionAgent, show in transcript (no voice)
        var frame = await _cameraManager.CaptureFrameAsync();

        AddTranscriptEntry(new TranscriptEntry
        {
            Role = "You",
            Text = prompt,
            Image = CreateImageSource(frame),
            ImageCaption = "Captured frame"
        },
        media: frame is null
            ? []
            : [new TranscriptMediaReference(
                "image",
                "Captured frame",
                "image/jpeg",
                ByteLength: frame.Length)]);

        if (frame is null)
        {
            AddTranscriptEntry(new TranscriptEntry { Role = "AI", Text = "Camera not available or no frame captured." });
            return;
        }

        var aiEntry = new TranscriptEntry { Role = "AI", IsThinking = true };
        Entries.Add(aiEntry);

        ShowTranscriptTab = true;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var description = await _orchestrator.Vision.DescribeFrameAsync(frame, prompt, cts.Token);
            aiEntry.IsThinking = false;
            aiEntry.Text = description;
            TrackTranscriptEntry(aiEntry, AssistiveActionIds.Look);
        }
        catch (OperationCanceledException)
        {
            aiEntry.IsThinking = false;
            aiEntry.Text = "Vision request timed out. Check your network connection and try again.";
            TrackTranscriptEntry(aiEntry, AssistiveActionIds.Look);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vision API error");
            aiEntry.IsThinking = false;
            aiEntry.Text = $"Vision error: {ex.Message}";
            TrackTranscriptEntry(aiEntry, AssistiveActionIds.Look);
        }
    }

    private async Task SendMessageAsync()
    {
        var message = MessageText.Trim();
        if (string.IsNullOrWhiteSpace(message))
            return;

        MessageText = string.Empty;
        ShowTranscriptTab = true;
        AddTranscriptEntry(new TranscriptEntry { Role = "You", Text = message });

        if (!IsRunning)
        {
            AddTranscriptEntry(new TranscriptEntry
            {
                Role = "AI",
                Text = "Switch to Active mode to send a live message."
            });
            return;
        }

        _currentAiEntry = new TranscriptEntry { Role = "AI", IsThinking = true };
        Entries.Add(_currentAiEntry);

        try
        {
            if (_sessionCoordinator is not null)
                await _sessionCoordinator.SendTextInputAsync(message);
            else
                await _orchestrator.SendTextInputAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Typed message send failed");
            if (_currentAiEntry is not null)
            {
                _currentAiEntry.IsThinking = false;
                _currentAiEntry.Text = $"Send error: {ex.Message}";
                _currentAiEntry = null;
            }
        }
    }

    /// <summary>
    /// Extracts readable text from a tool's JSON result.
    /// Looks for common fields: description, text, analysis, error.
    /// </summary>
    internal static string ExtractToolResultText(string resultJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(resultJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
                return err.GetString() ?? "Unknown error";

            if (root.TryGetProperty("description", out var desc))
                return desc.GetString() ?? string.Empty;

            if (root.TryGetProperty("text", out var txt))
                return txt.GetString() ?? string.Empty;

            if (root.TryGetProperty("analysis", out var analysis))
                return analysis.GetString() ?? string.Empty;

            return resultJson;
        }
        catch
        {
            return resultJson;
        }
    }

    private static async Task<string?> PromptForApiKeyAsync()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return null;

        return await page.DisplayPromptAsync(
            "API Key Required",
            "Enter your OpenAI or Azure OpenAI API key:",
            placeholder: "sk-proj-... or Azure key",
            maxLength: 200,
            keyboard: Keyboard.Text);
    }

    public void SetCameraView(CameraView cameraView)
    {
        _cameraView = cameraView;
    }

    /// <summary>
    /// Captures a JPEG frame from the CameraView for the vision API.
    /// </summary>
    internal async Task<byte[]?> CaptureFrameFromCameraViewAsync(CancellationToken ct = default)
    {
        var cameraView = _cameraView;
        if (cameraView is null) return null;

        try
        {
            if (!await EnsureCameraPreviewReadyForCaptureAsync(cameraView, ct))
                return null;

            var tcs = new TaskCompletionSource<byte[]?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void OnMediaCaptured(object? s, CommunityToolkit.Maui.Core.MediaCapturedEventArgs e)
            {
                try
                {
                    if (e.Media is null || e.Media.Length == 0)
                    {
                        tcs.TrySetResult(null);
                        return;
                    }

                    using var ms = new MemoryStream();
                    e.Media.CopyTo(ms);
                    tcs.TrySetResult(ms.ToArray());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
                cameraView.MediaCaptured += OnMediaCaptured);
            try
            {
                if (!await WaitForCameraPlatformViewAsync(cameraView, ct))
                    return null;

                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await cameraView.CaptureImage(ct));

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                using var registration = timeoutCts.Token.Register(() => tcs.TrySetResult(null));

                return await tcs.Task;
            }
            finally
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                    cameraView.MediaCaptured -= OnMediaCaptured);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to capture frame from inline camera preview");
            return null;
        }
    }

    private async Task<bool> EnsureCameraPreviewReadyForCaptureAsync(
        CameraView cameraView,
        CancellationToken ct)
    {
        if (!await WaitForCameraPlatformViewAsync(cameraView, ct))
            return false;

        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await cameraView.StartCameraPreview(ct));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to ensure inline camera preview is started before capture");
        }

        return await WaitForCameraPlatformViewAsync(cameraView, ct);
    }

    private async Task<bool> WaitForCameraPlatformViewAsync(
        CameraView cameraView,
        CancellationToken ct)
    {
        const int attempts = 20;

        for (var i = 0; i < attempts; i++)
        {
            ct.ThrowIfCancellationRequested();

            var isReady = await MainThread.InvokeOnMainThreadAsync(() =>
                cameraView.Handler?.PlatformView is not null);
            if (isReady)
                return true;

            await Task.Delay(50, ct);
        }

        _logger.LogDebug("Inline camera preview platform handler is not ready");
        return false;
    }

    internal void ShowScanResultCard(IQrContentHandler handler, Dictionary<string, object> parsed, string rawContent)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _lastScanHandler = handler;
            _lastScanParsed = parsed;
            _lastScanRawContent = rawContent;

            ScanResultIcon = handler.Icon;
            ScanResultTitle = handler.DisplayName;
            ScanResultSummary = handler.Summarize(parsed);

            ScanActions.Clear();
            foreach (var action in handler.SuggestedActions)
            {
                ScanActions.Add(new ContentAction
                {
                    Label = action,
                    Icon = "",
                    Command = new RelayCommand(() => ExecuteScanAction(action, handler, parsed, rawContent))
                });
            }

            ShowScanResult = true;
            _ = AutoDismissScanResultAsync();

            var entry = new TranscriptEntry
            {
                Role = "Scan",
                Text = $"{handler.Icon} {handler.DisplayName}: {handler.Summarize(parsed)}"
            };
            entry.Actions.Add(new ContentAction
            {
                Label = "Show actions",
                Icon = "\u21a9\ufe0f",
                Command = new RelayCommand(ReopenScanResultCard)
            });
            entry.NotifyActionsChanged();
            AddTranscriptEntry(entry, AssistiveActionIds.Scan);
        });
    }

    private void ReopenScanResultCard()
    {
        if (_lastScanHandler is not null && _lastScanParsed is not null && _lastScanRawContent is not null)
            ShowScanResultCard(_lastScanHandler, _lastScanParsed, _lastScanRawContent);
    }

    private void ExecuteScanAction(string action, IQrContentHandler handler, Dictionary<string, object> parsed, string rawContent)
    {
        ShowScanResult = false;

        if (IsRunning)
        {
            var prompt = $"The user chose \"{action}\" for the scanned {handler.ContentType}: {rawContent}";
            _ = _sessionCoordinator is not null
                ? _sessionCoordinator.SendTextInputAsync(prompt)
                : _orchestrator.SendTextInputAsync(prompt);
        }
    }

    private async Task AutoDismissScanResultAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(30));
        ShowScanResult = false;
    }

    /// <summary>
    /// Phase 6.1: Update AEC debug overlay when new statistics arrive from WebRTC APM.
    /// </summary>
    private void OnAecStatisticsUpdated(object? sender, ApmStatistics stats)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _aecMetricsDebugText = $"ERLE {stats.EchoReturnLossEnhancementDb:F1} dB · res {stats.ResidualEchoLikelihood:F2} · delay {stats.DelayMs} ms · div {stats.DivergentFilterFraction:F2}";
            RefreshAecDebugText();
        });
    }

    private void OnAudioRoutePolicyChanged(object? sender, AudioRoutePolicy policy)
    {
        MainThread.BeginInvokeOnMainThread(() => UpdateAudioPolicyDebugText(policy));
    }

    private void UpdateAudioPolicyDebugText(AudioRoutePolicy policy)
    {
        _audioPolicyDebugText =
            $"Audio: {policy.OutputCapabilities.EchoPathKind} | AEC {policy.AecMode} | cleanup {policy.VoiceCleanupMode} | {policy.EstimatedRoundTripLatencyMs}ms";
        RefreshAecDebugText();
    }

    private void RefreshAecDebugText()
    {
        var parts = new[] { _audioPolicyDebugText, _aecMetricsDebugText }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        AecDebugText = string.Join(Environment.NewLine, parts);
    }
}
