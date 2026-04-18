using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Orchestration;
using BodyCam.Services;
using BodyCam.Services.Camera;
using BodyCam.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace BodyCam.Tests.Orchestration;

public class AgentOrchestratorTests
{
    private static AgentOrchestrator CreateOrchestrator(
        out IAudioInputService audioIn,
        out IAudioOutputService audioOut,
        out IRealtimeClient realtime)
    {
        audioIn = Substitute.For<IAudioInputService>();
        audioOut = Substitute.For<IAudioOutputService>();
        realtime = Substitute.For<IRealtimeClient>();

        var voiceIn = new VoiceInputAgent(audioIn, realtime);
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

        return new AgentOrchestrator(voiceIn, conversation, voiceOut, vision, realtime, settingsService, new AppSettings(), dispatcher, new Lazy<IWakeWordService>(() => wakeWord), micCoordinator, cameraManager);
    }

    [Fact]
    public void IsRunning_InitiallyFalse()
    {
        var orchestrator = CreateOrchestrator(out _, out _, out _);
        orchestrator.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_ConnectsRealtime()
    {
        var orchestrator = CreateOrchestrator(out _, out _, out var realtime);

        await orchestrator.StartAsync();

        await realtime.Received(1).ConnectAsync(Arg.Any<CancellationToken>());
        orchestrator.IsRunning.Should().BeTrue();

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task StartAsync_SetsSessionActive()
    {
        var orchestrator = CreateOrchestrator(out _, out _, out _);

        await orchestrator.StartAsync();
        orchestrator.Session.IsActive.Should().BeTrue();

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task StartAsync_StartsAudioPipeline()
    {
        var orchestrator = CreateOrchestrator(out var audioIn, out var audioOut, out _);

        await orchestrator.StartAsync();

        await audioIn.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await audioOut.Received(1).StartAsync(Arg.Any<CancellationToken>());

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task StartAsync_EmitsDebugLogs()
    {
        var orchestrator = CreateOrchestrator(out _, out _, out _);
        var logs = new List<string>();
        orchestrator.DebugLog += (_, msg) => logs.Add(msg);

        await orchestrator.StartAsync();

        logs.Should().Contain(m => m.Contains("connected"));
        logs.Should().Contain(m => m.Contains("pipeline started"));

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task StartAsync_DoubleStart_IsNoOp()
    {
        var orchestrator = CreateOrchestrator(out _, out _, out var realtime);

        await orchestrator.StartAsync();
        await orchestrator.StartAsync();

        await realtime.Received(1).ConnectAsync(Arg.Any<CancellationToken>());

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task StopAsync_WhenNotRunning_IsNoOp()
    {
        var orchestrator = CreateOrchestrator(out _, out _, out var realtime);

        await orchestrator.StopAsync();

        await realtime.DidNotReceive().DisconnectAsync();
    }

    [Fact]
    public async Task StopAsync_DisconnectsRealtime()
    {
        var orchestrator = CreateOrchestrator(out _, out _, out var realtime);

        await orchestrator.StartAsync();
        await orchestrator.StopAsync();

        await realtime.Received(1).DisconnectAsync();
        orchestrator.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StopAsync_SetsSessionInactive()
    {
        var orchestrator = CreateOrchestrator(out _, out _, out _);

        await orchestrator.StartAsync();
        await orchestrator.StopAsync();

        orchestrator.Session.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task StopAsync_EmitsDebugLog()
    {
        var orchestrator = CreateOrchestrator(out _, out _, out _);
        var logs = new List<string>();
        orchestrator.DebugLog += (_, msg) => logs.Add(msg);

        await orchestrator.StartAsync();
        logs.Clear();
        await orchestrator.StopAsync();

        logs.Should().Contain(m => m.Contains("stopped"));
    }

    [Fact]
    public async Task AudioDelta_RoutesToVoiceOutput()
    {
        var orchestrator = CreateOrchestrator(out _, out var audioOut, out var realtime);

        await orchestrator.StartAsync();

        var pcm = new byte[] { 1, 2, 3, 4 };
        realtime.AudioDelta += Raise.Event<EventHandler<byte[]>>(realtime, pcm);

        await Task.Delay(50); // async void handler

        await audioOut.Received(1).PlayChunkAsync(pcm, Arg.Any<CancellationToken>());

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task InputTranscriptCompleted_UpdatesSessionAndUI()
    {
        var orchestrator = CreateOrchestrator(out _, out _, out var realtime);
        var transcripts = new List<string>();
        var completed = new List<string>();
        orchestrator.TranscriptUpdated += (_, t) => transcripts.Add(t);
        orchestrator.TranscriptCompleted += (_, c) => completed.Add(c);

        await orchestrator.StartAsync();

        realtime.InputTranscriptCompleted += Raise.Event<EventHandler<string>>(realtime, "Hello world");

        transcripts.Should().Contain(t => t.Contains("You:") && t.Contains("Hello world"));
        completed.Should().ContainSingle().Which.Should().Be("You:Hello world");

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task OutputTranscriptCompleted_AddsAssistantMessage()
    {
        var orchestrator = CreateOrchestrator(out _, out _, out var realtime);
        var logs = new List<string>();
        var completed = new List<string>();
        orchestrator.DebugLog += (_, msg) => logs.Add(msg);
        orchestrator.TranscriptCompleted += (_, c) => completed.Add(c);

        await orchestrator.StartAsync();

        realtime.OutputTranscriptCompleted += Raise.Event<EventHandler<string>>(realtime, "I'm fine, thanks");

        logs.Should().Contain(m => m.Contains("AI said"));
        completed.Should().ContainSingle().Which.Should().Be("AI:I'm fine, thanks");

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task OutputTranscriptDelta_EmitsTranscriptUpdated()
    {
        var orchestrator = CreateOrchestrator(out _, out _, out var realtime);
        var transcripts = new List<string>();
        var deltas = new List<string>();
        orchestrator.TranscriptUpdated += (_, t) => transcripts.Add(t);
        orchestrator.TranscriptDelta += (_, d) => deltas.Add(d);

        await orchestrator.StartAsync();

        realtime.OutputTranscriptDelta += Raise.Event<EventHandler<string>>(realtime, "Hel");

        transcripts.Should().Contain(t => t.Contains("AI:") && t.Contains("Hel"));
        deltas.Should().ContainSingle().Which.Should().Be("Hel");

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task ResponseDone_ResetsTracker()
    {
        var orchestrator = CreateOrchestrator(out _, out _, out var realtime);
        var logs = new List<string>();
        orchestrator.DebugLog += (_, msg) => logs.Add(msg);

        await orchestrator.StartAsync();

        realtime.ResponseDone += Raise.Event<EventHandler<RealtimeResponseInfo>>(
            realtime,
            new RealtimeResponseInfo { ResponseId = "resp-001" });

        logs.Should().Contain(m => m.Contains("Response complete"));

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task ErrorOccurred_EmitsDebugLog()
    {
        var orchestrator = CreateOrchestrator(out _, out _, out var realtime);
        var logs = new List<string>();
        orchestrator.DebugLog += (_, msg) => logs.Add(msg);

        await orchestrator.StartAsync();

        realtime.ErrorOccurred += Raise.Event<EventHandler<string>>(realtime, "Connection lost");

        logs.Should().Contain(m => m.Contains("Realtime error") && m.Contains("Connection lost"));

        await orchestrator.StopAsync();
    }

    [Fact]
    public async Task StopAsync_UnsubscribesAllEvents_NoMoreCallbacks()
    {
        var orchestrator = CreateOrchestrator(out _, out _, out var realtime);
        var transcripts = new List<string>();
        orchestrator.TranscriptUpdated += (_, t) => transcripts.Add(t);

        await orchestrator.StartAsync();
        await orchestrator.StopAsync();

        transcripts.Clear();

        // Raise event after stop — should NOT trigger handler
        realtime.InputTranscriptCompleted += Raise.Event<EventHandler<string>>(realtime, "Should not appear");

        transcripts.Should().BeEmpty();
    }
}
