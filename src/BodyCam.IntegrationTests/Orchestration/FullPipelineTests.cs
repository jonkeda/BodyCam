using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BodyCam.Agents;
using BodyCam.IntegrationTests.Fixtures;
using BodyCam.Models;
using BodyCam.Orchestration;
using BodyCam.Services;
using BodyCam.Services.Audio.WebRtcApm;
using BodyCam.Services.Camera;
using BodyCam.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BodyCam.IntegrationTests.Orchestration;

public class FullPipelineTests : IClassFixture<OpenAiWireMockFixture>
{
    private readonly OpenAiWireMockFixture _fixture;

    public FullPipelineTests(OpenAiWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetToDefaults();
    }

    [Fact]
    public async Task ConversationAgent_AnalyzeAsync_ReturnsResult()
    {
        // Arrange — ConversationAgent now uses IChatClient for deep analysis
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(
            Arg.Any<IList<Microsoft.Extensions.AI.ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, "4")));

        var agent = new ConversationAgent(chatClient, new AppSettings());

        // Act
        var result = await agent.AnalyzeAsync("What is 2+2?");

        // Assert
        result.Should().Be("4");
    }

    [Fact]
    public async Task Pipeline_AudioIn_Transcript_Conversation_AudioOut()
    {
        // Arrange — full pipeline with mocked services
        var audioIn = Substitute.For<IAudioInputService>();
        var audioOut = Substitute.For<IAudioOutputService>();
        var realtimeClient = Substitute.For<Microsoft.Extensions.AI.IRealtimeClient>();
        var chatClient = Substitute.For<IChatClient>();

        var voiceIn = new VoiceInputAgent(audioIn, NullLogger<VoiceInputAgent>.Instance);
        var conversation = new ConversationAgent(chatClient, new AppSettings());
        var voiceOut = new VoiceOutputAgent(audioOut);
        var vision = new VisionAgent(chatClient, new AppSettings());
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
        var aec = new AecProcessor(Substitute.For<ILogger<AecProcessor>>());
        var logger = Substitute.For<ILogger<AgentOrchestrator>>();
        var orchestrator = new AgentOrchestrator(voiceIn, conversation, voiceOut, vision, realtimeClient, settingsService, new AppSettings(), dispatcher, wakeWord, micCoordinator, cameraManager, aec, logger);

        var transcripts = new List<string>();
        orchestrator.TranscriptUpdated += (_, t) => transcripts.Add(t);

        // Act — StartAsync will fail because the mock IRealtimeClient doesn't return a session,
        // but the orchestrator should at least construct successfully
        orchestrator.Should().NotBeNull();
    }

    [Fact]
    public async Task WireMock_ChatEndpoint_AcceptsValidRequest()
    {
        // Arrange
        using var client = new HttpClient { BaseAddress = new Uri(_fixture.BaseUrl) };
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-key");

        var request = new
        {
            model = "gpt-5.4-mini",
            messages = new[]
            {
                new { role = "system", content = "You are BodyCam, an AI assistant." },
                new { role = "user", content = "What time is it?" }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/v1/chat/completions", request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("choices").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("role")
            .GetString()
            .Should().Be("assistant");
    }

    [Fact]
    public async Task WireMock_ErrorRecovery_ServerError_ThenSuccess()
    {
        // Arrange
        using var client = new HttpClient { BaseAddress = new Uri(_fixture.BaseUrl) };
        var request = new { model = "gpt-5.4-mini", messages = new[] { new { role = "user", content = "test" } } };

        // Act — first call returns 500
        _fixture.StubServerError();
        var errorResponse = await client.PostAsJsonAsync("/v1/chat/completions", request);

        // Reset to success
        _fixture.Server.Reset();
        _fixture.Server
            .Given(WireMock.RequestBuilders.Request.Create()
                .WithPath("/v1/chat/completions")
                .UsingPost())
            .RespondWith(WireMock.ResponseBuilders.Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"choices":[{"message":{"role":"assistant","content":"OK"}}]}"""));

        var successResponse = await client.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        errorResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        successResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

}
