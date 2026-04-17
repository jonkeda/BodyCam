using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tools;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Fixtures;

/// <summary>
/// Shared fixture that connects a RealtimeClient to the live API,
/// collects all events, and provides helper methods for test assertions.
/// </summary>
public class RealtimeFixture : IAsyncLifetime
{
    private readonly RealtimeClient _client;
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

    public RealtimeClient Client => _client;
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
        var apiKeyService = new StaticApiKeyService(LoadApiKey(settings.Provider));
        _client = new RealtimeClient(apiKeyService, settings, new ToolDispatcher([]));
    }

    public void SetOutput(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        // Subscribe to all events for collection
        _client.RawMessageReceived += OnRawMessage;
        _client.AudioDelta += OnAudioDelta;
        _client.OutputTranscriptDelta += OnOutputTranscriptDelta;
        _client.OutputTranscriptCompleted += OnOutputTranscriptCompleted;
        _client.InputTranscriptCompleted += OnInputTranscriptCompleted;
        _client.ResponseDone += OnResponseDone;
        _client.ErrorOccurred += OnError;
        _client.FunctionCallReceived += OnFunctionCallReceived;

        await _client.ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        _client.RawMessageReceived -= OnRawMessage;
        _client.AudioDelta -= OnAudioDelta;
        _client.OutputTranscriptDelta -= OnOutputTranscriptDelta;
        _client.OutputTranscriptCompleted -= OnOutputTranscriptCompleted;
        _client.InputTranscriptCompleted -= OnInputTranscriptCompleted;
        _client.ResponseDone -= OnResponseDone;
        _client.ErrorOccurred -= OnError;
        _client.FunctionCallReceived -= OnFunctionCallReceived;

        await _client.DisconnectAsync();
    }

    /// <summary>
    /// Reset all collected events. Call between tests.
    /// </summary>
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

    /// <summary>
    /// Wait for a response.done event.
    /// </summary>
    public Task WaitForResponseAsync(TimeSpan? timeout = null)
    {
        var t = timeout ?? TimeSpan.FromSeconds(30);
        return WaitWithTimeout(_responseDoneTcs.Task, t, "response.done");
    }

    /// <summary>
    /// Wait for an input transcription to complete.
    /// </summary>
    public Task WaitForInputTranscriptAsync(TimeSpan? timeout = null)
    {
        var t = timeout ?? TimeSpan.FromSeconds(30);
        return WaitWithTimeout(_inputTranscriptTcs.Task, t, "input transcript");
    }

    /// <summary>
    /// Wait for a function call to be received.
    /// </summary>
    public Task WaitForFunctionCallAsync(TimeSpan? timeout = null)
    {
        var t = timeout ?? TimeSpan.FromSeconds(30);
        return WaitWithTimeout(_functionCallTcs.Task, t, "function call");
    }

    /// <summary>
    /// Wait for the first audio delta chunk.
    /// </summary>
    public Task WaitForFirstAudioAsync(TimeSpan? timeout = null)
    {
        var t = timeout ?? TimeSpan.FromSeconds(30);
        return WaitWithTimeout(_firstAudioTcs.Task, t, "first audio delta");
    }

    /// <summary>
    /// Wait for input transcript, returning silently if timeout expires (input transcript may not arrive for text input).
    /// </summary>
    public async Task WaitForInputTranscriptOrTimeoutAsync(TimeSpan timeout)
    {
        await Task.WhenAny(_inputTranscriptTcs.Task, Task.Delay(timeout));
    }

    /// <summary>
    /// Send text input and trigger a response.
    /// </summary>
    public Task SendTextInputAsync(string text, CancellationToken ct = default)
        => _client.SendTextInputAsync(text, ct);

    /// <summary>
    /// Send function call output and trigger a continuation response.
    /// </summary>
    public Task SendFunctionCallOutputAsync(string callId, string output, CancellationToken ct = default)
        => _client.SendFunctionCallOutputAsync(callId, output, ct);

    /// <summary>
    /// Get events of a specific type.
    /// </summary>
    public IReadOnlyList<(string Type, string Json)> GetEventsByType(string type)
        => _events.Where(e => e.Type == type).ToList();

    /// <summary>
    /// Get the index of the first event of a given type.
    /// </summary>
    public int IndexOfEvent(string type)
        => _events.FindIndex(e => e.Type == type);

    // --- Event handlers ---

    private void OnRawMessage(object? sender, string json)
    {
        var type = BodyCam.Services.Realtime.ServerEventParser.GetType(json) ?? "unknown";
        _events.Add((type, json));
        _timestampedEvents.Add((type, DateTimeOffset.UtcNow));
        _output?.WriteLine($"[{_events.Count}] {type}");
    }

    private void OnAudioDelta(object? sender, byte[] data)
    {
        _audioChunks.Add(data);
        _firstAudioTcs.TrySetResult();
    }

    private void OnOutputTranscriptDelta(object? sender, string delta)
    {
        _outputTranscriptDeltas.Add(delta);
    }

    private void OnOutputTranscriptCompleted(object? sender, string transcript)
    {
        _outputTranscripts.Add(transcript);
        _output?.WriteLine($"[OUTPUT] {transcript}");
    }

    private void OnInputTranscriptCompleted(object? sender, string transcript)
    {
        _inputTranscripts.Add(transcript);
        _inputTranscriptTcs.TrySetResult();
        _output?.WriteLine($"[INPUT] {transcript}");
    }

    private void OnResponseDone(object? sender, RealtimeResponseInfo info)
    {
        _responseDones.Add(info);
        _responseDoneTcs.TrySetResult();
        _output?.WriteLine($"[DONE] {info.ResponseId}");
    }

    private void OnError(object? sender, string error)
    {
        _errors.Add(error);
        _output?.WriteLine($"[ERROR] {error}");
    }

    private void OnFunctionCallReceived(object? sender, FunctionCallInfo info)
    {
        _functionCalls.Add(info);
        _functionCallTcs.TrySetResult();
        _output?.WriteLine($"[FUNC] {info.Name}({info.Arguments})");
    }

    // --- Helpers ---

    private static async Task WaitWithTimeout(Task task, TimeSpan timeout, string description)
    {
        using var cts = new CancellationTokenSource(timeout);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));
        if (completed != task)
            throw new TimeoutException($"Timed out waiting for {description} after {timeout.TotalSeconds}s");
        await task; // Propagate any exceptions
    }

    private static AppSettings LoadSettings()
    {
        var settings = new AppSettings();
        var provider = ReadEnvValue("OPENAI_PROVIDER");

        if (string.Equals(provider, "azure", StringComparison.OrdinalIgnoreCase))
        {
            settings.Provider = OpenAiProvider.Azure;
            settings.AzureEndpoint = ReadEnvValue("AZURE_OPENAI_ENDPOINT");
            settings.AzureRealtimeDeploymentName = ReadEnvValue("AZURE_OPENAI_DEPLOYMENT");
            settings.AzureChatDeploymentName = ReadEnvValue("AZURE_OPENAI_CHAT_DEPLOYMENT");
            settings.AzureVisionDeploymentName = ReadEnvValue("AZURE_OPENAI_VISION_DEPLOYMENT");
            var version = ReadEnvValue("AZURE_OPENAI_API_VERSION");
            if (version is not null) settings.AzureApiVersion = version;
        }

        return settings;
    }

    private static string LoadApiKey(OpenAiProvider provider)
    {
        var key = provider == OpenAiProvider.Azure
            ? ReadEnvValue("AZURE_OPENAI_API_KEY")
            : ReadEnvValue("OPENAI_API_KEY");
        return key ?? throw new InvalidOperationException(
            $"API key not found in .env. Set {(provider == OpenAiProvider.Azure ? "AZURE_OPENAI_API_KEY" : "OPENAI_API_KEY")}.");
    }

    private static string? ReadEnvValue(string key)
    {
        return DotEnvReader.Read(key);
    }

    /// <summary>
    /// Simple IApiKeyService that returns a fixed key.
    /// </summary>
    private sealed class StaticApiKeyService(string apiKey) : IApiKeyService
    {
        public bool HasKey => true;
        public Task<string?> GetApiKeyAsync() => Task.FromResult<string?>(apiKey);
        public Task SetApiKeyAsync(string apiKey) => Task.CompletedTask;
        public Task ClearApiKeyAsync() => Task.CompletedTask;
    }
}
