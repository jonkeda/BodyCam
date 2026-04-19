using System.Text.Json;
using BodyCam.Models;
using BodyCam.Services;
using Microsoft.Extensions.AI;
using System.ClientModel;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Fixtures;

public class RealtimeFixture : IAsyncLifetime
{
    private readonly IRealtimeClient _client;
    private IRealtimeClientSession? _session;
    private Task? _messageLoop;
    private CancellationTokenSource? _cts;

    private readonly List<(string Type, string Json)> _events = [];
    private readonly List<(string Type, DateTimeOffset Timestamp)> _timestampedEvents = [];
    private readonly List<string> _inputTranscripts = [];
    private readonly List<string> _outputTranscripts = [];
    private readonly List<string> _outputTranscriptDeltas = [];
    private readonly List<byte[]> _audioChunks = [];
    private readonly List<FunctionCallInfo> _functionCalls = [];
    private readonly List<RealtimeResponseInfo> _responseDones = [];
    private readonly List<string> _errors = [];

    private TaskCompletionSource _responseDoneTcs = new();
    private TaskCompletionSource _inputTranscriptTcs = new();
    private TaskCompletionSource _functionCallTcs = new();
    private TaskCompletionSource _firstAudioTcs = new();

    private ITestOutputHelper? _output;

    public IReadOnlyList<(string Type, string Json)> Events => _events;
    public IReadOnlyList<(string Type, DateTimeOffset Timestamp)> TimestampedEvents => _timestampedEvents;
    public IReadOnlyList<string> InputTranscripts => _inputTranscripts;
    public IReadOnlyList<string> OutputTranscripts => _outputTranscripts;
    public IReadOnlyList<string> OutputTranscriptDeltas => _outputTranscriptDeltas;
    public IReadOnlyList<byte[]> AudioChunks => _audioChunks;
    public IReadOnlyList<FunctionCallInfo> FunctionCalls => _functionCalls;
    public IReadOnlyList<RealtimeResponseInfo> ResponseDones => _responseDones;
    public IReadOnlyList<string> Errors => _errors;

    public RealtimeFixture()
    {
        var settings = LoadSettings();
        var apiKey = LoadApiKey(settings.Provider);
        _client = BuildClient(apiKey, settings);
    }

    public void SetOutput(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _cts = new CancellationTokenSource();
        // Create session without options to avoid the SDK sending session.type
        // (a field Azure doesn't support). Configure the session via raw JSON instead.
        _session = await _client.CreateSessionAsync(null, _cts.Token);
        _messageLoop = Task.Run(() => RunMessageLoopAsync(_cts.Token));
        await SendRawAsync(BuildRawSessionUpdate(), _cts.Token);
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
        _timestampedEvents.Clear();
        _inputTranscripts.Clear();
        _outputTranscripts.Clear();
        _outputTranscriptDeltas.Clear();
        _audioChunks.Clear();
        _functionCalls.Clear();
        _responseDones.Clear();
        _errors.Clear();
        _responseDoneTcs = new TaskCompletionSource();
        _inputTranscriptTcs = new TaskCompletionSource();
        _functionCallTcs = new TaskCompletionSource();
        _firstAudioTcs = new TaskCompletionSource();
    }

    public Task WaitForResponseAsync(TimeSpan? timeout = null)
        => WaitWithTimeout(_responseDoneTcs.Task, timeout ?? TimeSpan.FromSeconds(30), "response.done");

    public Task WaitForInputTranscriptAsync(TimeSpan? timeout = null)
        => WaitWithTimeout(_inputTranscriptTcs.Task, timeout ?? TimeSpan.FromSeconds(30), "input transcript");

    public Task WaitForFunctionCallAsync(TimeSpan? timeout = null)
        => WaitWithTimeout(_functionCallTcs.Task, timeout ?? TimeSpan.FromSeconds(30), "function call");

    public Task WaitForFirstAudioAsync(TimeSpan? timeout = null)
        => WaitWithTimeout(_firstAudioTcs.Task, timeout ?? TimeSpan.FromSeconds(30), "first audio delta");

    public async Task WaitForInputTranscriptOrTimeoutAsync(TimeSpan timeout)
    {
        await Task.WhenAny(_inputTranscriptTcs.Task, Task.Delay(timeout));
    }

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

    public Task CancelResponseAsync(CancellationToken ct = default)
        => SendRawAsync(new { type = "response.cancel" }, ct);

    public IReadOnlyList<(string Type, string Json)> GetEventsByType(string type)
        => _events.Where(e => e.Type == type).ToList();

    public int IndexOfEvent(string type)
        => _events.FindIndex(e => e.Type == type);

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
                var rawJson = SerializeRaw(message.RawRepresentation);
                var eventType = ExtractType(rawJson);
                if (eventType == "unknown")
                    eventType = MapSdkType(message.Type);
                _events.Add((eventType, rawJson));
                _timestampedEvents.Add((eventType, DateTimeOffset.UtcNow));
                _output?.WriteLine($"[{_events.Count}] raw={eventType} type={message.Type}");
                ProcessMessage(message, eventType, rawJson);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { _output?.WriteLine($"[LOOP ERROR] {ex.Message}"); }
    }

    internal static string MapSdkType(RealtimeServerMessageType type)
    {
        if (type == RealtimeServerMessageType.ResponseCreated) return "response.created";
        if (type == RealtimeServerMessageType.ResponseDone) return "response.done";
        if (type == RealtimeServerMessageType.ResponseOutputItemAdded) return "response.output_item.added";
        if (type == RealtimeServerMessageType.ResponseOutputItemDone) return "response.output_item.done";
        if (type == RealtimeServerMessageType.OutputAudioDelta) return "response.audio.delta";
        if (type == RealtimeServerMessageType.OutputAudioTranscriptionDelta) return "response.audio_transcript.delta";
        if (type == RealtimeServerMessageType.OutputAudioTranscriptionDone) return "response.audio_transcript.done";
        if (type == RealtimeServerMessageType.OutputTextDelta) return "response.output_text.delta";
        if (type == RealtimeServerMessageType.OutputTextDone) return "response.output_text.done";
        if (type == RealtimeServerMessageType.InputAudioTranscriptionCompleted) return "conversation.item.input_audio_transcription.completed";
        if (type == RealtimeServerMessageType.Error) return "error";
        return type.ToString();
    }

    private void ProcessMessage(RealtimeServerMessage message, string eventType, string rawJson)
    {
        var type = message.Type;
        var audioHandled = false;
        var transcriptDeltaHandled = false;
        var transcriptDoneHandled = false;
        var functionCallHandled = false;

        if (type == RealtimeServerMessageType.OutputAudioDelta)
        {
            var m = (OutputTextAudioRealtimeServerMessage)message;
            if (m.Audio is { } audio)
            {
                _audioChunks.Add(Convert.FromBase64String(audio));
                _firstAudioTcs.TrySetResult();
                audioHandled = true;
            }
        }
        else if (type == RealtimeServerMessageType.OutputAudioTranscriptionDelta)
        {
            var m = (OutputTextAudioRealtimeServerMessage)message;
            if (m.Text is { } delta)
            {
                _outputTranscriptDeltas.Add(delta);
                transcriptDeltaHandled = true;
            }
        }
        else if (type == RealtimeServerMessageType.OutputAudioTranscriptionDone)
        {
            var m = (OutputTextAudioRealtimeServerMessage)message;
            if (m.Text is { } t)
            {
                _outputTranscripts.Add(t);
                _output?.WriteLine($"[OUTPUT] {t}");
                transcriptDoneHandled = true;
            }
        }
        else if (type == RealtimeServerMessageType.OutputTextDone)
        {
            var m = (OutputTextAudioRealtimeServerMessage)message;
            if (m.Text is { } t)
            {
                _outputTranscripts.Add(t);
                _output?.WriteLine($"[OUTPUT-TEXT] {t}");
                transcriptDoneHandled = true;
            }
        }
        else if (type == RealtimeServerMessageType.InputAudioTranscriptionCompleted)
        {
            var m = (InputAudioTranscriptionRealtimeServerMessage)message;
            if (m.Transcription is { } t)
            {
                _inputTranscripts.Add(t);
                _inputTranscriptTcs.TrySetResult();
                _output?.WriteLine($"[INPUT] {t}");
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
                        functionCallHandled = true;
                    }
                }
            }
        }

        // Raw JSON fallbacks — only fire when typed handlers didn't capture the data
        if (!functionCallHandled && eventType == "response.function_call_arguments.done")
            TryParseFunctionCallFromJson(rawJson);

        if (!transcriptDoneHandled && (eventType == "response.output_text.done" || eventType == "response.audio_transcript.done"))
            TryParseOutputTextFromJson(rawJson);
        if (!transcriptDeltaHandled && (eventType == "response.output_text.delta" || eventType == "response.audio_transcript.delta"))
            TryParseOutputTextDeltaFromJson(rawJson);
        if (!audioHandled && eventType == "response.audio.delta")
            TryParseAudioDeltaFromJson(rawJson);
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

    private void TryParseOutputTextFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // response.output_text.done uses "text", response.audio_transcript.done uses "transcript"
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

    private void TryParseOutputTextDeltaFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var delta = root.TryGetProperty("delta", out var d) ? d.GetString() : null;
            if (delta is not null)
                _outputTranscriptDeltas.Add(delta);
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
            {
                _audioChunks.Add(Convert.FromBase64String(delta));
                _firstAudioTcs.TrySetResult();
            }
        }
        catch { /* ignore parse failures */ }
    }

    internal static string SerializeRaw(object? raw)
    {
        if (raw is null) return "{}";
        try
        {
            if (raw is BinaryData bd) return bd.ToString();
            // SDK RealtimeServerUpdate types use ModelReaderWriter for serialization
            var data = System.ClientModel.Primitives.ModelReaderWriter.Write(
                raw, System.ClientModel.Primitives.ModelReaderWriterOptions.Json);
            return data.ToString();
        }
        catch
        {
            try { return JsonSerializer.Serialize(raw, raw.GetType()); }
            catch { return raw.ToString() ?? "{}"; }
        }
    }

    internal static string ExtractType(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var t))
                return t.GetString() ?? "unknown";
        }
        catch { }
        return "unknown";
    }

    protected virtual object BuildRawSessionUpdate() => new
    {
        type = "session.update",
        session = new
        {
            instructions = "You are a helpful assistant with vision capabilities. When the user asks about their surroundings, what they see, or asks you to look at something, call the describe_scene function. When the user asks for deep analysis, call the deep_analysis function.",
            voice = "marin",
            input_audio_format = "pcm16",
            output_audio_format = "pcm16",
            modalities = new[] { "audio", "text" },
            input_audio_transcription = new { model = "gpt-realtime-transcribe" },
            turn_detection = new { type = "server_vad" },
            tools = new object[]
            {
                new
                {
                    type = "function",
                    name = "describe_scene",
                    description = "Capture and describe the current camera view. Use this when the user asks about their surroundings or what they can see.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            query = new { type = "string", description = "Optional specific question about the scene." }
                        }
                    }
                },
                new
                {
                    type = "function",
                    name = "deep_analysis",
                    description = "Perform a deep, thorough analysis of a topic or question.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            topic = new { type = "string", description = "The topic or question to analyze deeply." }
                        },
                        required = new[] { "topic" }
                    }
                }
            }
        }
    };

    private static async Task WaitWithTimeout(Task task, TimeSpan timeout, string description)
    {
        using var cts = new CancellationTokenSource(timeout);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));
        if (completed != task)
            throw new TimeoutException($"Timed out waiting for {description} after {timeout.TotalSeconds}s");
        await task;
    }

    internal static IRealtimeClient BuildClient(string apiKey, AppSettings settings)
    {
        if (settings.Provider == OpenAiProvider.Azure)
        {
            var rtOpts = new OpenAI.Realtime.RealtimeClientOptions
            {
                Endpoint = new Uri($"{settings.AzureEndpoint!.TrimEnd('/')}/openai/realtime")
            };
            var sdkClient = new BodyCam.Services.AzureRealtimeClient(
                apiKey, rtOpts,
                settings.AzureRealtimeDeploymentName!,
                settings.AzureApiVersion);
            return new OpenAIRealtimeClient(sdkClient, settings.AzureRealtimeDeploymentName!);
        }
        return new OpenAIRealtimeClient(apiKey, settings.RealtimeModel);
    }

    internal static AppSettings LoadSettings()
    {
        var settings = new AppSettings();
        var provider = DotEnvReader.Read("OPENAI_PROVIDER");
        if (string.Equals(provider, "azure", StringComparison.OrdinalIgnoreCase))
        {
            settings.Provider = OpenAiProvider.Azure;
            settings.AzureEndpoint = DotEnvReader.Read("AZURE_OPENAI_ENDPOINT");
            settings.AzureRealtimeDeploymentName = DotEnvReader.Read("AZURE_OPENAI_DEPLOYMENT");
            settings.AzureChatDeploymentName = DotEnvReader.Read("AZURE_OPENAI_CHAT_DEPLOYMENT");
            settings.AzureVisionDeploymentName = DotEnvReader.Read("AZURE_OPENAI_VISION_DEPLOYMENT");
            var version = DotEnvReader.Read("AZURE_OPENAI_API_VERSION");
            if (version is not null) settings.AzureApiVersion = version;
        }
        return settings;
    }

    internal static string LoadApiKey(OpenAiProvider provider)
    {
        var key = provider == OpenAiProvider.Azure
            ? DotEnvReader.Read("AZURE_OPENAI_API_KEY")
            : DotEnvReader.Read("OPENAI_API_KEY");
        return key ?? throw new InvalidOperationException(
            $"API key not found. Set {(provider == OpenAiProvider.Azure ? "AZURE_OPENAI_API_KEY" : "OPENAI_API_KEY")}.");
    }
}
