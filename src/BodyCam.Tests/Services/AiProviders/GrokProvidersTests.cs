using System.Net;
using System.Text;
using System.Text.Json;
using BodyCam.Services.AiProviders;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace BodyCam.Tests.Services.AiProviders;

public class GrokProvidersTests
{
    [Fact]
    public async Task TextProvider_PostsChatCompletionAndParsesToolCall()
    {
        var handler = new CaptureHandler((request, _) => JsonResponse("""
            {
              "id": "resp-1",
              "model": "grok-4.3",
              "usage": {"prompt_tokens": 10, "completion_tokens": 2, "total_tokens": 12},
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "Sure.",
                    "tool_calls": [
                      {
                        "id": "call-1",
                        "type": "function",
                        "function": {
                          "name": "look",
                          "arguments": "{\"detail\":\"summary\"}"
                        }
                      }
                    ]
                  }
                }
              ]
            }
            """));
        var provider = new GrokTextProvider(CreateClient(handler));

        var response = await provider.GetTextAsync(new AiTextRequest(
            "grok-4.3",
            [new("user", "What is there?")],
            [new("look", "Capture and describe the scene")]));

        handler.RequestUri.Should().Be("https://api.x.ai/v1/chat/completions");
        handler.Authorization.Should().Be("Bearer xai-test");
        handler.Body.Should().Contain("\"model\":\"grok-4.3\"");
        handler.Body.Should().Contain("\"tools\"");
        response.Text.Should().Be("Sure.");
        response.Usage.Should().Be(new AiTokenUsage(10, 2, 12));
        response.ToolCalls.Should().ContainSingle(call =>
            call.Id == "call-1"
            && call.Name == "look"
            && call.ArgumentsJson.Contains("summary"));
    }

    [Fact]
    public async Task GrokChatClient_TracksProviderTelemetryWithUsage()
    {
        var handler = new CaptureHandler((_, _) => JsonResponse("""
            {
              "id": "resp-usage",
              "model": "grok-4.3",
              "usage": {"prompt_tokens": 4, "completion_tokens": 3, "total_tokens": 7},
              "choices":[{"message":{"role":"assistant","content":"tracked answer"}}]
            }
            """));
        var analytics = new RecordingAnalyticsService();
        var chatClient = new GrokChatClient(
            CreateClient(handler),
            new AppSettings { ChatModel = "grok-4.3" },
            analytics);

        var response = await chatClient.GetResponseAsync([
            new ChatMessage(ChatRole.User, "Track this")
        ]);

        response.Text.Should().Be("tracked answer");
        analytics.HasEvent(
            "ai.provider.request",
            ("provider.id", AiProviderIds.XaiGrok),
            ("capability.path", "text"),
            ("usage.total_tokens", "7")).Should().BeTrue();
        analytics.Metrics.Should().Contain(item => item.Name == "ai.provider.latency_ms");
    }

    [Fact]
    public async Task VisionProvider_SendsImageAsDataUriImageUrl()
    {
        var handler = new CaptureHandler((_, _) => JsonResponse("""
            {"choices":[{"message":{"role":"assistant","content":"A desk."}}]}
            """));
        var provider = new GrokVisionProvider(CreateClient(handler));

        var response = await provider.DescribeImageAsync(new AiVisionRequest(
            "grok-4.3",
            "Describe it",
            [1, 2, 3],
            "image/jpeg"));

        using var doc = JsonDocument.Parse(handler.Body);
        var content = doc.RootElement
            .GetProperty("messages")[0]
            .GetProperty("content");

        content[0].GetProperty("type").GetString().Should().Be("image_url");
        content[0].GetProperty("image_url").GetProperty("url").GetString()
            .Should().StartWith("data:image/jpeg;base64,");
        content[1].GetProperty("text").GetString().Should().Be("Describe it");
        response.Text.Should().Be("A desk.");
    }

    [Fact]
    public async Task SpeechToTextProvider_PostsMultipartAudio()
    {
        var handler = new CaptureHandler((_, _) => JsonResponse("""
            {"text":"hello bodycam","language":"en","duration":1.25}
            """));
        var provider = new GrokSpeechToTextProvider(CreateClient(handler));

        var response = await provider.TranscribeAsync(new AiSpeechToTextRequest(
            [4, 5, 6],
            "clip.mp3",
            "audio/mpeg",
            KeyTerms: ["BodyCam"]));

        handler.RequestUri.Should().Be("https://api.x.ai/v1/stt");
        handler.ContentType.Should().StartWith("multipart/form-data");
        handler.Body.Should().Contain("name=keyterm");
        handler.Body.Should().Contain("BodyCam");
        response.Text.Should().Be("hello bodycam");
        response.DurationSeconds.Should().Be(1.25);
    }

    [Fact]
    public async Task TextToSpeechProvider_ReturnsAudioBytes()
    {
        var handler = new CaptureHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([9, 8, 7])
            {
                Headers = { ContentType = new("audio/mpeg") }
            }
        });
        var provider = new GrokTextToSpeechProvider(CreateClient(handler));

        var response = await provider.SynthesizeAsync(new AiTextToSpeechRequest("Hello", VoiceId: "eve"));

        handler.RequestUri.Should().Be("https://api.x.ai/v1/tts");
        handler.Body.Should().Contain("\"voice_id\":\"eve\"");
        response.AudioBytes.Should().Equal(9, 8, 7);
        response.ContentType.Should().Be("audio/mpeg");
    }

    [Fact]
    public async Task ImageGenerationProvider_UsesEditsWhenSourceImageIsProvided()
    {
        var handler = new CaptureHandler((_, _) => JsonResponse("""
            {
              "data": [
                {
                  "url": "https://imgen.x.ai/test.jpeg",
                  "mime_type": "image/jpeg",
                  "revised_prompt": ""
                }
              ]
            }
            """));
        var provider = new GrokImageGenerationProvider(CreateClient(handler));

        var response = await provider.GenerateAsync(new AiImageGenerationRequest(
            "Add labels",
            SourceImageBytes: [1, 2, 3]));

        handler.RequestUri.Should().Be("https://api.x.ai/v1/images/edits");
        handler.Body.Should().Contain("\"image\"");
        response.Images.Should().ContainSingle(image =>
            image.Url == "https://imgen.x.ai/test.jpeg"
            && image.MimeType == "image/jpeg");
    }

    [Fact]
    public async Task GrokChatClient_UsesVisionPayloadForImageContent()
    {
        var handler = new CaptureHandler((_, _) => JsonResponse("""
            {"choices":[{"message":{"role":"assistant","content":"image answer"}}]}
            """));
        var chatClient = new GrokChatClient(
            CreateClient(handler),
            new AppSettings
            {
                ChatModel = "gpt-5.4-mini",
                VisionModel = "gpt-5.4"
            });

        var response = await chatClient.GetResponseAsync([
            new ChatMessage(ChatRole.User, [
                new DataContent(new byte[] { 1, 2, 3 }, "image/jpeg"),
                new TextContent("What do you see?")
            ])
        ]);

        handler.Body.Should().Contain("\"model\":\"grok-4.3\"");
        handler.Body.Should().Contain("\"image_url\"");
        response.Text.Should().Be("image answer");
    }

    private static GrokApiClient CreateClient(CaptureHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = GrokApiClient.DefaultBaseUri }, "xai-test");

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class CaptureHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public string RequestUri { get; private set; } = string.Empty;
        public string Authorization { get; private set; } = string.Empty;
        public string ContentType { get; private set; } = string.Empty;
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString() ?? string.Empty;
            Authorization = request.Headers.Authorization?.ToString() ?? string.Empty;
            ContentType = request.Content?.Headers.ContentType?.ToString() ?? string.Empty;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request, Body);
        }
    }
}
