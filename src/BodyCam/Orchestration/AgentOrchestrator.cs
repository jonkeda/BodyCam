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
    private CancellationTokenSource? _turnCts;
    public SessionContext Session { get; } = new();

    public event EventHandler<string>? TranscriptUpdated;
    public event EventHandler<string>? TranscriptDelta;
    public event EventHandler<string>? TranscriptCompleted;
    public event EventHandler<string>? DebugLog;
    public event EventHandler<string>? ConversationReplyDelta;
    public event EventHandler<string>? ConversationReplyCompleted;

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
        _settings.Mode = _settingsService.Mode;

        DebugLog?.Invoke(this, $"Model: {_settings.RealtimeModel}");

        // Subscribe to Realtime events
        _realtime.AudioDelta += OnAudioDelta;
        _realtime.OutputTranscriptDelta += OnOutputTranscriptDelta;
        _realtime.OutputTranscriptCompleted += OnOutputTranscriptCompleted;
        _realtime.InputTranscriptCompleted += OnInputTranscriptCompleted;
        _realtime.SpeechStarted += OnSpeechStarted;
        _realtime.ResponseDone += OnResponseDone;
        _realtime.ErrorOccurred += OnError;

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

        _turnCts?.Cancel();
        _turnCts?.Dispose();
        _turnCts = null;

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
        if (_settings.Mode == ConversationMode.Separated)
            return; // Already handled by ConversationAgent streaming

        TranscriptUpdated?.Invoke(this, $"AI: {delta}");
        TranscriptDelta?.Invoke(this, delta);
    }

    private void OnOutputTranscriptCompleted(object? sender, string transcript)
    {
        if (_settings.Mode == ConversationMode.Separated)
            return; // Already handled by ConversationAgent

        _conversation.AddAssistantMessage(transcript, Session);
        TranscriptCompleted?.Invoke(this, $"AI:{transcript}");
        DebugLog?.Invoke(this, $"AI said: {transcript}");
    }

    private async void OnInputTranscriptCompleted(object? sender, string transcript)
    {
        TranscriptUpdated?.Invoke(this, $"You: {transcript}");
        TranscriptCompleted?.Invoke(this, $"You:{transcript}");
        DebugLog?.Invoke(this, $"User said: {transcript}");

        if (_settings.Mode == ConversationMode.Separated)
        {
            // Mode B: ProcessTranscriptAsync adds user message internally
            await ProcessModeBAsync(transcript);
        }
        else
        {
            // Mode A: just record the transcript
            _conversation.AddUserMessage(transcript, Session);
        }
    }

    private async Task ProcessModeBAsync(string transcript)
    {
        // Cancel any in-flight previous turn
        _turnCts?.Cancel();
        _turnCts?.Dispose();
        _turnCts = new CancellationTokenSource();
        var ct = _turnCts.Token;

        try
        {
            DebugLog?.Invoke(this, "Mode B: Processing via ConversationAgent...");

            var replyBuilder = new System.Text.StringBuilder();

            await foreach (var token in _conversation.ProcessTranscriptAsync(
                transcript, Session, ct))
            {
                replyBuilder.Append(token);
                ConversationReplyDelta?.Invoke(this, token);
                TranscriptDelta?.Invoke(this, token);
            }

            var fullReply = replyBuilder.ToString();
            if (fullReply.Length > 0)
            {
                ConversationReplyCompleted?.Invoke(this, fullReply);
                TranscriptCompleted?.Invoke(this, $"AI:{fullReply}");
                DebugLog?.Invoke(this, $"AI replied: {fullReply}");

                // Send reply to Realtime API for TTS
                if (_realtime.IsConnected && !ct.IsCancellationRequested)
                {
                    await _realtime.SendTextForTtsAsync(fullReply, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            DebugLog?.Invoke(this, "Mode B: Turn cancelled (interruption).");
        }
        catch (Exception ex)
        {
            DebugLog?.Invoke(this, $"Mode B error: {ex.Message}");
        }
    }

    private async void OnSpeechStarted(object? sender, EventArgs e)
    {
        // Mode B: cancel in-flight Chat API call + stop TTS
        if (_settings.Mode == ConversationMode.Separated)
        {
            _turnCts?.Cancel();
            _voiceOut.HandleInterruption();
            _voiceOut.ResetTracker();

            // Also cancel any in-flight Realtime TTS response
            try { await _realtime.CancelResponseAsync(); }
            catch (Exception ex) { DebugLog?.Invoke(this, $"TTS cancel error: {ex.Message}"); }

            DebugLog?.Invoke(this, "Mode B: Interrupted — cancelled Chat API + cleared audio.");
            return;
        }

        // Mode A: existing truncation logic
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

    private void OnResponseDone(object? sender, RealtimeResponseInfo info)
    {
        _voiceOut.ResetTracker();
        DebugLog?.Invoke(this, $"Response complete: {info.ResponseId}");
    }

    private void OnError(object? sender, string error)
    {
        DebugLog?.Invoke(this, $"Realtime error: {error}");
    }
}