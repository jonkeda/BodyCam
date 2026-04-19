using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Services.Camera;
using BodyCam.Tools;
using Microsoft.Extensions.Logging;

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
    private readonly CameraManager _cameraManager;
    private readonly ILogger<AgentOrchestrator> _logger;

    public VisionAgent Vision => _vision;
    private readonly IMicrophoneCoordinator _micCoordinator;

    private CancellationTokenSource? _cts;
    private DateTimeOffset _lastVisionTime = DateTimeOffset.MinValue;
    public SessionContext Session { get; } = new();

    public event EventHandler<string>? TranscriptUpdated;
    public event EventHandler<string>? TranscriptDelta;
    public event EventHandler<string>? TranscriptCompleted;

    public SessionConfig? CurrentConfig { get; private set; }

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
        IMicrophoneCoordinator micCoordinator,
        CameraManager cameraManager,
        ILogger<AgentOrchestrator> logger)
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
        _cameraManager = cameraManager;
        _logger = logger;
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

        CurrentConfig = new SessionConfig
        {
            RealtimeModel = _settings.RealtimeModel,
            ChatModel = _settings.ChatModel,
            VisionModel = _settings.VisionModel,
            TranscriptionModel = _settings.TranscriptionModel,
            Voice = _settings.Voice,
            TurnDetection = _settings.TurnDetection,
            NoiseReduction = _settings.NoiseReduction,
            SystemInstructions = _settings.SystemInstructions,
        };

        _logger.LogInformation("Realtime model: {Model}", _settings.RealtimeModel);

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
        _realtime.ConnectionLost += OnConnectionLost;

        // Connect to OpenAI
        await _realtime.ConnectAsync(_cts.Token);
        _logger.LogInformation("Realtime connected");

        // Start audio pipeline
        await _voiceOut.StartAsync(_cts.Token);
        await _voiceIn.StartAsync(_cts.Token);
        _logger.LogInformation("Audio pipeline started");
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
        _realtime.ConnectionLost -= OnConnectionLost;

        _cts?.Cancel();

        await _voiceIn.StopAsync();
        await _voiceOut.StopAsync();

        await _realtime.DisconnectAsync();

        Session.IsActive = false;
        CurrentConfig = null;
        _cts?.Dispose();
        _cts = null;
        _logger.LogInformation("Orchestrator stopped");
    }

    // --- Event handlers ---

    private async void OnAudioDelta(object? sender, byte[] pcmData)
    {
        try { await _voiceOut.PlayAudioDeltaAsync(pcmData); }
        catch (Exception ex) { _logger.LogError(ex, "Playback error"); }
    }

    private void OnOutputTranscriptDelta(object? sender, string delta)
    {
        TranscriptUpdated?.Invoke(this, $"AI: {delta}");
        TranscriptDelta?.Invoke(this, delta);
    }

    private void OnOutputTranscriptCompleted(object? sender, string transcript)
    {
        TranscriptCompleted?.Invoke(this, $"AI:{transcript}");
        _logger.LogDebug("AI transcript: {Length} chars", transcript.Length);
    }

    private void OnInputTranscriptCompleted(object? sender, string transcript)
    {
        TranscriptUpdated?.Invoke(this, $"You: {transcript}");
        TranscriptCompleted?.Invoke(this, $"You:{transcript}");
        _logger.LogDebug("User transcript received");
    }

    private async void OnSpeechStarted(object? sender, EventArgs e)
    {
        try
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
                    _logger.LogDebug("Interrupted at {PlayedMs}ms", playedMs);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Truncation error");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SpeechStarted handler error");
        }
    }

    private void OnOutputItemAdded(object? sender, string itemId)
    {
        _voiceOut.SetCurrentItem(itemId);
    }

    private void OnResponseDone(object? sender, RealtimeResponseInfo info)
    {
        _voiceOut.ResetTracker();
        _logger.LogDebug("Response complete: {ResponseId}", info.ResponseId);
    }

    private void OnError(object? sender, string error)
    {
        _logger.LogError("Realtime error: {Error}", error);
    }

    private async void OnConnectionLost(object? sender, string reason)
    {
        try
        {
            _logger.LogWarning("Connection lost: {Reason}", reason);
            await ReconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconnect handler error");
        }
    }

    private async Task ReconnectAsync()
    {
        var delay = TimeSpan.FromSeconds(1);
        const int maxRetries = 5;

        for (int i = 0; i < maxRetries; i++)
        {
            _logger.LogInformation("Reconnecting ({Attempt}/{MaxRetries})", i + 1, maxRetries);
            try
            {
                await _realtime.ConnectAsync(_cts?.Token ?? CancellationToken.None);
                await _realtime.UpdateSessionAsync(_cts?.Token ?? CancellationToken.None);
                await _voiceIn.StartAsync(_cts?.Token ?? CancellationToken.None);
                _logger.LogInformation("Reconnected");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnect failed");
                await Task.Delay(delay);
                delay *= 2; // exponential backoff: 1s, 2s, 4s, 8s, 16s
            }
        }

        _logger.LogError("Reconnection failed after {MaxRetries} attempts. Stopping session", maxRetries);
        await StopAsync();
    }

    public async Task StartListeningAsync()
    {
        _wakeWord.WakeWordDetected += OnWakeWordDetected;
        await _wakeWord.StartAsync(_cts?.Token ?? CancellationToken.None);
        _logger.LogInformation("Wake word listening started");
    }

    public async Task StopListeningAsync()
    {
        _wakeWord.WakeWordDetected -= OnWakeWordDetected;
        await _wakeWord.StopAsync();
        _logger.LogInformation("Wake word listening stopped");
    }

    private async void OnWakeWordDetected(object? sender, WakeWordDetectedEventArgs e)
    {
        try
        {
            _logger.LogInformation("Wake word: {Keyword} ({Action})", e.Keyword, e.Action);

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

                        _logger.LogDebug("Wake word tool result: {Result}", result);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Wake word handler error");
        }
    }

    private ToolContext CreateToolContext() => new()
    {
        CaptureFrame = _cameraManager.CaptureFrameAsync,
        Session = Session,
        Log = msg => _logger.LogInformation("{ToolMessage}", msg),
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
        _logger.LogInformation("Function call: {ToolName}", info.Name);

        try
        {
            var context = CreateToolContext();
            var result = await _dispatcher.ExecuteAsync(
                info.Name, info.Arguments, context, _cts?.Token ?? CancellationToken.None);

            await _realtime.SendFunctionCallOutputAsync(info.CallId, result);
            _logger.LogDebug("Function result sent for {ToolName}", info.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Function call error ({ToolName})", info.Name);

            try
            {
                await _realtime.SendFunctionCallOutputAsync(
                    info.CallId,
                    System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message }));
            }
            catch (Exception sendEx)
            {
                _logger.LogError(sendEx, "Failed to send error result");
            }
        }
    }

}