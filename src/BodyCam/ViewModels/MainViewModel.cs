using System.Collections.ObjectModel;
using System.Windows.Input;
using BodyCam.Models;
using BodyCam.Mvvm;
using BodyCam.Orchestration;
using BodyCam.Services;
using BodyCam.Services.Camera;
using BodyCam.Services.Input;

namespace BodyCam.ViewModels;

public enum ListeningLayer
{
    Sleep,
    WakeWord,
    ActiveSession
}

public class MainViewModel : ViewModelBase
{
    private readonly AgentOrchestrator _orchestrator;
    private readonly IApiKeyService _apiKeyService;
    private readonly ISettingsService _settingsService;
    private string _debugLog = string.Empty;
    private string _toggleButtonText = "Start";
    private string _statusText = "Ready";
    private bool _isRunning;
    private bool _debugVisible;
    private string? _visionStatus;

    internal TranscriptEntry? _currentAiEntry;

    private ListeningLayer _currentLayer = ListeningLayer.Sleep;
    private bool _showTranscriptTab = true;
    private ImageSource? _snapshotImage;
    private string? _snapshotCaption;
    private bool _showSnapshot;
    private bool _isTransitioning;

    private readonly CameraManager _cameraManager;
    private readonly ButtonInputManager _buttonInput;

    public MainViewModel(AgentOrchestrator orchestrator, IApiKeyService apiKeyService, ISettingsService settingsService, CameraManager cameraManager, ButtonInputManager buttonInput)
    {
        _orchestrator = orchestrator;
        _apiKeyService = apiKeyService;
        _settingsService = settingsService;
        _cameraManager = cameraManager;
        _buttonInput = buttonInput;
        Title = "BodyCam";

        _debugVisible = _settingsService.DebugMode;

        _buttonInput.ActionTriggered += OnButtonAction;

        ToggleCommand = new AsyncRelayCommand(ToggleAsync);
        ClearCommand = new RelayCommand(() =>
        {
            Entries.Clear();
            _currentAiEntry = null;
        });

        SetStateCommand = new AsyncRelayCommand(async (object? param) =>
        {
            if (param is not string segment) return;
            await SetLayerAsync(segment);
        });

        SwitchToTranscriptCommand = new RelayCommand(() =>
        {
            ShowTranscriptTab = true;
        });
        SwitchToCameraCommand = new AsyncRelayCommand(async () =>
        {
            ShowTranscriptTab = false;
            if (_cameraManager.Active is not null)
                await _cameraManager.Active.StartAsync();
        });
        ToggleDebugCommand = new RelayCommand(() =>
        {
            DebugVisible = !DebugVisible;
            _settingsService.DebugMode = DebugVisible;
        });
        DismissSnapshotCommand = new RelayCommand(() => ShowSnapshot = false);

        LookCommand = new AsyncRelayCommand(() => DispatchActionAsync(ButtonAction.Look));
        ReadCommand = new AsyncRelayCommand(() => DispatchActionAsync(ButtonAction.Read));
        FindCommand = new AsyncRelayCommand(() => DispatchActionAsync(ButtonAction.Find));
        AskCommand = new AsyncRelayCommand(() => DispatchActionAsync(ButtonAction.ToggleSession));
        PhotoCommand = new AsyncRelayCommand(() => DispatchActionAsync(ButtonAction.Photo));

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
                            return; // Don't touch _currentAiEntry — AI is still streaming
                        }
                    }

                    Entries.Add(userEntry);
                }
                else if (msg.StartsWith("AI:"))
                {
                    if (_currentAiEntry is not null)
                    {
                        _currentAiEntry.IsThinking = false;
                        _currentAiEntry.Text = msg[3..].Trim();
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
    }

    public ObservableCollection<TranscriptEntry> Entries { get; } = [];

    public string DebugLog
    {
        get => _debugLog;
        set => SetProperty(ref _debugLog, value);
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
        set => SetProperty(ref _debugVisible, value);
    }

    public string? VisionStatus
    {
        get => _visionStatus;
        set => SetProperty(ref _visionStatus, value);
    }

    public ICommand ToggleCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand SetStateCommand { get; }
    public ICommand SwitchToTranscriptCommand { get; }
    public ICommand SwitchToCameraCommand { get; }
    public ICommand ToggleDebugCommand { get; }
    public ICommand DismissSnapshotCommand { get; }
    public ICommand LookCommand { get; }
    public ICommand ReadCommand { get; }
    public ICommand FindCommand { get; }
    public ICommand AskCommand { get; }
    public ICommand PhotoCommand { get; }

    private static readonly Color ActiveBg = Color.FromArgb("#512BD4");
    private static readonly Color InactiveBg = Colors.Transparent;
    private static readonly Color ActiveTextColor = Colors.White;
    private static readonly Color InactiveTextColor = Color.FromArgb("#999999");

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
                OnPropertyChanged(nameof(SleepSegmentColor));
                OnPropertyChanged(nameof(SleepSegmentTextColor));
                OnPropertyChanged(nameof(ListenSegmentColor));
                OnPropertyChanged(nameof(ListenSegmentTextColor));
                OnPropertyChanged(nameof(ActiveSegmentColor));
                OnPropertyChanged(nameof(ActiveSegmentTextColor));
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

    private static readonly Color TabActiveBg = Color.FromArgb("#2196F3");
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
        set => SetProperty(ref _showSnapshot, value);
    }

    public Color StateColor => CurrentLayer switch
    {
        ListeningLayer.Sleep => Color.FromArgb("#666666"),
        ListeningLayer.WakeWord => Color.FromArgb("#4CAF50"),
        ListeningLayer.ActiveSession => Color.FromArgb("#2196F3"),
        _ => Color.FromArgb("#666666")
    };

    public Color SleepSegmentColor => CurrentLayer == ListeningLayer.Sleep ? ActiveBg : InactiveBg;
    public Color ListenSegmentColor => CurrentLayer == ListeningLayer.WakeWord ? ActiveBg : InactiveBg;
    public Color ActiveSegmentColor => CurrentLayer == ListeningLayer.ActiveSession ? ActiveBg : InactiveBg;
    public Color SleepSegmentTextColor => CurrentLayer == ListeningLayer.Sleep ? ActiveTextColor : InactiveTextColor;
    public Color ListenSegmentTextColor => CurrentLayer == ListeningLayer.WakeWord ? ActiveTextColor : InactiveTextColor;
    public Color ActiveSegmentTextColor => CurrentLayer == ListeningLayer.ActiveSession ? ActiveTextColor : InactiveTextColor;

    private async Task ToggleAsync()
    {
        DebugVisible = _settingsService.DebugMode;

        if (IsRunning)
            await SetLayerAsync("Sleep");
        else
            await SetLayerAsync("Active");
    }

    private async Task SendVisionCommandAsync(string prompt)
    {
        // Option B: Session is running — send through Realtime API (spoken aloud)
        if (IsRunning)
        {
            // Show thinking indicator immediately for realtime path
            _currentAiEntry = new TranscriptEntry { Role = "AI", IsThinking = true };
            Entries.Add(_currentAiEntry);

            await _orchestrator.SendTextInputAsync(prompt);
            return;
        }

        // Option A: No session — capture frame directly, call VisionAgent, show in transcript (no voice)
        var frame = await _cameraManager.CaptureFrameAsync();

        ImageSource? imageSource = null;
        if (frame is not null)
            imageSource = ImageSource.FromStream(() => new MemoryStream(frame));

        // Add user action to transcript
        Entries.Add(new TranscriptEntry
        {
            Role = "You",
            Text = prompt,
            Image = imageSource,
            ImageCaption = "Captured frame"
        });

        if (frame is null)
        {
            Entries.Add(new TranscriptEntry { Role = "AI", Text = "Camera not available or no frame captured." });
            return;
        }

        // Show thinking indicator while vision processes
        var aiEntry = new TranscriptEntry { Role = "AI", IsThinking = true };
        Entries.Add(aiEntry);

        var description = await _orchestrator.Vision.DescribeFrameAsync(frame, prompt);

        aiEntry.IsThinking = false;
        aiEntry.Text = description;

        // Switch to transcript tab so the user sees the result
        ShowTranscriptTab = true;
    }

    private async Task EnsureActiveAndSendAsync(string prompt)
    {
        if (CurrentLayer != ListeningLayer.ActiveSession)
            await SetLayerAsync("Active");

        if (!IsRunning) return;

        await _orchestrator.SendTextInputAsync(prompt);
    }

    private async Task SetLayerAsync(string segment)
    {
        if (_isTransitioning) return;
        _isTransitioning = true;
        try
        {
            var target = segment switch
            {
                "Sleep" => ListeningLayer.Sleep,
                "Listen" => ListeningLayer.WakeWord,
                "Active" => ListeningLayer.ActiveSession,
                _ => CurrentLayer
            };

            if (target == CurrentLayer) return;

            // De-escalate
            if (target < CurrentLayer)
            {
                if (CurrentLayer == ListeningLayer.ActiveSession)
                {
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

                    IsRunning = true;
                }
                catch (Exception ex)
                {
                    IsRunning = false;
                    ToggleButtonText = "Start";
                    StatusText = "Ready";
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

    public async Task DispatchActionAsync(ButtonAction action)
    {
        try
        {
            switch (action)
            {
                case ButtonAction.Look:
                    await SendVisionCommandAsync("Describe what you see in front of me.");
                    break;
                case ButtonAction.Read:
                    await SendVisionCommandAsync("Read any text you can see in front of me.");
                    break;
                case ButtonAction.Find:
                    await SendVisionCommandAsync("Look around and tell me what objects you can find.");
                    break;
                case ButtonAction.Photo:
                    await SendVisionCommandAsync("Take a photo of what you see.");
                    break;
                case ButtonAction.ToggleSession:
                    if (IsRunning)
                        await SetLayerAsync("Sleep");
                    else
                        await SetLayerAsync("Active");
                    break;
                case ButtonAction.ToggleSleepActive:
                    if (CurrentLayer == ListeningLayer.Sleep)
                        await SetLayerAsync("Active");
                    else
                        await SetLayerAsync("Sleep");
                    break;
            }
        }
        catch (Exception ex)
        {
            DebugLog += $"[{DateTime.Now:HH:mm:ss}] Action error ({action}): {ex.Message}{Environment.NewLine}";
        }
    }

    private async void OnButtonAction(object? sender, ButtonActionEvent e)
    {
        try
        {
            await DispatchActionAsync(e.Action);
        }
        catch (Exception ex)
        {
            DebugLog += $"[{DateTime.Now:HH:mm:ss}] Button handler error: {ex.Message}{Environment.NewLine}";
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

}
