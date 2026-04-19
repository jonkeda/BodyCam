using System.Text.Json;
using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tools;
using Microsoft.Extensions.AI;
using System.ClientModel;
using Xunit;
using Xunit.Abstractions;

using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace BodyCam.RealTests.Fixtures;

/// <summary>
/// Shared fixture that connects to the live Realtime API via MAF
/// with all M5 tool definitions registered.
/// </summary>
public class M5ToolFixture : IAsyncLifetime
{
    private readonly IRealtimeClient _client;
    private readonly ToolDispatcher _dispatcher;
    private IRealtimeClientSession? _session;
    private Task? _messageLoop;
    private CancellationTokenSource? _cts;

    private readonly List<(string Type, string Json)> _events = [];
    private readonly List<string> _outputTranscripts = [];
    private readonly List<byte[]> _audioChunks = [];
    private readonly List<FunctionCallInfo> _functionCalls = [];
    private readonly List<RealtimeResponseInfo> _responseDones = [];
    private readonly List<string> _errors = [];

    private TaskCompletionSource _responseDoneTcs = new();
    private TaskCompletionSource _functionCallTcs = new();

    private ITestOutputHelper? _output;

    public ToolDispatcher Dispatcher => _dispatcher;
    public IReadOnlyList<(string Type, string Json)> Events => _events;
    public IReadOnlyList<string> OutputTranscripts => _outputTranscripts;
    public IReadOnlyList<byte[]> AudioChunks => _audioChunks;
    public IReadOnlyList<FunctionCallInfo> FunctionCalls => _functionCalls;
    public IReadOnlyList<RealtimeResponseInfo> ResponseDones => _responseDones;
    public IReadOnlyList<string> Errors => _errors;

    public M5ToolFixture()
    {
        var settings = RealtimeFixture.LoadSettings();
        var apiKey = RealtimeFixture.LoadApiKey(settings.Provider);

        // Build all M5 tools — we only need their definitions (Name, Description, ParameterSchema).
        var stubChatClient = new StubChatClient();
        var vision = new VisionAgent(stubChatClient, settings);
        var conversation = new ConversationAgent(stubChatClient, settings);
        var memoryStore = new MemoryStore(Path.Combine(Path.GetTempPath(), $"bodycam_test_{Guid.NewGuid():N}.json"));

        var tools = new ITool[]
        {
            new DescribeSceneTool(vision),
            new DeepAnalysisTool(conversation),
            new ReadTextTool(vision),
            new TakePhotoTool(),
            new SaveMemoryTool(memoryStore),
            new RecallMemoryTool(memoryStore),
            new SetTranslationModeTool(),
            new MakePhoneCallTool(),
            new SendMessageTool(),
            new LookupAddressTool(),
            new FindObjectTool(vision),
            new NavigateToTool(),
            new StartSceneWatchTool(vision),
        };

        _dispatcher = new ToolDispatcher(tools);
        _client = RealtimeFixture.BuildClient(apiKey, settings);
    }

    public void SetOutput(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _cts = new CancellationTokenSource();

        var toolDefs = _dispatcher.GetToolDefinitions();

        // Build raw session.update with tool schemas (bypasses SDK's session.type field
        // which Azure doesn't support)
        var rawTools = toolDefs.Select(d => new
        {
            type = "function",
            name = d.Name,
            description = d.Description,
            parameters = JsonSerializer.Deserialize<JsonElement>(d.ParametersJson),
        }).ToArray();

        // Create session without options to avoid SDK sending session.type
        _session = await _client.CreateSessionAsync(null, _cts.Token);
        _messageLoop = Task.Run(() => RunMessageLoopAsync(_cts.Token));

        // Send raw session.update with full config including tools
        var sessionUpdate = JsonSerializer.Serialize(new
        {
            type = "session.update",
            session = new
            {
                instructions = "You are a helpful assistant with access to tools.",
                voice = "marin",
                input_audio_format = "pcm16",
                output_audio_format = "pcm16",
                modalities = new[] { "audio", "text" },
                input_audio_transcription = new { model = "gpt-realtime-transcribe" },
                turn_detection = new { type = "server_vad" },
                tools = rawTools,
            }
        });
        var msg = new RealtimeClientMessage { RawRepresentation = sessionUpdate };
        await _session.SendAsync(msg, _cts.Token);
    }

    public async Task DisposeAsync()
    {
        _cts?.Cancel();
        if (_messageLoop is not null)
        {
            try { await _messageLoop; } catch (OperationCanceledException) { }
            _messageLoop = null;
        }
        if (_session is not null) { await _session.DisposeAsync(); _session = null; }
        _cts?.Dispose();
    }

    public void Reset()
    {
        _events.Clear();
        _outputTranscripts.Clear();
        _audioChunks.Clear();
        _functionCalls.Clear();
        _responseDones.Clear();
        _errors.Clear();
        _responseDoneTcs = new TaskCompletionSource();
        _functionCallTcs = new TaskCompletionSource();
    }

    public Task WaitForResponseAsync(TimeSpan? timeout = null)
        => WaitWithTimeout(_responseDoneTcs.Task, timeout ?? TimeSpan.FromSeconds(30), "response.done");

    public Task WaitForFunctionCallAsync(TimeSpan? timeout = null)
        => WaitWithTimeout(_functionCallTcs.Task, timeout ?? TimeSpan.FromSeconds(30), "function call");

    public async Task SendTextInputAsync(string text, CancellationToken ct = default)
    {
        await SendRawAsync(new { type = "conversation.item.create", item = new { type = "message", role = "user", content = new[] { new { type = "input_text", text } } } }, ct);
        await SendRawAsync(new { type = "response.create" }, ct);
    }

    public async Task SendFunctionCallOutputAsync(string callId, string output, CancellationToken ct = default)
    {
        await SendRawAsync(new { type = "conversation.item.create", item = new { type = "function_call_output", call_id = callId, output } }, ct);
        await SendRawAsync(new { type = "response.create" }, ct);
    }

    // --- Internals ---

    private Task SendRawAsync(object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        var msg = new RealtimeClientMessage { RawRepresentation = json };
        return _session!.SendAsync(msg, ct);
    }

    private async Task RunMessageLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var message in _session!.GetStreamingResponseAsync(ct))
            {
                var rawJson = RealtimeFixture.SerializeRaw(message.RawRepresentation);
                var eventType = RealtimeFixture.ExtractType(rawJson);
                if (eventType == "unknown")
                    eventType = RealtimeFixture.MapSdkType(message.Type);
                _events.Add((eventType, rawJson));
                _output?.WriteLine($"[{_events.Count}] {eventType}");
                ProcessMessage(message, eventType, rawJson);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { _output?.WriteLine($"[LOOP ERROR] {ex.Message}"); }
    }

    private void ProcessMessage(RealtimeServerMessage message, string eventType, string rawJson)
    {
        var type = message.Type;

        if (type == RealtimeServerMessageType.OutputAudioDelta)
        {
            var m = (OutputTextAudioRealtimeServerMessage)message;
            if (m.Audio is { } audio)
                _audioChunks.Add(Convert.FromBase64String(audio));
        }
        else if (type == RealtimeServerMessageType.OutputAudioTranscriptionDone)
        {
            var m = (OutputTextAudioRealtimeServerMessage)message;
            if (m.Text is { } t)
            {
                _outputTranscripts.Add(t);
                _output?.WriteLine($"[OUTPUT] {t}");
            }
        }
        else if (type == RealtimeServerMessageType.ResponseDone)
        {
            var m = (ResponseCreatedRealtimeServerMessage)message;
            var info = new RealtimeResponseInfo { ResponseId = m.ResponseId ?? "" };
            _responseDones.Add(info);
            _responseDoneTcs.TrySetResult();
            _output?.WriteLine($"[DONE] {info.ResponseId}");
        }
        else if (type == RealtimeServerMessageType.Error)
        {
            var m = (ErrorRealtimeServerMessage)message;
            var error = m.Error?.Message ?? "unknown error";
            _errors.Add(error);
            _output?.WriteLine($"[ERROR] {error}");
        }
        else if (type == RealtimeServerMessageType.ResponseOutputItemDone)
        {
            var m = (ResponseOutputItemRealtimeServerMessage)message;
            if (m.Item?.Contents is { } contents)
            {
                foreach (var c in contents)
                {
                    if (c is FunctionCallContent fcc)
                    {
                        var argsJson = fcc.Arguments is not null ? JsonSerializer.Serialize(fcc.Arguments) : "{}";
                        var info = new FunctionCallInfo(fcc.CallId ?? "", fcc.Name ?? "", argsJson);
                        _functionCalls.Add(info);
                        _functionCallTcs.TrySetResult();
                        _output?.WriteLine($"[FUNC] {info.Name}({info.Arguments})");
                    }
                }
            }
        }

        // Fallback: parse function call from raw JSON
        if (eventType == "response.function_call_arguments.done")
            TryParseFunctionCallFromJson(rawJson);

        // Fallback: parse audio/text output from raw JSON for events MAF maps as RawContentOnly
        if (eventType == "response.output_text.done" || eventType == "response.audio_transcript.done")
            TryParseOutputTextFromJson(rawJson);
        if (eventType == "response.audio.delta")
            TryParseAudioDeltaFromJson(rawJson);
    }

    private void TryParseOutputTextFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var text = root.TryGetProperty("text", out var t) ? t.GetString()
                     : root.TryGetProperty("transcript", out var tr) ? tr.GetString()
                     : null;
            if (text is not null)
            {
                _outputTranscripts.Add(text);
                _output?.WriteLine($"[OUTPUT-RAW] {text}");
            }
        }
        catch { /* ignore parse failures */ }
    }

    private void TryParseAudioDeltaFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var delta = root.TryGetProperty("delta", out var d) ? d.GetString() : null;
            if (delta is not null)
                _audioChunks.Add(Convert.FromBase64String(delta));
        }
        catch { /* ignore parse failures */ }
    }

    private void TryParseFunctionCallFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var callId = root.TryGetProperty("call_id", out var c) ? c.GetString() ?? "" : "";
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var args = root.TryGetProperty("arguments", out var a) ? a.GetString() ?? "{}" : "{}";
            if (!_functionCalls.Any(fc => fc.CallId == callId))
            {
                var info = new FunctionCallInfo(callId, name, args);
                _functionCalls.Add(info);
                _functionCallTcs.TrySetResult();
                _output?.WriteLine($"[FUNC-RAW] {info.Name}({info.Arguments})");
            }
        }
        catch { /* ignore parse failures */ }
    }

    private static async Task WaitWithTimeout(Task task, TimeSpan timeout, string description)
    {
        using var cts = new CancellationTokenSource(timeout);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));
        if (completed != task)
            throw new TimeoutException($"Timed out waiting for {description} after {timeout.TotalSeconds}s");
        await task;
    }

    /// <summary>
    /// Stub chat client — tools won't actually execute during real API tests.
    /// </summary>
    private sealed class StubChatClient : IChatClient
    {
        public ChatClientMetadata Metadata => new("stub");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<AIChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new AIChatMessage(ChatRole.Assistant, "stub")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<AIChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
