using System.Net;
using System.Text;
using BodyCam.Services;
using BodyCam.Services.AiProviders;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Services.AiProviders;

public class AiProviderDiagnosticsServiceTests
{
    [Fact]
    public async Task TestAsync_GrokWithKeyOutsideLiveMode_SkipsLiveProbes()
    {
        var analytics = new RecordingAnalyticsService();
        var service = CreateService(
            apiKey: "xai-test",
            analytics: analytics,
            isAndroid: () => false,
            getEnvironmentVariable: _ => null);

        var result = await service.TestAsync(AiProviderIds.XaiGrok);

        result.Success.Should().BeTrue();
        result.Summary.Should().Contain("API key configured");
        result.Capabilities.Should().OnlyContain(item => item.Status == AiCapabilityDiagnosticStatus.Skipped);
        analytics.HasEvent(
            "ai.provider.diagnostic",
            ("provider.id", AiProviderIds.XaiGrok),
            ("result", "skipped")).Should().BeTrue();
    }

    [Fact]
    public async Task TestAsync_GrokLiveMode_ProbesTextAndVision()
    {
        var handler = new CaptureHandler(_ => JsonResponse("""
            {
              "id": "resp-test",
              "model": "grok-4.3",
              "usage": {"prompt_tokens": 3, "completion_tokens": 1, "total_tokens": 4},
              "choices": [{"message": {"role": "assistant", "content": "OK"}}]
            }
            """));
        var analytics = new RecordingAnalyticsService();
        var service = CreateService(
            apiKey: "xai-test",
            analytics: analytics,
            httpClientFactory: () => new HttpClient(handler) { BaseAddress = GrokApiClient.DefaultBaseUri },
            isAndroid: () => false,
            getEnvironmentVariable: name => name == "BODYCAM_GROK_LIVE_TESTS" ? "1" : null);

        var result = await service.TestAsync(AiProviderIds.XaiGrok);

        result.Success.Should().BeTrue();
        result.Summary.Should().Contain("text and vision live probes passed");
        result.Capabilities.Should().Contain(item =>
            item.Capability == "Text" && item.Status == AiCapabilityDiagnosticStatus.Passed);
        result.Capabilities.Should().Contain(item =>
            item.Capability == "Vision" && item.Status == AiCapabilityDiagnosticStatus.Passed);
        handler.RequestUris.Should().HaveCount(2);
        handler.RequestUris.Should().OnlyContain(uri => uri == "https://api.x.ai/v1/chat/completions");
        analytics.HasEvent("ai.provider.diagnostic", ("capability.path", "text")).Should().BeTrue();
        analytics.HasEvent("ai.provider.diagnostic", ("capability.path", "vision")).Should().BeTrue();
    }

    [Fact]
    public async Task TestAsync_MissingKey_FailsBeforeProviderProbe()
    {
        var service = CreateService(apiKey: null);

        var result = await service.TestAsync(AiProviderIds.XaiGrok);

        result.Success.Should().BeFalse();
        result.Summary.Should().Contain("No Grok API key");
        result.Capabilities.Should().ContainSingle(item =>
            item.Capability == "Credentials"
            && item.Status == AiCapabilityDiagnosticStatus.Failed
            && item.ErrorCategory == "missing_credentials");
    }

    [Fact]
    public async Task TestAsync_AzureWithKeyAndMissingEndpoint_FailsEndpointCheck()
    {
        var service = CreateService(
            providerId: AiProviderIds.AzureOpenAi,
            apiKey: "azure-key");

        var result = await service.TestAsync(AiProviderIds.AzureOpenAi);

        result.Success.Should().BeFalse();
        result.Summary.Should().Contain("endpoint is missing");
        result.Capabilities.Should().Contain(item =>
            item.Capability == "Endpoint"
            && item.Status == AiCapabilityDiagnosticStatus.Failed);
    }

    private static AiProviderDiagnosticsService CreateService(
        string providerId = AiProviderIds.XaiGrok,
        string? apiKey = "xai-test",
        RecordingAnalyticsService? analytics = null,
        Func<HttpClient>? httpClientFactory = null,
        Func<bool>? isAndroid = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.ProviderId.Returns(providerId);
        settings.ChatModel.Returns(GrokModelOptions.DefaultChat);
        settings.VisionModel.Returns(GrokModelOptions.DefaultVision);
        settings.AzureEndpoint.Returns((string?)null);

        var apiKeyService = Substitute.For<IApiKeyService>();
        apiKeyService.GetApiKeyAsync(Arg.Any<string>())
            .Returns(Task.FromResult(apiKey));

        return new AiProviderDiagnosticsService(
            settings,
            apiKeyService,
            new AiProviderRegistry(),
            analytics ?? new RecordingAnalyticsService(),
            httpClientFactory ?? (() => new HttpClient { BaseAddress = GrokApiClient.DefaultBaseUri }),
            isAndroid ?? (() => false),
            getEnvironmentVariable ?? (_ => null));
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
            return Task.FromResult(responder(request));
        }
    }
}
