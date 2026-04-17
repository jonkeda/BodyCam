using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Services;

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
        AppSettings settings)
    {
        _voiceIn = voiceIn;
        _conversation = conversation;
        _voiceOut = voiceOut;
        _vision = vision;
        _realtime = realtime;
        _settingsService = settingsService;
        _settings = settings;
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

    private async void OnFunctionCallReceived(object? sender, FunctionCallInfo info)
    {
        DebugLog?.Invoke(this, $"Function call: {info.Name}");

        try
        {
            var result = info.Name switch
            {
                "describe_scene" => await ExecuteDescribeSceneAsync(info.Arguments),
                "deep_analysis" => await ExecuteDeepAnalysisAsync(info.Arguments),
                _ => System.Text.Json.JsonSerializer.Serialize(new { error = $"Unknown function: {info.Name}" })
            };

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

    private async Task<string> ExecuteDescribeSceneAsync(string? argumentsJson = null)
    {
        string? userPrompt = null;
        if (argumentsJson is not null)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.TryGetProperty("query", out var q))
                userPrompt = q.GetString();
        }

        // Rate-limit: return cached description if within cooldown
        if (_vision.LastDescription is not null
            && DateTimeOffset.UtcNow - _vision.LastCaptureTime < TimeSpan.FromSeconds(5))
        {
            return System.Text.Json.JsonSerializer.Serialize(new { description = _vision.LastDescription });
        }

        byte[]? frame = null;
        if (FrameCaptureFunc is not null)
            frame = await FrameCaptureFunc(_cts?.Token ?? CancellationToken.None);

        if (frame is null)
        {
            var stale = _vision.LastDescription ?? "Camera not available or no frame captured.";
            return System.Text.Json.JsonSerializer.Serialize(new { description = stale });
        }

        var description = await _vision.DescribeFrameAsync(frame, userPrompt);
        Session.LastVisionDescription = description;

        return System.Text.Json.JsonSerializer.Serialize(new { description });
    }

    private async Task<string> ExecuteDeepAnalysisAsync(string argumentsJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        var query = root.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
        var context = root.TryGetProperty("context", out var c) ? c.GetString() : null;

        var result = await _conversation.AnalyzeAsync(query, context);
        return System.Text.Json.JsonSerializer.Serialize(new { analysis = result });
    }
}