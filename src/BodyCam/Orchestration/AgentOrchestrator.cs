using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Services.Audio.WebRtcApm;
using Microsoft.Extensions.AI;
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
    private readonly Microsoft.Extensions.AI.IRealtimeClient _realtimeFactory;
    private Microsoft.Extensions.AI.IRealtimeClientSession? _session;
    private Task? _messageLoop;
    private readonly ISettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly ToolDispatcher _dispatcher;
    private readonly IWakeWordService _wakeWord;
    private readonly CameraManager _cameraManager;
    private readonly AecProcessor _aec;
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

    // Speech events not in MAF — use extensible RealtimeServerMessageType
    private static readonly RealtimeServerMessageType SpeechStarted =
        new("input_audio_buffer.speech_started");

    public AgentOrchestrator(
        VoiceInputAgent voiceIn,
        ConversationAgent conversation,
        VoiceOutputAgent voiceOut,
        VisionAgent vision,
        Microsoft.Extensions.AI.IRealtimeClient realtimeFactory,
        ISettingsService settingsService,
        AppSettings settings,
        ToolDispatcher dispatcher,
        IWakeWordService wakeWord,
        IMicrophoneCoordinator micCoordinator,
        CameraManager cameraManager,
        AecProcessor aec,
        ILogger<AgentOrchestrator> logger)
    {
        _voiceIn = voiceIn;
        _conversation = conversation;
        _voiceOut = voiceOut;
        _vision = vision;
        _realtimeFactory = realtimeFactory;
        _settingsService = settingsService;
        _settings = settings;
        _dispatcher = dispatcher;
        _wakeWord = wakeWord;
        _micCoordinator = micCoordinator;
        _cameraManager = cameraManager;
        _aec = aec;
        _logger = logger;
    }

    private RealtimeSessionOptions BuildSessionOptions()
    {
        return new RealtimeSessionOptions
        {
            Instructions = _settings.SystemInstructions,
            Voice = _settings.Voice,
            InputAudioFormat = new RealtimeAudioFormat("audio/pcm", _settings.SampleRate),
            OutputAudioFormat = new RealtimeAudioFormat("audio/pcm", _settings.SampleRate),
            OutputModalities = ["audio", "text"],
            TranscriptionOptions = new TranscriptionOptions { ModelId = _settings.TranscriptionModel },
            VoiceActivityDetection = new VoiceActivityDetectionOptions
            {
                Enabled = true,
                AllowInterruption = true,
            },
        };
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

        var options = BuildSessionOptions();
        _session = await _realtimeFactory.CreateSessionAsync(options, _cts.Token);
        _messageLoop = Task.Run(() => RunMessageLoopAsync(_session, _cts.Token));
        _logger.LogInformation("Realtime session created");

        // Wire audio sink for VoiceInputAgent
        _voiceIn.SetAudioSink(async (pcm, ct) =>
        {
            var audioMsg = new InputAudioBufferAppendRealtimeClientMessage(
                new DataContent(pcm, "audio/pcm"));
            await _session.SendAsync(audioMsg, ct);
        });
        _voiceIn.SetConnected(true);

        // Initialize echo cancellation
        if (_settings.AecEnabled)
        {
            try
            {
                bool mobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
                _aec.Initialize(mobile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AEC initialization failed, continuing without echo cancellation");
                _aec.IsEnabled = false;
            }
        }
        else
        {
            _aec.IsEnabled = false;
        }

        // Start audio pipeline
        await _voiceOut.StartAsync(_cts.Token);
        await _voiceIn.StartAsync(_cts.Token);
        _logger.LogInformation("Audio pipeline started");
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        _cts?.Cancel();

        if (_messageLoop is not null)
        {
            try { await _messageLoop; } catch (OperationCanceledException) { }
            _messageLoop = null;
        }

        _voiceIn.SetConnected(false);
        _voiceIn.SetAudioSink(null);
        await _voiceIn.StopAsync();
        await _voiceOut.StopAsync();

        if (_session is not null)
        {
            await _session.DisposeAsync();
            _session = null;
        }

        Session.IsActive = false;
        CurrentConfig = null;
        _cts?.Dispose();
        _cts = null;
        _logger.LogInformation("Orchestrator stopped");
    }

    private async Task RunMessageLoopAsync(
        Microsoft.Extensions.AI.IRealtimeClientSession session,
        CancellationToken ct)
    {
        try
        {
            await foreach (var message in session.GetStreamingResponseAsync(ct))
            {
                var type = message.Type;

                if (type == RealtimeServerMessageType.OutputAudioDelta)
                {
                    var audioMsg = (OutputTextAudioRealtimeServerMessage)message;
                    if (audioMsg.Audio is { } audio)
                    {
                        try { await _voiceOut.PlayAudioDeltaAsync(Convert.FromBase64String(audio)); }
                        catch (Exception ex) { _logger.LogError(ex, "Playback error"); }
                    }
                }
                else if (type == RealtimeServerMessageType.OutputTextDelta)
                {
                    var textMsg = (OutputTextAudioRealtimeServerMessage)message;
                    if (textMsg.Text is { } delta)
                    {
                        TranscriptUpdated?.Invoke(this, $"AI: {delta}");
                        TranscriptDelta?.Invoke(this, delta);
                    }
                }
                else if (type == RealtimeServerMessageType.OutputTextDone
                      || type == RealtimeServerMessageType.OutputAudioTranscriptionDone)
                {
                    var textMsg = (OutputTextAudioRealtimeServerMessage)message;
                    if (textMsg.Text is { } transcript)
                    {
                        TranscriptCompleted?.Invoke(this, $"AI:{transcript}");
                        _logger.LogDebug("AI transcript: {Length} chars", transcript.Length);
                    }
                }
                else if (type == RealtimeServerMessageType.InputAudioTranscriptionCompleted)
                {
                    var transcriptMsg = (InputAudioTranscriptionRealtimeServerMessage)message;
                    if (transcriptMsg.Transcription is { } transcript)
                    {
                        TranscriptUpdated?.Invoke(this, $"You: {transcript}");
                        TranscriptCompleted?.Invoke(this, $"You:{transcript}");
                        _logger.LogDebug("User transcript received");
                    }
                }
                else if (type == RealtimeServerMessageType.ResponseOutputItemAdded)
                {
                    var itemMsg = (ResponseOutputItemRealtimeServerMessage)message;
                    if (itemMsg.Item?.Id is { } itemId)
                        _voiceOut.SetCurrentItem(itemId);
                }
                else if (type == RealtimeServerMessageType.ResponseDone)
                {
                    _voiceOut.ResetTracker();
                    var respMsg = (ResponseCreatedRealtimeServerMessage)message;
                    _logger.LogDebug("Response complete: {ResponseId}", respMsg.ResponseId);
                }
                else if (type == RealtimeServerMessageType.Error)
                {
                    var errorMsg = (ErrorRealtimeServerMessage)message;
                    _logger.LogError("Realtime error: {Error}", errorMsg.Error);
                }
                else if (type == SpeechStarted)
                {
                    await HandleSpeechStartedAsync(session, ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message loop error");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Session ended unexpectedly — reconnecting");
                await ReconnectAsync();
            }
        }
    }

    private async Task HandleSpeechStartedAsync(
        Microsoft.Extensions.AI.IRealtimeClientSession session,
        CancellationToken ct)
    {
        try
        {
            if (_voiceOut.Tracker.CurrentItemId is not null)
            {
                _voiceOut.HandleInterruption();
                var itemId = _voiceOut.Tracker.CurrentItemId;
                var playedMs = _voiceOut.Tracker.PlayedMs;
                _voiceOut.ResetTracker();

                try
                {
                    var truncateJson = BinaryData.FromObjectAsJson(new
                    {
                        type = "conversation.item.truncate",
                        item_id = itemId,
                        content_index = 0,
                        audio_end_ms = playedMs,
                    });
                    var msg = new RealtimeClientMessage { RawRepresentation = truncateJson };
                    await session.SendAsync(msg, ct);
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

    private async Task ReconnectAsync()
    {
        var delay = TimeSpan.FromSeconds(1);
        const int maxRetries = 5;

        for (int i = 0; i < maxRetries; i++)
        {
            _logger.LogInformation("Reconnecting ({Attempt}/{MaxRetries})", i + 1, maxRetries);
            try
            {
                if (_session is not null)
                    await _session.DisposeAsync();

                var options = BuildSessionOptions();
                var ct = _cts?.Token ?? CancellationToken.None;
                _session = await _realtimeFactory.CreateSessionAsync(options, ct);
                _messageLoop = Task.Run(() => RunMessageLoopAsync(_session, ct));
                _voiceIn.SetAudioSink(async (pcm, innerCt) =>
                {
                    var audioMsg = new InputAudioBufferAppendRealtimeClientMessage(
                        new DataContent(pcm, "audio/pcm"));
                    await _session.SendAsync(audioMsg, innerCt);
                });
                _voiceIn.SetConnected(true);
                await _voiceIn.StartAsync(ct);
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
    };

    public async Task SendTextInputAsync(string text, CancellationToken ct = default)
    {
        if (_session is null)
            throw new InvalidOperationException("Session is not active.");

        var item = new RealtimeConversationItem([new TextContent(text)], null, ChatRole.User);
        await _session.SendAsync(new CreateConversationItemRealtimeClientMessage(item), ct);
        await _session.SendAsync(new CreateResponseRealtimeClientMessage(), ct);
    }

}