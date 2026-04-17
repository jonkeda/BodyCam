using System.Collections.ObjectModel;
using System.Windows.Input;
using BodyCam.Models;
using BodyCam.Mvvm;
using BodyCam.Orchestration;
using BodyCam.Services;
using CommunityToolkit.Maui.Views;

namespace BodyCam.ViewModels;

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
    private CameraView? _cameraView;

    internal TranscriptEntry? _currentAiEntry;

    public MainViewModel(AgentOrchestrator orchestrator, IApiKeyService apiKeyService, ISettingsService settingsService)
    {
        _orchestrator = orchestrator;
        _apiKeyService = apiKeyService;
        _settingsService = settingsService;
        Title = "BodyCam";

        _debugVisible = _settingsService.DebugMode;

        ToggleCommand = new AsyncRelayCommand(ToggleAsync);
        ClearCommand = new RelayCommand(() =>
        {
            Entries.Clear();
            _currentAiEntry = null;
        });

        _orchestrator.TranscriptDelta += (_, delta) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (string.IsNullOrEmpty(delta)) return;

                if (_currentAiEntry is null)
                {
                    _currentAiEntry = new TranscriptEntry { Role = "AI" };
                    Entries.Add(_currentAiEntry);
                }
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
                        _currentAiEntry.Text = msg[3..].Trim();
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

    private async Task ToggleAsync()
    {
        // Refresh debug visibility from settings
        DebugVisible = _settingsService.DebugMode;

        if (IsRunning)
        {
            _cameraView?.StopCameraPreview();
            VisionStatus = null;

            await _orchestrator.StopAsync();
            IsRunning = false;
            ToggleButtonText = "Start";
            StatusText = "Ready";
        }
        else
        {
            try
            {
                // Ensure API key is available
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

                if (_cameraView is not null)
                    await _cameraView.StartCameraPreview(CancellationToken.None);

                IsRunning = true;
                StatusText = "Listening...";
                OnPropertyChanged();
            }
            catch (Exception ex)
            {
                IsRunning = false;
                ToggleButtonText = "Start";
                StatusText = $"Error: {ex.Message}";
                DebugLog += $"[{DateTime.Now:HH:mm:ss}] Start failed: {ex.Message}{Environment.NewLine}";
            }
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
        if (_cameraView is null) return null;

        try
        {
            var tcs = new TaskCompletionSource<byte[]?>();

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

            _cameraView.MediaCaptured += OnMediaCaptured;
            try
            {
                await _cameraView.CaptureImage(ct);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                timeoutCts.Token.Register(() => tcs.TrySetResult(null));

                return await tcs.Task;
            }
            finally
            {
                _cameraView.MediaCaptured -= OnMediaCaptured;
            }
        }
        catch
        {
            return null;
        }
    }
}
