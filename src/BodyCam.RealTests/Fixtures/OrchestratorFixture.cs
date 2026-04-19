using Azure.AI.OpenAI;
using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Orchestration;
using BodyCam.Services;
using BodyCam.Services.Audio.WebRtcApm;
using BodyCam.Services.Camera;
using BodyCam.Tools;
using Microsoft.Extensions.AI;
using OpenAI.Realtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using System.ClientModel;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Fixtures;

/// <summary>
/// Full-stack orchestrator fixture for real integration tests.
/// Wires up a real <see cref="AgentOrchestrator"/> with live API connections,
/// test audio I/O, and test camera.
/// </summary>
public class OrchestratorFixture : IAsyncLifetime
{
    private readonly string _tempMemoryPath;
    private ITestOutputHelper? _output;
    private readonly object _outputLock = new();

    // ── Public test surfaces ──
    public AgentOrchestrator Orchestrator { get; private set; } = null!;
    public AppSettings Settings { get; private set; } = null!;
    public PcmAudioSource AudioInput { get; } = new();
    public AudioCaptureSink AudioOutput { get; } = new();
    public TestFrameProvider FrameProvider { get; } = new();

    // ── Captured events ──
    private readonly List<string> _transcriptDeltas = [];
    private readonly List<string> _transcriptCompletions = [];
    private readonly List<string> _debugLogs = [];
    private readonly object _eventsLock = new();

    private TaskCompletionSource _transcriptCompletionTcs = new();

    public IReadOnlyList<string> TranscriptDeltas
    {
        get { lock (_eventsLock) return _transcriptDeltas.ToList(); }
    }

    public IReadOnlyList<string> TranscriptCompletions
    {
        get { lock (_eventsLock) return _transcriptCompletions.ToList(); }
    }

    public IReadOnlyList<string> DebugLogs
    {
        get { lock (_eventsLock) return _debugLogs.ToList(); }
    }

    public OrchestratorFixture()
    {
        _tempMemoryPath = Path.Combine(Path.GetTempPath(), $"bodycam_orch_test_{Guid.NewGuid():N}.json");
    }

    /// <summary>
    /// Set the test output helper for logging. Must be called before <see cref="InitializeAsync"/>
    /// when used with <c>IClassFixture</c>, or per-test for shared fixtures.
    /// </summary>
    public void SetOutput(ITestOutputHelper output)
    {
        lock (_outputLock)
            _output = output;
    }

    public async Task InitializeAsync()
    {
        Settings = RealtimeFixture.LoadSettings();
        var apiKey = RealtimeFixture.LoadApiKey(Settings.Provider);

        var realtimeClient = RealtimeFixture.BuildClient(apiKey, Settings);

        // Real chat client (for vision + conversation agents)
        var chatClient = BuildChatClient(apiKey, Settings);

        // Agents
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance) { IsEnabled = false };
        var vision = new VisionAgent(chatClient, Settings);
        var conversation = new ConversationAgent(chatClient, Settings);
        var voiceIn = new VoiceInputAgent(AudioInput, NullLogger<VoiceInputAgent>.Instance, aec);
        var voiceOut = new VoiceOutputAgent(AudioOutput, aec);

        // Tools — all 13
        var memoryStore = new MemoryStore(_tempMemoryPath);
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
        var dispatcher = new ToolDispatcher(tools);

        // Supporting services
        var settingsService = new InMemorySettingsService(Settings);
        var wakeWord = new NullWakeWordService();
        var micCoordinator = new NoOpMicrophoneCoordinator();
        var cameraManager = new CameraManager([FrameProvider], settingsService);

        Orchestrator = new AgentOrchestrator(
            voiceIn, conversation, voiceOut, vision,
            realtimeClient, settingsService, Settings, dispatcher,
            wakeWord, micCoordinator, cameraManager, aec,
            NullLogger<AgentOrchestrator>.Instance);

        // Wire event capture
        Orchestrator.TranscriptDelta += (_, delta) =>
        {
            lock (_eventsLock)
                _transcriptDeltas.Add(delta);
            Log($"[TranscriptDelta] {delta}");
        };

        Orchestrator.TranscriptCompleted += (_, text) =>
        {
            lock (_eventsLock)
                _transcriptCompletions.Add(text);
            _transcriptCompletionTcs.TrySetResult();
            Log($"[TranscriptCompleted] {text}");
        };

        Orchestrator.DebugLog += (_, msg) =>
        {
            lock (_eventsLock)
                _debugLogs.Add(msg);
            Log($"[Debug] {msg}");
        };

        await Orchestrator.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (Orchestrator is not null)
            await Orchestrator.StopAsync();

        try
        {
            if (File.Exists(_tempMemoryPath))
                File.Delete(_tempMemoryPath);
        }
        catch { /* best-effort cleanup */ }
    }

    /// <summary>Clears all captured events and resets completion signals.</summary>
    public void Reset()
    {
        lock (_eventsLock)
        {
            _transcriptDeltas.Clear();
            _transcriptCompletions.Clear();
            _debugLogs.Clear();
        }
        _transcriptCompletionTcs = new TaskCompletionSource();
        AudioOutput.Clear();
    }

    /// <summary>Waits until at least one <see cref="TranscriptCompletions"/> event arrives.</summary>
    public async Task WaitForTranscriptCompletion(TimeSpan? timeout = null)
    {
        var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        using var reg = cts.Token.Register(() => _transcriptCompletionTcs.TrySetCanceled());
        await _transcriptCompletionTcs.Task;
    }

    /// <summary>Polls <paramref name="condition"/> until it returns true or timeout elapses.</summary>
    public async Task WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Condition was not met within the specified timeout.");
            await Task.Delay(100);
        }
    }

    // ── Private helpers ──

    private static IChatClient BuildChatClient(string apiKey, AppSettings settings)
    {
        if (settings.Provider == OpenAiProvider.Azure)
        {
            var credential = new ApiKeyCredential(apiKey);
            var azureClient = new AzureOpenAIClient(new Uri(settings.AzureEndpoint!), credential);
            var deployment = settings.AzureVisionDeploymentName ?? settings.AzureChatDeploymentName!;
            return azureClient.GetChatClient(deployment).AsIChatClient();
        }

        return new OpenAIClient(apiKey).GetChatClient(settings.VisionModel).AsIChatClient();
    }

    private void Log(string message)
    {
        lock (_outputLock)
        {
            try { _output?.WriteLine(message); }
            catch (InvalidOperationException) { /* test already finished */ }
        }
    }

    // ── Inline no-op implementations ──

    private sealed class NoOpMicrophoneCoordinator : IMicrophoneCoordinator
    {
        public Task TransitionToActiveSessionAsync() => Task.CompletedTask;
        public Task TransitionToWakeWordAsync() => Task.CompletedTask;
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        private readonly AppSettings _settings;

        public InMemorySettingsService(AppSettings settings) => _settings = settings;

        public string RealtimeModel
        {
            get => _settings.RealtimeModel;
            set => _settings.RealtimeModel = value;
        }

        public string ChatModel
        {
            get => _settings.ChatModel;
            set => _settings.ChatModel = value;
        }

        public string VisionModel
        {
            get => _settings.VisionModel;
            set => _settings.VisionModel = value;
        }

        public string TranscriptionModel
        {
            get => _settings.TranscriptionModel;
            set => _settings.TranscriptionModel = value;
        }

        public string Voice
        {
            get => _settings.Voice;
            set => _settings.Voice = value;
        }

        public string TurnDetection
        {
            get => _settings.TurnDetection;
            set => _settings.TurnDetection = value;
        }

        public string NoiseReduction
        {
            get => _settings.NoiseReduction;
            set => _settings.NoiseReduction = value;
        }

        public OpenAiProvider Provider
        {
            get => _settings.Provider;
            set => _settings.Provider = value;
        }

        public string? AzureEndpoint
        {
            get => _settings.AzureEndpoint;
            set => _settings.AzureEndpoint = value;
        }

        public string? AzureRealtimeDeploymentName
        {
            get => _settings.AzureRealtimeDeploymentName;
            set => _settings.AzureRealtimeDeploymentName = value;
        }

        public string? AzureChatDeploymentName
        {
            get => _settings.AzureChatDeploymentName;
            set => _settings.AzureChatDeploymentName = value;
        }

        public string? AzureVisionDeploymentName
        {
            get => _settings.AzureVisionDeploymentName;
            set => _settings.AzureVisionDeploymentName = value;
        }

        public string AzureApiVersion
        {
            get => _settings.AzureApiVersion;
            set => _settings.AzureApiVersion = value;
        }

        public bool DebugMode { get; set; }
        public bool ShowTokenCounts { get; set; }
        public bool ShowCostEstimate { get; set; }

        public string SystemInstructions
        {
            get => _settings.SystemInstructions;
            set => _settings.SystemInstructions = value;
        }

        public string? ActiveCameraProvider { get; set; }
        public string? ActiveAudioInputProvider { get; set; }
        public string? ActiveAudioOutputProvider { get; set; }
        public string? PicovoiceAccessKey { get; set; }
        public bool SendDiagnosticData { get; set; }
        public string? AzureMonitorConnectionString { get; set; }
        public bool SendCrashReports { get; set; }
        public string? SentryDsn { get; set; }
        public bool SendUsageData { get; set; }
        public bool SetupCompleted { get; set; }
    }
}
