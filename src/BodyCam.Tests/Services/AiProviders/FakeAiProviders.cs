using System.Text;
using System.Text.Json.Nodes;
using BodyCam;
using BodyCam.Services;
using BodyCam.Services.AiProviders;

namespace BodyCam.Tests.Services.AiProviders;

internal sealed class FakeAiTextProvider(string text = "fake text") : IAiTextProvider
{
    public AiTextRequest? LastRequest { get; private set; }

    public Task<AiTextResponse> GetTextAsync(AiTextRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(new AiTextResponse(text, []));
    }
}

internal sealed class FakeAiVisionProvider(string text = "fake vision") : IAiVisionProvider
{
    public AiVisionRequest? LastRequest { get; private set; }

    public Task<AiTextResponse> DescribeImageAsync(AiVisionRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(new AiTextResponse(text, []));
    }
}

internal sealed class FakeAiSpeechToTextProvider(string text = "fake transcript") : IAiSpeechToTextProvider
{
    public AiSpeechToTextRequest? LastRequest { get; private set; }

    public Task<AiSpeechToTextResponse> TranscribeAsync(AiSpeechToTextRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(new AiSpeechToTextResponse(text, "en", 0.5));
    }
}

internal sealed class FakeAiTextToSpeechProvider : IAiTextToSpeechProvider
{
    public AiTextToSpeechRequest? LastRequest { get; private set; }

    public Task<AiTextToSpeechResponse> SynthesizeAsync(AiTextToSpeechRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(new AiTextToSpeechResponse(Encoding.UTF8.GetBytes(request.Text), "audio/mpeg"));
    }
}

internal sealed class FakeAiImageGenerationProvider : IAiImageGenerationProvider
{
    public AiImageGenerationRequest? LastRequest { get; private set; }

    public Task<AiImageGenerationResponse> GenerateAsync(AiImageGenerationRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(new AiImageGenerationResponse([
            new AiGeneratedImage("https://example.test/fake.png", null, "image/png", request.Prompt)
        ]));
    }
}

internal sealed class FakeGrokRealtimeVoiceProvider : IGrokRealtimeVoiceProvider
{
    public GrokRealtimeVoiceSessionOptions CreateSessionOptions(AppSettings settings) =>
        new(
            new Uri("wss://example.test/realtime?model=fake-voice"),
            "fake-voice",
            RequiresEphemeralClientSecret: true,
            new Uri("https://example.test/realtime/client_secrets"));
}

internal sealed class FakeProviderAdapter(
    string providerId,
    AiProviderCapability capabilities,
    bool selectable = true) : IAiProviderAdapter
{
    public AiProviderDefinition Definition { get; } = new(
        providerId,
        "Fake Provider",
        "Fake",
        "Fake provider for provider-architecture tests.",
        selectable,
        [AiCredentialMode.ApiKey],
        capabilities,
        new Dictionary<AiModelKind, ModelInfo[]>
        {
            [AiModelKind.Chat] = [new("fake-chat", "Fake chat")],
            [AiModelKind.Vision] = [new("fake-vision", "Fake vision")],
        },
        AiProviderCredentialPolicy.ApiKeyOnly,
        [new("Open fake docs", new Uri("https://example.test/docs"), AiProviderSetupLinkKind.Documentation)]);

    public Uri GetRealtimeUri(AppSettings settings) => new("wss://example.test/realtime");

    public Uri GetChatUri(AppSettings settings) => new("https://example.test/chat");

    public Uri GetVisionUri(AppSettings settings) => new("https://example.test/vision");
}

internal sealed class RecordingAnalyticsService : IAnalyticsService
{
    public bool IsEnabled { get; set; } = true;
    public List<(string Name, IDictionary<string, string>? Properties)> Events { get; } = [];
    public List<(string Name, double Value, IDictionary<string, string>? Tags)> Metrics { get; } = [];

    public void TrackEvent(string name, IDictionary<string, string>? properties = null) =>
        Events.Add((name, properties is null ? null : new Dictionary<string, string>(properties)));

    public void TrackMetric(string name, double value, IDictionary<string, string>? tags = null) =>
        Metrics.Add((name, value, tags is null ? null : new Dictionary<string, string>(tags)));

    public bool HasEvent(string name, params (string Key, string Value)[] properties) =>
        Events.Any(item =>
            item.Name == name
            && item.Properties is not null
            && properties.All(property =>
                item.Properties.TryGetValue(property.Key, out var value)
                && value == property.Value));
}
