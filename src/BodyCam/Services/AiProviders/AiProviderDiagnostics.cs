using System.Diagnostics;

namespace BodyCam.Services.AiProviders;

public enum AiCapabilityDiagnosticStatus
{
    Passed,
    Failed,
    Skipped,
}

public sealed record AiCapabilityDiagnostic(
    string Capability,
    AiCapabilityDiagnosticStatus Status,
    string Message,
    TimeSpan? Latency = null,
    string? ErrorCategory = null);

public sealed record AiProviderDiagnosticResult(
    string ProviderId,
    bool Success,
    string Summary,
    IReadOnlyList<AiCapabilityDiagnostic> Capabilities);

public interface IAiProviderDiagnosticsService
{
    Task<AiProviderDiagnosticResult> TestAsync(string providerId, CancellationToken ct = default);
}

public sealed class AiProviderDiagnosticsService : IAiProviderDiagnosticsService
{
    private static readonly byte[] OnePixelJpeg =
    [
        0xFF, 0xD8, 0xFF, 0xDB, 0x00, 0x43, 0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08, 0x07, 0x07,
        0x07, 0x09, 0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12, 0x13, 0x0F,
        0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20, 0x24, 0x2E, 0x27, 0x20, 0x22, 0x2C,
        0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29, 0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27, 0x39, 0x3D,
        0x38, 0x32, 0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01, 0x00, 0x01,
        0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x14, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0xFF, 0xC4, 0x00, 0x14, 0x10, 0x01,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0x7F, 0xFF, 0xD9
    ];

    private readonly ISettingsService _settings;
    private readonly IApiKeyService _apiKeyService;
    private readonly IAiProviderRegistry _registry;
    private readonly IAnalyticsService _analytics;
    private readonly Func<HttpClient> _httpClientFactory;
    private readonly Func<bool> _isAndroid;
    private readonly Func<string, string?> _getEnvironmentVariable;

    public AiProviderDiagnosticsService(
        ISettingsService settings,
        IApiKeyService apiKeyService,
        IAiProviderRegistry registry,
        IAnalyticsService analytics)
        : this(
            settings,
            apiKeyService,
            registry,
            analytics,
            () => new HttpClient { BaseAddress = GrokApiClient.DefaultBaseUri, Timeout = TimeSpan.FromSeconds(30) },
            OperatingSystem.IsAndroid,
            Environment.GetEnvironmentVariable)
    {
    }

    internal AiProviderDiagnosticsService(
        ISettingsService settings,
        IApiKeyService apiKeyService,
        IAiProviderRegistry registry,
        IAnalyticsService analytics,
        Func<HttpClient> httpClientFactory,
        Func<bool> isAndroid,
        Func<string, string?> getEnvironmentVariable)
    {
        _settings = settings;
        _apiKeyService = apiKeyService;
        _registry = registry;
        _analytics = analytics;
        _httpClientFactory = httpClientFactory;
        _isAndroid = isAndroid;
        _getEnvironmentVariable = getEnvironmentVariable;
    }

    public async Task<AiProviderDiagnosticResult> TestAsync(string providerId, CancellationToken ct = default)
    {
        providerId = AiProviderIds.Normalize(providerId);
        var provider = _registry.TryGet(providerId);
        if (provider is null)
        {
            return new AiProviderDiagnosticResult(
                providerId,
                Success: false,
                Summary: $"Provider '{providerId}' is not registered.",
                Capabilities: []);
        }

        var key = await _apiKeyService.GetApiKeyAsync(provider.Id);
        if (string.IsNullOrWhiteSpace(key))
        {
            return new AiProviderDiagnosticResult(
                provider.Id,
                Success: false,
                Summary: $"No {provider.DisplayName} API key configured.",
                Capabilities: [new("Credentials", AiCapabilityDiagnosticStatus.Failed, "API key missing.", ErrorCategory: "missing_credentials")]);
        }

        if (provider.Id == AiProviderIds.XaiGrok)
            return await TestGrokAsync(provider, key, ct);

        return TestConfiguredProvider(provider);
    }

    private async Task<AiProviderDiagnosticResult> TestGrokAsync(
        AiProviderDefinition provider,
        string apiKey,
        CancellationToken ct)
    {
        var diagnostics = new List<AiCapabilityDiagnostic>();
        if (!ShouldRunLiveGrokDiagnostics())
        {
            diagnostics.Add(new("Text", AiCapabilityDiagnosticStatus.Skipped,
                "Live xAI probe is enabled on Android or with BODYCAM_GROK_LIVE_TESTS=1."));
            diagnostics.Add(new("Vision", AiCapabilityDiagnosticStatus.Skipped,
                "Live xAI probe is enabled on Android or with BODYCAM_GROK_LIVE_TESTS=1."));
            diagnostics.Add(new("Voice", AiCapabilityDiagnosticStatus.Skipped,
                "Realtime voice requires Android phase-7 device testing and an ephemeral-token broker."));

            TrackDiagnostic(provider.Id, "configured", "skipped", null, null);
            return new AiProviderDiagnosticResult(
                provider.Id,
                Success: true,
                Summary: "Grok API key configured. Live xAI probes are waiting for Android phase 7.",
                Capabilities: diagnostics);
        }

        using var http = _httpClientFactory();
        var client = new GrokApiClient(http, apiKey);

        diagnostics.Add(await ProbeTextAsync(client, ct));
        diagnostics.Add(await ProbeVisionAsync(client, ct));
        diagnostics.Add(new("Voice", AiCapabilityDiagnosticStatus.Skipped,
            "Realtime voice is configured through wss://api.x.ai/v1/realtime and needs the Android audio-route matrix."));
        diagnostics.Add(new("STT", AiCapabilityDiagnosticStatus.Skipped,
            "Batch STT is wired through /v1/stt; run a device audio test before using it in a session."));
        diagnostics.Add(new("TTS", AiCapabilityDiagnosticStatus.Skipped,
            "Batch TTS is wired through /v1/tts; route playback is covered by the Android voice matrix."));
        diagnostics.Add(new("Images", AiCapabilityDiagnosticStatus.Skipped,
            "Image generation/editing is wired but not run by the default connection probe to avoid surprise cost."));

        var livePassed = diagnostics
            .Where(item => item.Capability is "Text" or "Vision")
            .All(item => item.Status == AiCapabilityDiagnosticStatus.Passed);

        return new AiProviderDiagnosticResult(
            provider.Id,
            Success: livePassed,
            Summary: livePassed
                ? "Grok text and vision live probes passed."
                : "One or more Grok live probes failed.",
            Capabilities: diagnostics);
    }

    private AiProviderDiagnosticResult TestConfiguredProvider(AiProviderDefinition provider)
    {
        var diagnostics = new List<AiCapabilityDiagnostic>
        {
            new("Credentials", AiCapabilityDiagnosticStatus.Passed, "API key configured.")
        };

        if (provider.Id == AiProviderIds.AzureOpenAi && string.IsNullOrWhiteSpace(_settings.AzureEndpoint))
        {
            diagnostics.Add(new("Endpoint", AiCapabilityDiagnosticStatus.Failed, "Azure endpoint is missing.", ErrorCategory: "missing_endpoint"));
            return new AiProviderDiagnosticResult(
                provider.Id,
                Success: false,
                Summary: "Azure API key is configured, but the endpoint is missing.",
                Capabilities: diagnostics);
        }

        return new AiProviderDiagnosticResult(
            provider.Id,
            Success: true,
            Summary: $"{provider.DisplayName} credentials are configured.",
            Capabilities: diagnostics);
    }

    private async Task<AiCapabilityDiagnostic> ProbeTextAsync(GrokApiClient client, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var provider = new GrokTextProvider(client);
            var response = await provider.GetTextAsync(new AiTextRequest(
                GrokModelOptions.NormalizeChatModel(_settings.ChatModel),
                [new("user", "Reply with exactly: OK")],
                MaxOutputTokens: 8), ct);

            sw.Stop();
            var passed = !string.IsNullOrWhiteSpace(response.Text);
            TrackDiagnostic(AiProviderIds.XaiGrok, "text", passed ? "success" : "empty", sw.Elapsed, null);
            return new AiCapabilityDiagnostic(
                "Text",
                passed ? AiCapabilityDiagnosticStatus.Passed : AiCapabilityDiagnosticStatus.Failed,
                passed ? "Chat completion returned text." : "Chat completion returned no text.",
                sw.Elapsed,
                passed ? null : "empty_response");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            var category = Categorize(ex);
            TrackDiagnostic(AiProviderIds.XaiGrok, "text", "error", sw.Elapsed, category);
            return new AiCapabilityDiagnostic("Text", AiCapabilityDiagnosticStatus.Failed, ex.Message, sw.Elapsed, category);
        }
    }

    private async Task<AiCapabilityDiagnostic> ProbeVisionAsync(GrokApiClient client, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var provider = new GrokVisionProvider(client);
            var response = await provider.DescribeImageAsync(new AiVisionRequest(
                GrokModelOptions.NormalizeVisionModel(_settings.VisionModel),
                "Reply with one short sentence about this test image.",
                OnePixelJpeg,
                "image/jpeg",
                Detail: "low"), ct);

            sw.Stop();
            var passed = !string.IsNullOrWhiteSpace(response.Text);
            TrackDiagnostic(AiProviderIds.XaiGrok, "vision", passed ? "success" : "empty", sw.Elapsed, null);
            return new AiCapabilityDiagnostic(
                "Vision",
                passed ? AiCapabilityDiagnosticStatus.Passed : AiCapabilityDiagnosticStatus.Failed,
                passed ? "Vision completion returned text." : "Vision completion returned no text.",
                sw.Elapsed,
                passed ? null : "empty_response");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            var category = Categorize(ex);
            TrackDiagnostic(AiProviderIds.XaiGrok, "vision", "error", sw.Elapsed, category);
            return new AiCapabilityDiagnostic("Vision", AiCapabilityDiagnosticStatus.Failed, ex.Message, sw.Elapsed, category);
        }
    }

    private bool ShouldRunLiveGrokDiagnostics() =>
        _isAndroid()
        || string.Equals(_getEnvironmentVariable("BODYCAM_GROK_LIVE_TESTS"), "1", StringComparison.Ordinal);

    private void TrackDiagnostic(
        string providerId,
        string capability,
        string result,
        TimeSpan? latency,
        string? errorCategory)
    {
        var properties = new Dictionary<string, string>
        {
            ["provider.id"] = providerId,
            ["capability.path"] = capability,
            ["result"] = result,
            ["fallback.path"] = "none",
        };

        if (errorCategory is not null)
            properties["error.category"] = errorCategory;
        if (latency is not null)
            properties["latency.ms"] = ((int)latency.Value.TotalMilliseconds).ToString();

        _analytics.TrackEvent("ai.provider.diagnostic", properties);
        if (latency is not null)
            _analytics.TrackMetric("ai.provider.latency_ms", latency.Value.TotalMilliseconds, properties);
    }

    private static string Categorize(Exception ex)
    {
        if (ex is HttpRequestException)
            return "network";
        if (ex.Message.Contains("401", StringComparison.Ordinal)
            || ex.Message.Contains("403", StringComparison.Ordinal))
        {
            return "auth";
        }
        if (ex.Message.Contains("429", StringComparison.Ordinal))
            return "rate_limit";
        if (ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "timeout";

        return "provider_error";
    }
}
