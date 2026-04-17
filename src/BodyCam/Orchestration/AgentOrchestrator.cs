using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tools;

namespace BodyCam.Orchestration;

public class AgentOrchestrator
{
    private readonly VoiceInputAgent _voiceIn;
    private readonly VoiceOutputAgent _voiceOut;
    private readonly ConversationAgent _conversation;
    private readonly VisionAgent _vision;
    private readonly IRealtimeClient _realtime;
    private readonly ISettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly ToolDispatcher _dispatcher;
    private readonly IWakeWordService _wakeWord;

    public VisionAgent Vision => _vision;
    private readonly IMicrophoneCoordinator _micCoordinator;

    private CancellationTokenSource? _cts;
    private DateTimeOffset _lastVisionTime = DateTimeOffset.MinValue;
    public SessionContext Session { get; } = new();

    /// <summary>
    /// Delegate for capturing a frame from the CameraView. Set by the ViewModel.
    /// </summary>
    public Func<CancellationToken, Task<byte[]?>>? FrameCaptureFunc { get; set; }

    public event EventHandler<string>? TranscriptUpdated;
    public event EventHandler<string>? TranscriptDelta;
    public event EventHandler<string>? TranscriptCompleted;
    public event EventHandler<string>? DebugLog;

    public bool IsRunning => _cts is not null && !_cts.IsCancellationRequested;

    public AgentOrchestrator(
        VoiceInputAgent voiceIn,
        ConversationAgent conversation,
        VoiceOutputAgent voiceOut,
        VisionAgent vision,
        IRealtimeClient realtime,
        ISettingsService settingsService,
        AppSettings settings,
        ToolDispatcher dispatcher,
        IWakeWordService wakeWord,
        IMicrophoneCoordinator micCoordinator)
    {
        _voiceIn = voiceIn;
        _conversation = conversation;
        _voiceOut = voiceOut;
        _vision = vision;
        _realtime = realtime;
        _settingsService = settingsService;
        _settings = settings;
        _dispatcher = dispatcher;
        _wakeWord = wakeWord;
        _micCoordinator = micCoordinator;
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        Session.IsActive = true;

        // Refresh settings from persisted preferences before connecting
        _settings.RealtimeModel = _settingsService.RealtimeModel;
        _settings.ChatModel = _settingsService.ChatModel;
        _settings.VisionModel = _settingsService.VisionModel;
        _settings.TranscriptionModel = _settingsService.TranscriptionModel;
        _settings.Voice = _settingsService.Voice;
        _settings.TurnDetection = _settingsService.TurnDetection;
        _settings.NoiseReduction = _settingsService.NoiseReduction;
        _settings.SystemInstructions = _settingsService.SystemInstructions;

        // Provider & Azure settings
        _settings.Provider = _settingsService.Provider;
        _settings.AzureEndpoint = _settingsService.AzureEndpoint;
        _settings.AzureRealtimeDeploymentName = _settingsService.AzureRealtimeDeploymentName;
        _settings.AzureChatDeploymentName = _settingsService.AzureChatDeploymentName;
        _settings.AzureVisionDeploymentName = _settingsService.AzureVisionDeploymentName;
        _settings.AzureApiVersion = _settingsService.AzureApiVersion;

        DebugLog?.Invoke(this, $"Model: {_settings.RealtimeModel}");

        // Subscribe to Realtime events
        _realtime.AudioDelta += OnAudioDelta;
        _realtime.OutputTranscriptDelta += OnOutputTranscriptDelta;
        _realtime.OutputTranscriptCompleted += OnOutputTranscriptCompleted;
        _realtime.InputTranscriptCompleted += OnInputTranscriptCompleted;
        _realtime.SpeechStarted += OnSpeechStarted;
        _realtime.ResponseDone += OnResponseDone;
        _realtime.ErrorOccurred += OnError;
        _realtime.OutputItemAdded += OnOutputItemAdded;
        _realtime.FunctionCallReceived += OnFunctionCallReceived;

        // Connect to OpenAI
        await _realtime.ConnectAsync(_cts.Token);
        DebugLog?.Invoke(this, "Realtime connected.");

        // Start audio pipeline
        await _voiceOut.StartAsync(_cts.Token);
        await _voiceIn.StartAsync(_cts.Token);
        DebugLog?.Invoke(this, "Audio pipeline started.");
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        // Unsubscribe events
        _realtime.AudioDelta -= OnAudioDelta;
        _realtime.OutputTranscriptDelta -= OnOutputTranscriptDelta;
        _realtime.OutputTranscriptCompleted -= OnOutputTranscriptCompleted;
        _realtime.InputTranscriptCompleted -= OnInputTranscriptCompleted;
        _realtime.SpeechStarted -= OnSpeechStarted;
        _realtime.ResponseDone -= OnResponseDone;
        _realtime.ErrorOccurred -= OnError;
        _realtime.OutputItemAdded -= OnOutputItemAdded;
        _realtime.FunctionCallReceived -= OnFunctionCallReceived;

        _cts?.Cancel();

        await _voiceIn.StopAsync();
        await _voiceOut.StopAsync();

        await _realtime.DisconnectAsync();

        Session.IsActive = false;
        _cts?.Dispose();
        _cts = null;
        DebugLog?.Invoke(this, "Orchestrator stopped.");
    }

    // --- Event handlers ---

    private async void OnAudioDelta(object? sender, byte[] pcmData)
    {
        try { await _voiceOut.PlayAudioDeltaAsync(pcmData); }
        catch (Exception ex) { DebugLog?.Invoke(this, $"Playback error: {ex.Message}"); }
    }

    private void OnOutputTranscriptDelta(object? sender, string delta)
    {
        TranscriptUpdated?.Invoke(this, $"AI: {delta}");
        TranscriptDelta?.Invoke(this, delta);
    }

    private void OnOutputTranscriptCompleted(object? sender, string transcript)
    {
        TranscriptCompleted?.Invoke(this, $"AI:{transcript}");
        DebugLog?.Invoke(this, $"AI said: {transcript}");
    }

    private void OnInputTranscriptCompleted(object? sender, string transcript)
    {
        TranscriptUpdated?.Invoke(this, $"You: {transcript}");
        TranscriptCompleted?.Invoke(this, $"You:{transcript}");
        DebugLog?.Invoke(this, $"User said: {transcript}");
    }

    private async void OnSpeechStarted(object? sender, EventArgs e)
    {
        // Truncation logic
        if (_voiceOut.Tracker.CurrentItemId is not null)
        {
            _voiceOut.HandleInterruption();
            var itemId = _voiceOut.Tracker.CurrentItemId;
            var playedMs = _voiceOut.Tracker.PlayedMs;
            _voiceOut.ResetTracker();

            try
            {
                await _realtime.TruncateResponseAudioAsync(itemId, playedMs);
                DebugLog?.Invoke(this, $"Interrupted at {playedMs}ms.");
            }
            catch (Exception ex)
            {
                DebugLog?.Invoke(this, $"Truncation error: {ex.Message}");
            }
        }
    }

    private void OnOutputItemAdded(object? sender, string itemId)
    {
        _voiceOut.SetCurrentItem(itemId);
    }

    private void OnResponseDone(object? sender, RealtimeResponseInfo info)
    {
        _voiceOut.ResetTracker();
        DebugLog?.Invoke(this, $"Response complete: {info.ResponseId}");
    }

    private void OnError(object? sender, string error)
    {
        DebugLog?.Invoke(this, $"Realtime error: {error}");
    }

    public async Task StartListeningAsync()
    {
        _wakeWord.WakeWordDetected += OnWakeWordDetected;
        await _wakeWord.StartAsync(_cts?.Token ?? CancellationToken.None);
        DebugLog?.Invoke(this, "Wake word listening started.");
    }

    public async Task StopListeningAsync()
    {
        _wakeWord.WakeWordDetected -= OnWakeWordDetected;
        await _wakeWord.StopAsync();
        DebugLog?.Invoke(this, "Wake word listening stopped.");
    }

    private async void OnWakeWordDetected(object? sender, WakeWordDetectedEventArgs e)
    {
        DebugLog?.Invoke(this, $"Wake word: {e.Keyword} ({e.Action})");

        switch (e.Action)
        {
            case WakeWordAction.StartSession:
                await _micCoordinator.TransitionToActiveSessionAsync();
                await StartAsync();
                break;

            case WakeWordAction.GoToSleep:
                await StopAsync();
                await StopListeningAsync();
                await _micCoordinator.TransitionToWakeWordAsync();
                break;

            case WakeWordAction.InvokeTool:
                if (e.ToolName is not null)
                {
                    var wasActive = IsRunning;
                    if (!wasActive)
                        await StartAsync();

                    var context = CreateToolContext();
                    var result = await _dispatcher.ExecuteAsync(
                        e.ToolName, null, context, _cts?.Token ?? CancellationToken.None);

                    DebugLog?.Invoke(this, $"Wake word tool result: {result}");
                }
                break;
        }
    }

    private ToolContext CreateToolContext() => new()
    {
        CaptureFrame = FrameCaptureFunc ?? ((ct) => Task.FromResult<byte[]?>(null)),
        Session = Session,
        Log = msg => DebugLog?.Invoke(this, msg),
        RealtimeClient = _realtime
    };

    public async Task SendTextInputAsync(string text, CancellationToken ct = default)
    {
        if (!IsRunning)
            throw new InvalidOperationException("Orchestrator is not running.");

        await _realtime.SendTextInputAsync(text, ct);
    }

    private async void OnFunctionCallReceived(object? sender, FunctionCallInfo info)
    {
        DebugLog?.Invoke(this, $"Function call: {info.Name}");

        try
        {
            var context = CreateToolContext();
            var result = await _dispatcher.ExecuteAsync(
                info.Name, info.Arguments, context, _cts?.Token ?? CancellationToken.None);

            await _realtime.SendFunctionCallOutputAsync(info.CallId, result);
            DebugLog?.Invoke(this, $"Function result sent for {info.Name}");
        }
        catch (Exception ex)
        {
            DebugLog?.Invoke(this, $"Function call error ({info.Name}): {ex.Message}");

            try
            {
                await _realtime.SendFunctionCallOutputAsync(
                    info.CallId,
                    System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message }));
            }
            catch (Exception sendEx)
            {
                DebugLog?.Invoke(this, $"Failed to send error result: {sendEx.Message}");
            }
        }
    }

}