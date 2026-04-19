using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Orchestration;
using BodyCam.Services;
using BodyCam.Services.Camera;
using BodyCam.Services.Logging;
using BodyCam.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BodyCam.Tests.Orchestration;

public class OrchestratorWakeWordTests
{
    private static AgentOrchestrator CreateOrchestrator(
        out IWakeWordService wakeWord,
        out IRealtimeClient realtime)
    {
        var audioIn = Substitute.For<IAudioInputService>();
        var audioOut = Substitute.For<IAudioOutputService>();
        realtime = Substitute.For<IRealtimeClient>();
        wakeWord = Substitute.For<IWakeWordService>();

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

        var descTool = new DescribeSceneTool(vision);
        var deepTool = new DeepAnalysisTool(conversation);
        var dispatcher = new ToolDispatcher(new ITool[] { descTool, deepTool });
        var micCoordinator = Substitute.For<IMicrophoneCoordinator>();
        var cameraManager = new CameraManager([], settingsService);
        var wakeWordInstance = wakeWord;
        var sink = new InAppLogSink();
        var loggerProvider = new InAppLoggerProvider(sink, LogLevel.Debug);
        var loggerFactory = new LoggerFactory([loggerProvider]);
        var logger = loggerFactory.CreateLogger<AgentOrchestrator>();

        return new AgentOrchestrator(voiceIn, conversation, voiceOut, vision, realtime, settingsService, new AppSettings(), dispatcher, wakeWordInstance, micCoordinator, cameraManager, logger);
    }

    [Fact]
    public async Task StartListeningAsync_SubscribesAndStarts()
    {
        var orchestrator = CreateOrchestrator(out var wakeWord, out _);

        await orchestrator.StartListeningAsync();

        await wakeWord.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopListeningAsync_UnsubscribesAndStops()
    {
        var orchestrator = CreateOrchestrator(out var wakeWord, out _);

        await orchestrator.StartListeningAsync();
        await orchestrator.StopListeningAsync();

        await wakeWord.Received(1).StopAsync();
    }

    [Fact]
    public async Task OnWakeWord_StartSession_StartsOrchestrator()
    {
        var orchestrator = CreateOrchestrator(out var wakeWord, out var realtime);

        await orchestrator.StartListeningAsync();

        wakeWord.WakeWordDetected += Raise.Event<EventHandler<WakeWordDetectedEventArgs>>(
            wakeWord,
            new WakeWordDetectedEventArgs
            {
                Action = WakeWordAction.StartSession,
                Keyword = "Hey BodyCam"
            });

        await Task.Delay(100); // async void handler

        await realtime.Received().ConnectAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnWakeWord_GoToSleep_StopsEverything()
    {
        var orchestrator = CreateOrchestrator(out var wakeWord, out var realtime);

        await orchestrator.StartListeningAsync();

        // First start the session
        await orchestrator.StartAsync();

        wakeWord.WakeWordDetected += Raise.Event<EventHandler<WakeWordDetectedEventArgs>>(
            wakeWord,
            new WakeWordDetectedEventArgs
            {
                Action = WakeWordAction.GoToSleep,
                Keyword = "Go to sleep"
            });

        await Task.Delay(100);

        await wakeWord.Received(1).StopAsync();
    }
}
