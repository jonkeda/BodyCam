using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tools;
using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace BodyCam.RealTests.Fixtures;

/// <summary>
/// Shared fixture that connects a RealtimeClient to the live API
/// with all M5 tool definitions registered. Tool execution is not tested here —
/// we only need the definitions sent to the model so it can trigger function calls.
/// </summary>
public class M5ToolFixture : IAsyncLifetime
{
    private readonly RealtimeClient _client;
    private readonly ToolDispatcher _dispatcher;
    private readonly List<(string Type, string Json)> _events = [];
    private readonly List<string> _outputTranscripts = [];
    private readonly List<byte[]> _audioChunks = [];
    private readonly List<FunctionCallInfo> _functionCalls = [];
    private readonly List<RealtimeResponseInfo> _responseDones = [];
    private readonly List<string> _errors = [];

    private TaskCompletionSource _responseDoneTcs = new();
    private TaskCompletionSource _functionCallTcs = new();

    private ITestOutputHelper? _output;

    public RealtimeClient Client => _client;
    public ToolDispatcher Dispatcher => _dispatcher;
    public IReadOnlyList<(string Type, string Json)> Events => _events;
    public IReadOnlyList<string> OutputTranscripts => _outputTranscripts;
    public IReadOnlyList<byte[]> AudioChunks => _audioChunks;
    public IReadOnlyList<FunctionCallInfo> FunctionCalls => _functionCalls;
    public IReadOnlyList<RealtimeResponseInfo> ResponseDones => _responseDones;
    public IReadOnlyList<string> Errors => _errors;

    public M5ToolFixture()
    {
        var settings = LoadSettings();
        var apiKeyService = new StaticApiKeyService(LoadApiKey(settings.Provider));

        // Build all M5 tools — we only need their definitions (Name, Description, ParameterSchema).
        // Dependencies are stubs; the tools won't actually execute via the fixture.
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
        _client = new RealtimeClient(apiKeyService, settings, _dispatcher);
    }

    public void SetOutput(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _client.RawMessageReceived += OnRawMessage;
        _client.AudioDelta += OnAudioDelta;
        _client.OutputTranscriptCompleted += OnOutputTranscriptCompleted;
        _client.ResponseDone += OnResponseDone;
        _client.ErrorOccurred += OnError;
        _client.FunctionCallReceived += OnFunctionCallReceived;

        await _client.ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        _client.RawMessageReceived -= OnRawMessage;
        _client.AudioDelta -= OnAudioDelta;
        _client.OutputTranscriptCompleted -= OnOutputTranscriptCompleted;
        _client.ResponseDone -= OnResponseDone;
        _client.ErrorOccurred -= OnError;
        _client.FunctionCallReceived -= OnFunctionCallReceived;

        await _client.DisconnectAsync();
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

    public Task SendTextInputAsync(string text, CancellationToken ct = default)
        => _client.SendTextInputAsync(text, ct);

    public Task SendFunctionCallOutputAsync(string callId, string output, CancellationToken ct = default)
        => _client.SendFunctionCallOutputAsync(callId, output, ct);

    // --- Event handlers ---

    private void OnRawMessage(object? sender, string json)
    {
        var type = BodyCam.Services.Realtime.ServerEventParser.GetType(json) ?? "unknown";
        _events.Add((type, json));
        _output?.WriteLine($"[{_events.Count}] {type}");
    }

    private void OnAudioDelta(object? sender, byte[] data)
    {
        _audioChunks.Add(data);
    }

    private void OnOutputTranscriptCompleted(object? sender, string transcript)
    {
        _outputTranscripts.Add(transcript);
        _output?.WriteLine($"[OUTPUT] {transcript}");
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
        await task;
    }

    private static AppSettings LoadSettings()
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

    private static string LoadApiKey(OpenAiProvider provider)
    {
        var key = provider == OpenAiProvider.Azure
            ? DotEnvReader.Read("AZURE_OPENAI_API_KEY")
            : DotEnvReader.Read("OPENAI_API_KEY");
        return key ?? throw new InvalidOperationException(
            $"API key not found in .env. Set {(provider == OpenAiProvider.Azure ? "AZURE_OPENAI_API_KEY" : "OPENAI_API_KEY")}.");
    }

    private sealed class StaticApiKeyService(string apiKey) : IApiKeyService
    {
        public bool HasKey => true;
        public Task<string?> GetApiKeyAsync() => Task.FromResult<string?>(apiKey);
        public Task SetApiKeyAsync(string apiKey) => Task.CompletedTask;
        public Task ClearApiKeyAsync() => Task.CompletedTask;
    }

    /// <summary>
    /// Stub chat client — tools won't actually execute during real API tests.
    /// We intercept function calls at the Realtime API level and send mock results.
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
