using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Services.Audio.WebRtcApm;
using BodyCam.Services.Camera;
using BodyCam.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

#pragma warning disable OPENAI002
#pragma warning disable SCME0001
#pragma warning disable MEAI001

namespace BodyCam.Orchestration;

public class AgentOrchestrator
{
    private readonly VoiceInputAgent _voiceIn;
    private readonly VoiceOutputAgent _voiceOut;
    private readonly ConversationAgent _conversation;
    private readonly VisionAgent _vision;
    private readonly IRealtimeClient _realtimeFactory;
    private IRealtimeClientSession? _session;
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
    public event EventHandler<string>? DebugLog;

    /// <summary>Delegate injected by UI to capture camera frames for vision.</summary>
    public Func<CancellationToken, Task<byte[]?>>? FrameCaptureFunc { get; set; }

    public SessionConfig? CurrentConfig { get; private set; }

    public bool IsRunning => _cts is not null && !_cts.IsCancellationRequested;

    /// <summary>Custom message type for speech started (not in MAF abstraction).</summary>
    private static readonly RealtimeServerMessageType SpeechStarted = new("input_audio_buffer.speech_started");

    public AgentOrchestrator(
        VoiceInputAgent voiceIn,
        ConversationAgent conversation,
        VoiceOutputAgent voiceOut,
        VisionAgent vision,
        IRealtimeClient realtimeFactory,
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

    /// <summary>
    /// Build MAF session options from current settings.
    /// </summary>
    private RealtimeSessionOptions BuildSessionOptions()
    {
        var voice = _settings.Voice?.ToLowerInvariant() switch
        {
            "echo" => "echo",
            "shimmer" => "shimmer",
            "ash" => "ash",
            "coral" => "coral",
            "sage" => "sage",
            _ => "alloy",
        };

        var tools = _dispatcher.GetToolDefinitions().Select(dto =>
            (AITool)AIFunctionFactory.Create(
                method: (string? args) => args ?? "",
                name: dto.Name,
                description: dto.Description))
            .ToList();

        // Azure Realtime only supports whisper-1 for input transcription;
        // gpt-4o-mini-transcribe is only available on direct OpenAI.
        var transcriptionModel = _settings.TranscriptionModel ?? "whisper-1";
        if (_settings.Provider == OpenAiProvider.Azure
            && transcriptionModel.Contains("transcribe", StringComparison.OrdinalIgnoreCase))
        {
            transcriptionModel = "whisper-1";
        }

        return new RealtimeSessionOptions
        {
            Model = _settings.RealtimeModel,
            Instructions = _settings.SystemInstructions,
            Voice = voice,
            Tools = tools,
            TranscriptionOptions = new()
            {
                ModelId = transcriptionModel,
            },
            VoiceActivityDetection = new()
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
        _logger.LogInformation("Realtime connected");

        // Wire audio sink for VoiceInputAgent — send PCM chunks via MAF
        _voiceIn.SetAudioSink(async (pcm, ct) =>
        {
            await _session!.SendAsync(
                new InputAudioBufferAppendRealtimeClientMessage(
                    new DataContent(pcm, "audio/pcm")), ct);
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

        if (_session is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (_session is IDisposable disposable)
            disposable.Dispose();
        _session = null;

        Session.IsActive = false;
        CurrentConfig = null;
        _cts?.Dispose();
        _cts = null;
        _logger.LogInformation("Orchestrator stopped");
    }

    /// <summary>
    /// Receive typed server updates from the MAF event stream.
    /// </summary>
    private async Task RunMessageLoopAsync(
        IRealtimeClientSession session,
        CancellationToken ct)
    {
        try
        {
            await foreach (var msg in session.GetStreamingResponseAsync(ct))
            {
                var type = msg.Type;

                if (type == RealtimeServerMessageType.OutputAudioDelta)
                {
                    try
                    {
                        var audioMsg = (OutputTextAudioRealtimeServerMessage)msg;
                        if (audioMsg.Audio is not null)
                        {
                            var audioBytes = Convert.FromBase64String(audioMsg.Audio);
                            await _voiceOut.PlayAudioDeltaAsync(audioBytes);
                        }
                    }
                    catch (Exception ex) { _logger.LogError(ex, "Playback error"); }
                }
                else if (type == RealtimeServerMessageType.OutputAudioTranscriptionDelta)
                {
                    // Streaming transcript of AI audio — drives live UI text
                    var transcriptMsg = (OutputTextAudioRealtimeServerMessage)msg;
                    var delta = transcriptMsg.Text ?? "";
                    TranscriptUpdated?.Invoke(this, $"AI: {delta}");
                    TranscriptDelta?.Invoke(this, delta);
                }
                else if (type == RealtimeServerMessageType.OutputTextDelta)
                {
                    // Text-only mode fallback (not used with audio output)
                    var textMsg = (OutputTextAudioRealtimeServerMessage)msg;
                    var delta = textMsg.Text ?? "";
                    TranscriptUpdated?.Invoke(this, $"AI: {delta}");
                    TranscriptDelta?.Invoke(this, delta);
                }
                else if (type == RealtimeServerMessageType.OutputAudioTranscriptionDone)
                {
                    var transcriptMsg = (OutputTextAudioRealtimeServerMessage)msg;
                    var transcript = transcriptMsg.Text ?? "";
                    TranscriptCompleted?.Invoke(this, $"AI:{transcript}");
                    _logger.LogDebug("AI audio transcript: {Length} chars", transcript.Length);
                }
                else if (type == RealtimeServerMessageType.OutputTextDone)
                {
                    var textMsg = (OutputTextAudioRealtimeServerMessage)msg;
                    var text = textMsg.Text ?? "";
                    TranscriptCompleted?.Invoke(this, $"AI:{text}");
                    _logger.LogDebug("AI text: {Length} chars", text.Length);
                }
                else if (type == RealtimeServerMessageType.InputAudioTranscriptionCompleted)
                {
                    var userMsg = (InputAudioTranscriptionRealtimeServerMessage)msg;
                    var transcript = userMsg.Transcription ?? "";
                    TranscriptUpdated?.Invoke(this, $"You: {transcript}");
                    TranscriptCompleted?.Invoke(this, $"You:{transcript}");
                    _logger.LogDebug("User transcript received");
                }
                else if (type == RealtimeServerMessageType.ResponseOutputItemAdded)
                {
                    var itemMsg = (ResponseOutputItemRealtimeServerMessage)msg;
                    if (itemMsg.Item?.Id is not null)
                    {
                        _voiceOut.SetCurrentItem(itemMsg.Item.Id);
                    }
                }
                else if (type == RealtimeServerMessageType.ResponseDone)
                {
                    _voiceOut.ResetTracker();
                    await HandleResponseDoneAsync(session, msg, ct);
                    _logger.LogDebug("Response complete");
                }
                else if (type == SpeechStarted)
                {
                    await HandleSpeechStartedAsync(session, ct);
                }
                else if (type == RealtimeServerMessageType.Error)
                {
                    var errorMsg = (ErrorRealtimeServerMessage)msg;
                    _logger.LogError("Realtime error: {Error}", errorMsg.Error?.Message ?? "unknown");
                }
                else
                {
                    _logger.LogDebug("Unhandled message type: {Type} CLR={ClrType}", type, msg.GetType().Name);
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

    /// <summary>
    /// Handle response done: extract function call items via RawRepresentation and dispatch.
    /// MAF doesn't expose function call items as first-class types, so we access the
    /// underlying SDK types via RawRepresentation.
    /// </summary>
    private async Task HandleResponseDoneAsync(
        IRealtimeClientSession session,
        RealtimeServerMessage msg,
        CancellationToken ct)
    {
        // Access function calls via the underlying SDK type
        if (msg.RawRepresentation is not OpenAI.Realtime.RealtimeServerUpdateResponseDone sdkResponseDone)
            return;

        var response = sdkResponseDone.Response;
        bool hadFunctionCalls = false;

        foreach (var item in response.OutputItems)
        {
            if (item is not OpenAI.Realtime.RealtimeFunctionCallItem functionCall) continue;
            hadFunctionCalls = true;

            var callId = functionCall.CallId;
            var name = functionCall.FunctionName;
            var args = functionCall.FunctionArguments?.ToString();

            _logger.LogDebug("Tool call: {Name}({CallId})", name, callId);
            DebugLog?.Invoke(this, $"Tool: {name}");

            try
            {
                var context = CreateToolContext();
                var result = await _dispatcher.ExecuteAsync(name, args, context, ct);

                // Send tool result back via raw SDK command
                var outputItem = OpenAI.Realtime.RealtimeItem.CreateFunctionCallOutputItem(
                    callId: callId, functionOutput: result);
                var createCmd = new OpenAI.Realtime.RealtimeClientCommandConversationItemCreate(outputItem);
                var sdkSession = GetSdkSession(session);
                await sdkSession.SendCommandAsync(createCmd, ct);
                _logger.LogDebug("Tool result sent: {Name}", name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tool {Name} failed", name);
                var errorOutput = OpenAI.Realtime.RealtimeItem.CreateFunctionCallOutputItem(
                    callId: callId,
                    functionOutput: $"{{\"error\": \"{ex.Message}\"}}");
                var createCmd = new OpenAI.Realtime.RealtimeClientCommandConversationItemCreate(errorOutput);
                var sdkSession = GetSdkSession(session);
                await sdkSession.SendCommandAsync(createCmd, ct);
            }
        }

        if (hadFunctionCalls)
        {
            // Trigger a new response after sending all tool results
            await session.SendAsync(new CreateResponseRealtimeClientMessage(), ct);
        }
    }

    /// <summary>Get the underlying SDK session client for operations not exposed by MAF.</summary>
    private static OpenAI.Realtime.RealtimeSessionClient GetSdkSession(IRealtimeClientSession session)
    {
        if (session is OpenAIRealtimeClientSession openAiSession)
        {
            // The session wraps a RealtimeSessionClient — access via GetService
            var sdkSession = openAiSession.GetService<OpenAI.Realtime.RealtimeSessionClient>();
            if (sdkSession is not null) return sdkSession;
        }
        throw new InvalidOperationException("Cannot access SDK session for tool result dispatch.");
    }

    private async Task HandleSpeechStartedAsync(
        IRealtimeClientSession session,
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
                    // Truncation requires SDK access — not in MAF abstraction
                    var sdkSession = GetSdkSession(session);
                    await sdkSession.TruncateItemAsync(itemId, 0, TimeSpan.FromMilliseconds(playedMs), ct);
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
                if (_session is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else if (_session is IDisposable disposable)
                    disposable.Dispose();

                var ct = _cts?.Token ?? CancellationToken.None;
                var options = BuildSessionOptions();
                _session = await _realtimeFactory.CreateSessionAsync(options, ct);
                _messageLoop = Task.Run(() => RunMessageLoopAsync(_session, ct));
                _voiceIn.SetAudioSink(async (pcm, innerCt) =>
                {
                    await _session!.SendAsync(
                        new InputAudioBufferAppendRealtimeClientMessage(
                            new DataContent(pcm, "audio/pcm")), innerCt);
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
                delay *= 2;
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
        CaptureFrame = FrameCaptureFunc ?? _cameraManager.CaptureFrameAsync,
        Session = Session,
        Log = msg => DebugLog?.Invoke(this, msg),
    };

    public async Task SendTextInputAsync(string text, CancellationToken ct = default)
    {
        if (_session is null)
            throw new InvalidOperationException("Session is not active.");

        await _session.SendAsync(new CreateConversationItemRealtimeClientMessage(
            new RealtimeConversationItem([new TextContent(text)], role: ChatRole.User)), ct);
        await _session.SendAsync(new CreateResponseRealtimeClientMessage(), ct);
    }

}