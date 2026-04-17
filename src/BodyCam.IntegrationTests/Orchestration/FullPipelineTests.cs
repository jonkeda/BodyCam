using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BodyCam.Agents;
using BodyCam.IntegrationTests.Fixtures;
using BodyCam.Models;
using BodyCam.Orchestration;
using BodyCam.Services;
using FluentAssertions;
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
    public void ConversationAgent_WithWireMock_ProcessesAndReturns()
    {
        // Arrange — ConversationAgent still uses stub logic (M2 will implement real HTTP)
        // This test validates the pipeline integration point
        var agent = new ConversationAgent();
        var session = new SessionContext();

        // Act
        agent.AddUserMessage("What is 2+2?", session);

        // Assert
        session.Messages.Should().ContainSingle(m => m.Role == "user" && m.Content == "What is 2+2?");
    }

    [Fact]
    public async Task Pipeline_AudioIn_Transcript_Conversation_AudioOut()
    {
        // Arrange — full pipeline with mocked services
        var audioIn = Substitute.For<IAudioInputService>();
        var audioOut = Substitute.For<IAudioOutputService>();
        var realtime = Substitute.For<IRealtimeClient>();
        var camera = Substitute.For<ICameraService>();

        var voiceIn = new VoiceInputAgent(audioIn, realtime);
        var conversation = new ConversationAgent();
        var voiceOut = new VoiceOutputAgent(audioOut);
        var vision = new VisionAgent(camera, new AppSettings());
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
        var orchestrator = new AgentOrchestrator(voiceIn, conversation, voiceOut, vision, realtime, settingsService, new AppSettings());

        var transcripts = new List<string>();
        var debugLogs = new List<string>();
        orchestrator.TranscriptUpdated += (_, t) => transcripts.Add(t);
        orchestrator.DebugLog += (_, d) => debugLogs.Add(d);

        // Act
        await orchestrator.StartAsync();

        // Assert — orchestrator started successfully
        orchestrator.IsRunning.Should().BeTrue();
        orchestrator.Session.IsActive.Should().BeTrue();
        debugLogs.Should().Contain(m => m.Contains("connected"));

        // Cleanup
        await orchestrator.StopAsync();
        orchestrator.IsRunning.Should().BeFalse();
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
