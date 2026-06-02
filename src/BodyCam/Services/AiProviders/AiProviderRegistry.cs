namespace BodyCam.Services.AiProviders;

public static class AiProviderIds
{
    public const string OpenAi = "openai";
    public const string AzureOpenAi = "azure-openai";
    public const string XaiGrok = "xai-grok";

    public static string Normalize(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return OpenAi;

        return providerId.Trim().ToLowerInvariant() switch
        {
            "azure" or "azureopenai" or "azure-open-ai" => AzureOpenAi,
            "open-ai" => OpenAi,
            "grok" or "xai" => XaiGrok,
            var id => id
        };
    }

    public static string FromLegacyProvider(OpenAiProvider provider) => provider switch
    {
        OpenAiProvider.Azure => AzureOpenAi,
        _ => OpenAi
    };

    public static string FromLegacyProviderName(string? provider)
    {
        if (Enum.TryParse<OpenAiProvider>(provider, true, out var legacyProvider))
            return FromLegacyProvider(legacyProvider);

        return Normalize(provider);
    }

    public static OpenAiProvider ToLegacyProvider(string? providerId) =>
        Normalize(providerId) == AzureOpenAi ? OpenAiProvider.Azure : OpenAiProvider.OpenAi;
}

public enum AiCredentialMode
{
    ApiKey,
    OAuthPkce,
    OAuthDeviceCode,
    EphemeralClientSecret
}

[Flags]
public enum AiProviderCapability
{
    None = 0,
    Chat = 1 << 0,
    Vision = 1 << 1,
    RealtimeVoice = 1 << 2,
    SpeechToText = 1 << 3,
    TextToSpeech = 1 << 4,
    ImageInput = 1 << 5,
    ImageGeneration = 1 << 6,
    OAuth = 1 << 7,
    ImageEditing = 1 << 8,
    StructuredVisualOutput = 1 << 9
}

public enum AiModelKind
{
    Realtime,
    Chat,
    Vision,
    Transcription,
    TextToSpeech,
    ImageGeneration
}

public sealed record AiProviderSetupLink(
    string Label,
    Uri Url,
    AiProviderSetupLinkKind Kind);

public enum AiProviderSetupLinkKind
{
    Account,
    Billing,
    ApiKeys,
    Documentation,
    Portal
}

public sealed record AiProviderCredentialPolicy(
    bool OAuthAvailable,
    string? OAuthUnavailableReason,
    bool RequiresEphemeralRealtimeTokenBroker,
    Uri? AuthDocumentationUrl = null,
    Uri? EphemeralTokenDocumentationUrl = null)
{
    public static AiProviderCredentialPolicy ApiKeyOnly { get; } =
        new(
            OAuthAvailable: false,
            OAuthUnavailableReason: null,
            RequiresEphemeralRealtimeTokenBroker: false);
}

public sealed record AiProviderDefinition(
    string Id,
    string DisplayName,
    string ShortName,
    string Description,
    bool IsSelectable,
    IReadOnlyList<AiCredentialMode> CredentialModes,
    AiProviderCapability Capabilities,
    IReadOnlyDictionary<AiModelKind, ModelInfo[]> Models,
    AiProviderCredentialPolicy CredentialPolicy,
    IReadOnlyList<AiProviderSetupLink> SetupLinks)
{
    public bool Supports(AiProviderCapability capability) => (Capabilities & capability) == capability;
}

public interface IAiProviderAdapter
{
    AiProviderDefinition Definition { get; }
    Uri GetRealtimeUri(AppSettings settings);
    Uri GetChatUri(AppSettings settings);
    Uri GetVisionUri(AppSettings settings);
}

public interface IAiProviderRegistry
{
    IReadOnlyList<AiProviderDefinition> Providers { get; }
    AiProviderDefinition? TryGet(string? providerId);
    AiProviderDefinition GetRequired(string? providerId);
    IAiProviderAdapter GetRequiredAdapter(string? providerId);
    ModelInfo[] GetModels(string? providerId, AiModelKind kind);
}

public sealed class AiProviderRegistry : IAiProviderRegistry
{
    private readonly Dictionary<string, IAiProviderAdapter> _adapters;

    public static IAiProviderRegistry Default { get; } = new AiProviderRegistry();

    public AiProviderRegistry()
        : this(CreateDefaultAdapters())
    {
    }

    public AiProviderRegistry(IEnumerable<IAiProviderAdapter> adapters)
    {
        var adapterList = adapters.ToArray();
        if (adapterList.Length == 0)
            adapterList = CreateDefaultAdapters().ToArray();

        _adapters = adapterList.ToDictionary(
            adapter => AiProviderIds.Normalize(adapter.Definition.Id),
            adapter => adapter,
            StringComparer.OrdinalIgnoreCase);

        Providers = adapterList
            .Select(adapter => adapter.Definition)
            .OrderBy(provider => provider.IsSelectable ? 0 : 1)
            .ToArray();
    }

    public IReadOnlyList<AiProviderDefinition> Providers { get; }

    public AiProviderDefinition? TryGet(string? providerId)
    {
        var id = AiProviderIds.Normalize(providerId);
        return _adapters.TryGetValue(id, out var adapter) ? adapter.Definition : null;
    }

    public AiProviderDefinition GetRequired(string? providerId) =>
        GetRequiredAdapter(providerId).Definition;

    public IAiProviderAdapter GetRequiredAdapter(string? providerId)
    {
        var id = AiProviderIds.Normalize(providerId);
        if (_adapters.TryGetValue(id, out var adapter))
            return adapter;

        throw new InvalidOperationException($"AI provider '{id}' is not registered.");
    }

    public ModelInfo[] GetModels(string? providerId, AiModelKind kind)
    {
        var definition = TryGet(providerId);
        if (definition is null)
            return [];

        return definition.Models.TryGetValue(kind, out var models) ? models : [];
    }

    private static IEnumerable<IAiProviderAdapter> CreateDefaultAdapters()
    {
        yield return new OpenAiProviderAdapter();
        yield return new AzureOpenAiProviderAdapter();
        yield return new XaiGrokProviderAdapter();
    }
}

public sealed class OpenAiProviderAdapter : IAiProviderAdapter
{
    public AiProviderDefinition Definition { get; } = new(
        AiProviderIds.OpenAi,
        "OpenAI",
        "OpenAI",
        "Direct OpenAI API using API key credentials.",
        IsSelectable: true,
        CredentialModes: [AiCredentialMode.ApiKey, AiCredentialMode.EphemeralClientSecret],
        Capabilities: AiProviderCapability.Chat
            | AiProviderCapability.Vision
            | AiProviderCapability.RealtimeVoice
            | AiProviderCapability.SpeechToText
            | AiProviderCapability.TextToSpeech
            | AiProviderCapability.ImageInput,
        Models: new Dictionary<AiModelKind, ModelInfo[]>
        {
            [AiModelKind.Realtime] = ModelOptions.RealtimeModels,
            [AiModelKind.Chat] = ModelOptions.ChatModels,
            [AiModelKind.Vision] = ModelOptions.VisionModels,
            [AiModelKind.Transcription] = ModelOptions.TranscriptionModels,
        },
        CredentialPolicy: AiProviderCredentialPolicy.ApiKeyOnly,
        SetupLinks:
        [
            new("Open API keys", new Uri("https://platform.openai.com/api-keys"), AiProviderSetupLinkKind.ApiKeys),
            new("Open billing", new Uri("https://platform.openai.com/settings/organization/billing/overview"), AiProviderSetupLinkKind.Billing),
            new("Open docs", new Uri("https://platform.openai.com/docs"), AiProviderSetupLinkKind.Documentation),
        ]);

    public Uri GetRealtimeUri(AppSettings settings) =>
        new($"{settings.RealtimeApiEndpoint}?model={settings.RealtimeModel}");

    public Uri GetChatUri(AppSettings settings) => new(settings.ChatApiEndpoint);

    public Uri GetVisionUri(AppSettings settings) => new(settings.ChatApiEndpoint);
}

public sealed class AzureOpenAiProviderAdapter : IAiProviderAdapter
{
    public AiProviderDefinition Definition { get; } = new(
        AiProviderIds.AzureOpenAi,
        "Azure OpenAI",
        "Azure",
        "Azure OpenAI deployments using Azure endpoint, deployment names, and API key credentials.",
        IsSelectable: true,
        CredentialModes: [AiCredentialMode.ApiKey],
        Capabilities: AiProviderCapability.Chat
            | AiProviderCapability.Vision
            | AiProviderCapability.RealtimeVoice
            | AiProviderCapability.SpeechToText
            | AiProviderCapability.TextToSpeech
            | AiProviderCapability.ImageInput,
        Models: new Dictionary<AiModelKind, ModelInfo[]>(),
        CredentialPolicy: AiProviderCredentialPolicy.ApiKeyOnly,
        SetupLinks:
        [
            new("Open Azure portal", new Uri("https://portal.azure.com/"), AiProviderSetupLinkKind.Portal),
            new("Open Azure OpenAI docs", new Uri("https://learn.microsoft.com/azure/ai-services/openai/"), AiProviderSetupLinkKind.Documentation),
        ]);

    public Uri GetRealtimeUri(AppSettings settings)
    {
        var azureBase = settings.AzureEndpoint?.TrimEnd('/') ?? string.Empty;
        return new Uri($"{azureBase.Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)}/openai/realtime"
            + $"?api-version={settings.AzureApiVersion}&deployment={settings.AzureRealtimeDeploymentName}");
    }

    public Uri GetChatUri(AppSettings settings)
    {
        var azureBase = settings.AzureEndpoint?.TrimEnd('/') ?? string.Empty;
        return new Uri($"{azureBase}/openai/deployments/{settings.AzureChatDeploymentName}"
            + $"/chat/completions?api-version={settings.AzureApiVersion}");
    }

    public Uri GetVisionUri(AppSettings settings)
    {
        var azureBase = settings.AzureEndpoint?.TrimEnd('/') ?? string.Empty;
        return new Uri($"{azureBase}/openai/deployments/{settings.AzureVisionDeploymentName}"
            + $"/chat/completions?api-version={settings.AzureApiVersion}");
    }
}

public sealed class XaiGrokProviderAdapter : IAiProviderAdapter
{
    public AiProviderDefinition Definition { get; } = new(
        AiProviderIds.XaiGrok,
        "Grok",
        "Grok",
        "xAI Grok via documented API-key auth. OAuth is not official for inference API access yet.",
        IsSelectable: true,
        CredentialModes: [AiCredentialMode.ApiKey, AiCredentialMode.EphemeralClientSecret],
        Capabilities: AiProviderCapability.Chat
            | AiProviderCapability.Vision
            | AiProviderCapability.RealtimeVoice
            | AiProviderCapability.SpeechToText
            | AiProviderCapability.TextToSpeech
            | AiProviderCapability.ImageInput
            | AiProviderCapability.ImageGeneration
            | AiProviderCapability.ImageEditing
            | AiProviderCapability.StructuredVisualOutput,
        Models: new Dictionary<AiModelKind, ModelInfo[]>
        {
            [AiModelKind.Realtime] = GrokModelOptions.RealtimeModels,
            [AiModelKind.Chat] = GrokModelOptions.ChatModels,
            [AiModelKind.Vision] = GrokModelOptions.VisionModels,
            [AiModelKind.Transcription] = GrokModelOptions.TranscriptionModels,
            [AiModelKind.TextToSpeech] = GrokModelOptions.TextToSpeechModels,
            [AiModelKind.ImageGeneration] = GrokModelOptions.ImageGenerationModels,
        },
        CredentialPolicy: new AiProviderCredentialPolicy(
            OAuthAvailable: false,
            OAuthUnavailableReason: "xAI inference docs currently document API-key bearer auth. No official third-party OAuth flow for inference API access has been found.",
            RequiresEphemeralRealtimeTokenBroker: true,
            AuthDocumentationUrl: new Uri("https://docs.x.ai/developers/rest-api-reference/inference"),
            EphemeralTokenDocumentationUrl: new Uri("https://docs.x.ai/developers/model-capabilities/audio/ephemeral-tokens")),
        SetupLinks:
        [
            new("Open xAI account", new Uri("https://accounts.x.ai/"), AiProviderSetupLinkKind.Account),
            new("Open xAI console", new Uri("https://console.x.ai/"), AiProviderSetupLinkKind.Portal),
            new("Open API keys", new Uri("https://console.x.ai/team/default/api-keys"), AiProviderSetupLinkKind.ApiKeys),
            new("Open docs", new Uri("https://docs.x.ai/developers/quickstart"), AiProviderSetupLinkKind.Documentation),
        ]);

    public Uri GetRealtimeUri(AppSettings settings) =>
        new($"wss://api.x.ai/v1/realtime?model={GrokModelOptions.NormalizeRealtimeModel(settings.RealtimeModel)}");

    public Uri GetChatUri(AppSettings settings) => new("https://api.x.ai/v1/chat/completions");

    public Uri GetVisionUri(AppSettings settings) => new("https://api.x.ai/v1/chat/completions");
}
