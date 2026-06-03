using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace BodyCam.Services.AiProviders;

#pragma warning disable OPENAI002

/// <summary>
/// Creates provider-specific realtime clients only when an active session starts.
/// </summary>
public sealed class AppRealtimeClient : IRealtimeClient
{
    private readonly AppSettings _settings;
    private readonly IApiKeyService _apiKeyService;
    private readonly IAiProviderRegistry _providers;
    private readonly IServiceProvider _services;

    public AppRealtimeClient(
        AppSettings settings,
        IApiKeyService apiKeyService,
        IAiProviderRegistry providers,
        IServiceProvider services)
    {
        _settings = settings;
        _apiKeyService = apiKeyService;
        _providers = providers;
        _services = services;
    }

    public async Task<IRealtimeClientSession> CreateSessionAsync(
        RealtimeSessionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var provider = _providers.GetRequired(_settings.ProviderId);
        var apiKey = await _apiKeyService.GetApiKeyAsync(provider.Id);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("API key not configured.");

        var client = CreateClient(provider, apiKey);
        return await client.CreateSessionAsync(options, cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private IRealtimeClient CreateClient(AiProviderDefinition provider, string apiKey)
    {
        if (provider.Id == AiProviderIds.AzureOpenAi)
        {
            if (string.IsNullOrWhiteSpace(_settings.AzureEndpoint))
                throw new InvalidOperationException("Azure OpenAI endpoint is not configured.");

            if (string.IsNullOrWhiteSpace(_settings.AzureRealtimeDeploymentName))
                throw new InvalidOperationException("Azure OpenAI realtime deployment is not configured.");

            var rtOptions = new OpenAI.Realtime.RealtimeClientOptions
            {
                Endpoint = new Uri($"{_settings.AzureEndpoint.TrimEnd('/')}/openai/v1/realtime")
            };
            var sdkClient = new AzureRealtimeClient(apiKey, rtOptions);
            return new OpenAIRealtimeClient(sdkClient, _settings.AzureRealtimeDeploymentName);
        }

        if (provider.Id == AiProviderIds.OpenAi)
        {
            return new OpenAIRealtimeClient(apiKey, _settings.RealtimeModel);
        }

        if (provider.Id == AiProviderIds.XaiGrok)
        {
            var grokRealtime = _services.GetRequiredService<IGrokRealtimeVoiceProvider>()
                .CreateSessionOptions(_settings);
            return new UnsupportedRealtimeClient(
                $"Grok realtime voice is configured for {grokRealtime.WebSocketUri}; the Android realtime session still needs the device audio-route implementation.");
        }

        throw new InvalidOperationException(
            $"Realtime client creation is not implemented for provider '{provider.DisplayName}'.");
    }
}

#pragma warning restore OPENAI002
