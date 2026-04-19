using BodyCam.Tests.TestInfrastructure;
using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Orchestration;
using BodyCam.Services;
using BodyCam.Services.Audio.WebRtcApm;
using BodyCam.Services.Camera;
using BodyCam.Services.Logging;
using BodyCam.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenAI.Realtime;

namespace BodyCam.Tests.Orchestration;

public class AgentOrchestratorTests
{
    private static AgentOrchestrator CreateOrchestrator(
        out IAudioInputService audioIn,
        out IAudioOutputService audioOut,
        InAppLogSink? logSink = null)
    {
        audioIn = Substitute.For<IAudioInputService>();
        audioOut = Substitute.For<IAudioOutputService>();

        var voiceIn = new VoiceInputAgent(audioIn, NullLogger<VoiceInputAgent>.Instance);
        var chatClient = Substitute.For<IChatClient>();
        var conversation = new ConversationAgent(chatClient, new AppSettings());
        var voiceOut = new VoiceOutputAgent(audioOut);
        var visionChatClient = Substitute.For<IChatClient>();
        var vision = new VisionAgent(visionChatClient, new AppSettings());
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.RealtimeModel.Returns(ModelOptions.DefaultRealtime);
        settingsService.ChatModel.Returns(ModelOptions.DefaultChat);
        settingsService.VisionModel.Returns(ModelOptions.DefaultVision);
        settingsService.TranscriptionModel.Returns(ModelOptions.DefaultTranscription);
        settingsService.Voice.Returns(ModelOptions.DefaultVoice);
        settingsService.TurnDetection.Returns(ModelOptions.DefaultTurnDetection);
        settingsService.NoiseReduction.Returns(ModelOptions.DefaultNoiseReduction);
        settingsService.SystemInstructions.Returns("You are a helpful assistant.");
        settingsService.Provider.Returns(OpenAiProvider.OpenAi);
        settingsService.AzureApiVersion.Returns("2025-04-01-preview");

        var describeSceneTool = new DescribeSceneTool(vision);
        var deepAnalysisTool = new DeepAnalysisTool(conversation);
        var dispatcher = new ToolDispatcher(new ITool[] { describeSceneTool, deepAnalysisTool });
        var wakeWord = Substitute.For<IWakeWordService>();
        var micCoordinator = Substitute.For<IMicrophoneCoordinator>();
        var cameraManager = new CameraManager([], settingsService);
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        var sink = logSink ?? new InAppLogSink();
        var loggerProvider = new InAppLoggerProvider(sink, LogLevel.Debug);
        var loggerFactory = new LoggerFactory([loggerProvider]);
        var logger = loggerFactory.CreateLogger<AgentOrchestrator>();
        var realtimeClient = new StubRealtimeClient();

        return new AgentOrchestrator(voiceIn, conversation, voiceOut, vision, realtimeClient, settingsService, new AppSettings(), dispatcher, wakeWord, micCoordinator, cameraManager, aec, logger);
    }

    [Fact]
    public void IsRunning_InitiallyFalse()
    {
        var orchestrator = CreateOrchestrator(out _, out _);
        orchestrator.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_ConnectsRealtime()
    {
        var orchestrator = CreateOrchestrator(out _, out _);

        await orchestrator.StartAsync();

        // TODO: Verify MAF session creation when realtime tests are migrated
        orchestrator.IsRunning.Should().BeTrue();

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task StartAsync_SetsSessionActive()
    {
        var orchestrator = CreateOrchestrator(out _, out _);

        await orchestrator.StartAsync();
        orchestrator.Session.IsActive.Should().BeTrue();

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task StartAsync_StartsAudioPipeline()
    {
        var orchestrator = CreateOrchestrator(out var audioIn, out var audioOut);

        await orchestrator.StartAsync();

        await audioIn.Received().StartAsync(Arg.Any<CancellationToken>());
        await audioOut.Received().StartAsync(Arg.Any<CancellationToken>());

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task StartAsync_EmitsDebugLogs()
    {
        var sink = new InAppLogSink();
        var orchestrator = CreateOrchestrator(out _, out _, sink);

        await orchestrator.StartAsync();

        var entries = sink.GetEntries();
        entries.Should().Contain(e => e.Message.Contains("connected"));
        entries.Should().Contain(e => e.Message.Contains("pipeline started"));

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task StartAsync_DoubleStart_IsNoOp()
    {
        var orchestrator = CreateOrchestrator(out _, out _);

        await orchestrator.StartAsync();
        await orchestrator.StartAsync();

        orchestrator.IsRunning.Should().BeTrue();

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task StopAsync_WhenNotRunning_IsNoOp()
    {
        var orchestrator = CreateOrchestrator(out _, out _);

        await orchestrator.StopAsync();

        orchestrator.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StopAsync_DisconnectsRealtime()
    {
        var orchestrator = CreateOrchestrator(out _, out _);

        await orchestrator.StartAsync();
        await orchestrator.StopAsync();

        // TODO: Verify MAF session disposal when realtime tests are migrated
        orchestrator.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StopAsync_SetsSessionInactive()
    {
        var orchestrator = CreateOrchestrator(out _, out _);

        await orchestrator.StartAsync();
        await orchestrator.StopAsync();

        orchestrator.Session.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task StopAsync_EmitsDebugLog()
    {
        var sink = new InAppLogSink();
        var orchestrator = CreateOrchestrator(out _, out _, sink);

        await orchestrator.StartAsync();
        sink.Clear();
        await orchestrator.StopAsync();

        var entries = sink.GetEntries();
        entries.Should().Contain(e => e.Message.Contains("stopped"));
    }

    // TODO: Tests below require rewriting for MAF realtime session model.
    // Old IRealtimeClient events (AudioDelta, InputTranscriptCompleted, etc.) no longer exist.
    // These tests should use IRealtimeClientSession streaming with RealtimeServerMessage.
}
