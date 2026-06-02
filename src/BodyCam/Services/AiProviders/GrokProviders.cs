using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace BodyCam.Services.AiProviders;

public sealed record AiTextMessage(string Role, string Text);

public sealed record AiToolDefinition(string Name, string Description, JsonNode? Parameters = null);

public sealed record AiToolCall(string Id, string Name, string ArgumentsJson);

public sealed record AiTextRequest(
    string Model,
    IReadOnlyList<AiTextMessage> Messages,
    IReadOnlyList<AiToolDefinition>? Tools = null,
    int? MaxOutputTokens = null);

public sealed record AiTextResponse(
    string Text,
    IReadOnlyList<AiToolCall> ToolCalls,
    string? ResponseId = null,
    string? ModelId = null,
    AiTokenUsage? Usage = null);

public sealed record AiTokenUsage(
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens);

public sealed record AiVisionRequest(
    string Model,
    string Prompt,
    byte[] ImageBytes,
    string MediaType = "image/jpeg",
    string? SystemPrompt = null,
    string Detail = "high");

public sealed record AiSpeechToTextRequest(
    byte[] AudioBytes,
    string FileName,
    string MediaType,
    string Language = "en",
    bool FormatText = true,
    IReadOnlyList<string>? KeyTerms = null);

public sealed record AiSpeechToTextResponse(
    string Text,
    string? Language,
    double? DurationSeconds);

public sealed record AiTextToSpeechRequest(
    string Text,
    string VoiceId = "eve",
    string Language = "en",
    string Codec = "mp3",
    int SampleRate = 24000,
    int BitRate = 128000);

public sealed record AiTextToSpeechResponse(byte[] AudioBytes, string ContentType);

public sealed record AiImageGenerationRequest(
    string Prompt,
    string Model = GrokModelOptions.DefaultImageGeneration,
    string ResponseFormat = "url",
    int Count = 1,
    byte[]? SourceImageBytes = null,
    string SourceImageMediaType = "image/jpeg",
    Uri? SourceImageUrl = null);

public sealed record AiGeneratedImage(
    string? Url,
    string? Base64Json,
    string? MimeType,
    string? RevisedPrompt);

public sealed record AiImageGenerationResponse(IReadOnlyList<AiGeneratedImage> Images);

public sealed record GrokRealtimeVoiceSessionOptions(
    Uri WebSocketUri,
    string ModelId,
    bool RequiresEphemeralClientSecret,
    Uri ClientSecretEndpoint);

public interface IAiTextProvider
{
    Task<AiTextResponse> GetTextAsync(AiTextRequest request, CancellationToken ct = default);
}

public interface IAiVisionProvider
{
    Task<AiTextResponse> DescribeImageAsync(AiVisionRequest request, CancellationToken ct = default);
}

public interface IAiSpeechToTextProvider
{
    Task<AiSpeechToTextResponse> TranscribeAsync(AiSpeechToTextRequest request, CancellationToken ct = default);
}

public interface IAiTextToSpeechProvider
{
    Task<AiTextToSpeechResponse> SynthesizeAsync(AiTextToSpeechRequest request, CancellationToken ct = default);
}

public interface IAiImageGenerationProvider
{
    Task<AiImageGenerationResponse> GenerateAsync(AiImageGenerationRequest request, CancellationToken ct = default);
}

public interface IGrokRealtimeVoiceProvider
{
    GrokRealtimeVoiceSessionOptions CreateSessionOptions(AppSettings settings);
}

public sealed class GrokApiClient
{
    public static readonly Uri DefaultBaseUri = new("https://api.x.ai/v1/");
    public static readonly Uri DefaultRealtimeClientSecretUri = new("https://api.x.ai/v1/realtime/client_secrets");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public GrokApiClient(string apiKey)
        : this(new HttpClient { BaseAddress = DefaultBaseUri, Timeout = TimeSpan.FromSeconds(60) }, apiKey)
    {
    }

    internal GrokApiClient(HttpClient http, string apiKey)
    {
        _http = http;
        if (_http.BaseAddress is null)
            _http.BaseAddress = DefaultBaseUri;

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    internal async Task<JsonDocument> PostJsonAsync(string path, JsonNode payload, CancellationToken ct)
    {
        using var content = new StringContent(payload.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(path, content, ct);
        await ThrowIfUnsuccessfulAsync(response, path, ct);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    internal async Task<JsonDocument> PostMultipartAsync(string path, MultipartFormDataContent content, CancellationToken ct)
    {
        using var response = await _http.PostAsync(path, content, ct);
        await ThrowIfUnsuccessfulAsync(response, path, ct);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    internal async Task<(byte[] Bytes, string ContentType)> PostJsonForBytesAsync(string path, JsonNode payload, CancellationToken ct)
    {
        using var content = new StringContent(payload.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(path, content, ct);
        await ThrowIfUnsuccessfulAsync(response, path, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return (bytes, contentType);
    }

    private static async Task ThrowIfUnsuccessfulAsync(HttpResponseMessage response, string path, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        var reason = string.IsNullOrWhiteSpace(body)
            ? response.ReasonPhrase ?? response.StatusCode.ToString()
            : body;
        throw new InvalidOperationException($"xAI {path} returned {(int)response.StatusCode}: {reason}");
    }
}

public sealed class GrokTextProvider : IAiTextProvider
{
    private readonly GrokApiClient _client;

    public GrokTextProvider(GrokApiClient client) => _client = client;

    public async Task<AiTextResponse> GetTextAsync(AiTextRequest request, CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["model"] = GrokModelOptions.NormalizeChatModel(request.Model),
            ["messages"] = new JsonArray(request.Messages.Select(CreateTextMessageNode).ToArray()),
            ["stream"] = false
        };

        if (request.MaxOutputTokens is > 0)
            payload["max_completion_tokens"] = request.MaxOutputTokens.Value;

        if (request.Tools is { Count: > 0 })
            payload["tools"] = new JsonArray(request.Tools.Select(CreateToolNode).ToArray());

        using var doc = await _client.PostJsonAsync("chat/completions", payload, ct);
        return ParseChatCompletion(doc.RootElement);
    }

    private static JsonNode CreateTextMessageNode(AiTextMessage message) =>
        new JsonObject
        {
            ["role"] = NormalizeRole(message.Role),
            ["content"] = message.Text
        };

    private static JsonNode CreateToolNode(AiToolDefinition tool) =>
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = tool.Parameters?.DeepClone() ?? CreateEmptyParametersSchema()
            }
        };

    private static JsonObject CreateEmptyParametersSchema() =>
        new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject()
        };

    internal static string NormalizeRole(string role) =>
        role.Trim().ToLowerInvariant() switch
        {
            "system" => "system",
            "assistant" => "assistant",
            "tool" => "tool",
            _ => "user"
        };

    internal static AiTextResponse ParseChatCompletion(JsonElement root)
    {
        var responseId = ReadString(root, "id");
        var modelId = ReadString(root, "model");
        var usage = ReadUsage(root);
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return new AiTextResponse(string.Empty, [], responseId, modelId, usage);

        var first = choices.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined)
            return new AiTextResponse(string.Empty, [], responseId, modelId, usage);

        if (!first.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            return new AiTextResponse(string.Empty, [], responseId, modelId, usage);

        var text = ReadContentText(message);
        var toolCalls = ReadToolCalls(message);
        return new AiTextResponse(text, toolCalls, responseId, modelId, usage);
    }

    internal static string ReadContentText(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
            return string.Empty;

        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? string.Empty;

        if (content.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var item in content.EnumerateArray())
        {
            if (ReadString(item, "text") is { Length: > 0 } text)
                builder.Append(text);
        }

        return builder.ToString();
    }

    private static AiToolCall[] ReadToolCalls(JsonElement message)
    {
        if (!message.TryGetProperty("tool_calls", out var toolCalls)
            || toolCalls.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var calls = new List<AiToolCall>();
        foreach (var item in toolCalls.EnumerateArray())
        {
            var id = ReadString(item, "id") ?? string.Empty;
            if (!item.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object)
                continue;

            var name = ReadString(function, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var arguments = ReadString(function, "arguments") ?? "{}";
            calls.Add(new AiToolCall(id, name, arguments));
        }

        return calls.ToArray();
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static AiTokenUsage? ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return null;

        return new AiTokenUsage(
            PromptTokens: ReadInt(usage, "prompt_tokens"),
            CompletionTokens: ReadInt(usage, "completion_tokens"),
            TotalTokens: ReadInt(usage, "total_tokens"));
    }

    private static int? ReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
}

public sealed class GrokVisionProvider : IAiVisionProvider
{
    private readonly GrokApiClient _client;

    public GrokVisionProvider(GrokApiClient client) => _client = client;

    public async Task<AiTextResponse> DescribeImageAsync(AiVisionRequest request, CancellationToken ct = default)
    {
        var messages = new JsonArray();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = request.SystemPrompt
            });
        }

        messages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray
            {
                CreateImageContentNode(CreateDataUri(request.ImageBytes, request.MediaType), request.Detail),
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = request.Prompt
                }
            }
        });

        var payload = new JsonObject
        {
            ["model"] = GrokModelOptions.NormalizeVisionModel(request.Model),
            ["messages"] = messages,
            ["stream"] = false
        };

        using var doc = await _client.PostJsonAsync("chat/completions", payload, ct);
        return GrokTextProvider.ParseChatCompletion(doc.RootElement);
    }

    internal static JsonObject CreateImageContentNode(string imageUrl, string detail = "high") =>
        new()
        {
            ["type"] = "image_url",
            ["image_url"] = new JsonObject
            {
                ["url"] = imageUrl,
                ["detail"] = string.IsNullOrWhiteSpace(detail) ? "high" : detail
            }
        };

    internal static string CreateDataUri(byte[] bytes, string mediaType) =>
        $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";
}

public sealed class GrokSpeechToTextProvider : IAiSpeechToTextProvider
{
    private readonly GrokApiClient _client;

    public GrokSpeechToTextProvider(GrokApiClient client) => _client = client;

    public async Task<AiSpeechToTextResponse> TranscribeAsync(AiSpeechToTextRequest request, CancellationToken ct = default)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(request.FormatText ? "true" : "false"), "format");
        multipart.Add(new StringContent(request.Language), "language");

        if (request.KeyTerms is not null)
        {
            foreach (var keyTerm in request.KeyTerms.Where(term => !string.IsNullOrWhiteSpace(term)))
                multipart.Add(new StringContent(keyTerm), "keyterm");
        }

        var audio = new ByteArrayContent(request.AudioBytes);
        audio.Headers.ContentType = new MediaTypeHeaderValue(request.MediaType);
        multipart.Add(audio, "file", request.FileName);

        using var doc = await _client.PostMultipartAsync("stt", multipart, ct);
        var root = doc.RootElement;
        var text = root.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
            ? textElement.GetString() ?? string.Empty
            : string.Empty;
        var language = root.TryGetProperty("language", out var languageElement) && languageElement.ValueKind == JsonValueKind.String
            ? languageElement.GetString()
            : null;
        var duration = root.TryGetProperty("duration", out var durationElement) && durationElement.ValueKind == JsonValueKind.Number
            ? durationElement.GetDouble()
            : (double?)null;

        return new AiSpeechToTextResponse(text, language, duration);
    }
}

public sealed class GrokTextToSpeechProvider : IAiTextToSpeechProvider
{
    private readonly GrokApiClient _client;

    public GrokTextToSpeechProvider(GrokApiClient client) => _client = client;

    public async Task<AiTextToSpeechResponse> SynthesizeAsync(AiTextToSpeechRequest request, CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["text"] = request.Text,
            ["voice_id"] = request.VoiceId,
            ["language"] = request.Language,
            ["output_format"] = new JsonObject
            {
                ["codec"] = request.Codec,
                ["sample_rate"] = request.SampleRate,
                ["bit_rate"] = request.BitRate
            }
        };

        var (bytes, contentType) = await _client.PostJsonForBytesAsync("tts", payload, ct);
        return new AiTextToSpeechResponse(bytes, contentType);
    }
}

public sealed class GrokImageGenerationProvider : IAiImageGenerationProvider
{
    private readonly GrokApiClient _client;

    public GrokImageGenerationProvider(GrokApiClient client) => _client = client;

    public async Task<AiImageGenerationResponse> GenerateAsync(AiImageGenerationRequest request, CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["model"] = GrokModelOptions.NormalizeImageGenerationModel(request.Model),
            ["prompt"] = request.Prompt,
            ["response_format"] = request.ResponseFormat
        };

        var path = "images/generations";
        if (request.SourceImageBytes is not null || request.SourceImageUrl is not null)
        {
            path = "images/edits";
            payload["image"] = CreateSourceImageNode(request);
        }
        else if (request.Count > 1)
        {
            payload["n"] = request.Count;
        }

        using var doc = await _client.PostJsonAsync(path, payload, ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return new AiImageGenerationResponse([]);

        var images = data.EnumerateArray()
            .Select(item => new AiGeneratedImage(
                Url: ReadString(item, "url"),
                Base64Json: ReadString(item, "b64_json"),
                MimeType: ReadString(item, "mime_type"),
                RevisedPrompt: ReadString(item, "revised_prompt")))
            .ToArray();

        return new AiImageGenerationResponse(images);
    }

    private static JsonObject CreateSourceImageNode(AiImageGenerationRequest request)
    {
        if (request.SourceImageUrl is not null)
        {
            return new JsonObject
            {
                ["url"] = request.SourceImageUrl.ToString(),
                ["type"] = "image_url"
            };
        }

        return new JsonObject
        {
            ["url"] = GrokVisionProvider.CreateDataUri(request.SourceImageBytes!, request.SourceImageMediaType),
            ["type"] = "image_url"
        };
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

public sealed class GrokRealtimeVoiceProvider : IGrokRealtimeVoiceProvider
{
    public GrokRealtimeVoiceSessionOptions CreateSessionOptions(AppSettings settings)
    {
        var model = GrokModelOptions.NormalizeRealtimeModel(settings.RealtimeModel);
        return new GrokRealtimeVoiceSessionOptions(
            WebSocketUri: new Uri($"wss://api.x.ai/v1/realtime?model={model}"),
            ModelId: model,
            RequiresEphemeralClientSecret: true,
            ClientSecretEndpoint: GrokApiClient.DefaultRealtimeClientSecretUri);
    }
}

public sealed class GrokChatClient : IChatClient
{
    private readonly GrokApiClient _client;
    private readonly AppSettings _settings;
    private readonly IAnalyticsService? _analytics;
    private readonly HttpClient? _ownedHttpClient;

    public GrokChatClient(string apiKey, AppSettings settings, IAnalyticsService? analytics = null)
    {
        _ownedHttpClient = new HttpClient { BaseAddress = GrokApiClient.DefaultBaseUri, Timeout = TimeSpan.FromSeconds(60) };
        _client = new GrokApiClient(_ownedHttpClient, apiKey);
        _settings = settings;
        _analytics = analytics;
    }

    internal GrokChatClient(GrokApiClient client, AppSettings settings, IAnalyticsService? analytics = null)
    {
        _client = client;
        _settings = settings;
        _analytics = analytics;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messages = chatMessages.ToArray();
        var hasImage = messages.Any(ContainsImageContent);
        var payload = CreateChatCompletionPayload(messages, options, hasImage);
        var model = payload["model"]?.GetValue<string>() ?? string.Empty;
        var capabilityPath = hasImage ? "vision" : "text";
        var sw = Stopwatch.StartNew();

        try
        {
            using var doc = await _client.PostJsonAsync("chat/completions", payload, cancellationToken);
            var response = GrokTextProvider.ParseChatCompletion(doc.RootElement);
            sw.Stop();
            TrackProviderRequest(capabilityPath, model, "success", sw.Elapsed, response.Usage, null);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, response.Text));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            TrackProviderRequest(capabilityPath, model, "error", sw.Elapsed, null, Categorize(ex));
            throw;
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(chatMessages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() => _ownedHttpClient?.Dispose();

    internal JsonObject CreateChatCompletionPayload(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        bool hasImage)
    {
        var model = options?.ModelId
            ?? (hasImage
                ? GrokModelOptions.NormalizeVisionModel(_settings.VisionModel)
                : GrokModelOptions.NormalizeChatModel(_settings.ChatModel));

        if (hasImage)
            model = GrokModelOptions.NormalizeVisionModel(model);
        else
            model = GrokModelOptions.NormalizeChatModel(model);

        var payload = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(messages.Select(CreateMessageNode).ToArray()),
            ["stream"] = false
        };

        if (options?.MaxOutputTokens is > 0)
            payload["max_completion_tokens"] = options.MaxOutputTokens.Value;
        if (options?.Temperature is not null)
            payload["temperature"] = options.Temperature.Value;
        if (options?.TopP is not null)
            payload["top_p"] = options.TopP.Value;
        if (options?.Tools is { Count: > 0 })
            payload["tools"] = new JsonArray(options.Tools.Select(CreateToolNode).ToArray());

        return payload;
    }

    private static bool ContainsImageContent(ChatMessage message) =>
        message.Contents.OfType<DataContent>()
            .Any(content => content.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true);

    private static JsonNode CreateMessageNode(ChatMessage message)
    {
        if (message.Contents.Count == 0)
        {
            return new JsonObject
            {
                ["role"] = GrokTextProvider.NormalizeRole(message.Role.Value),
                ["content"] = message.Text
            };
        }

        if (message.Contents.Count == 1 && message.Contents[0] is TextContent onlyText)
        {
            return new JsonObject
            {
                ["role"] = GrokTextProvider.NormalizeRole(message.Role.Value),
                ["content"] = onlyText.Text
            };
        }

        return new JsonObject
        {
            ["role"] = GrokTextProvider.NormalizeRole(message.Role.Value),
            ["content"] = new JsonArray(message.Contents.Select(CreateContentNode).ToArray())
        };
    }

    private static JsonNode CreateContentNode(AIContent content)
    {
        if (content is TextContent text)
        {
            return new JsonObject
            {
                ["type"] = "text",
                ["text"] = text.Text
            };
        }

        if (content is DataContent data && data.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
        {
            var uri = !string.IsNullOrWhiteSpace(data.Uri)
                ? data.Uri
                : $"data:{data.MediaType!};base64,{Convert.ToBase64String(data.Data.ToArray())}";
            return GrokVisionProvider.CreateImageContentNode(uri);
        }

        return new JsonObject
        {
            ["type"] = "text",
            ["text"] = content.ToString() ?? string.Empty
        };
    }

    private static JsonNode CreateToolNode(AITool tool) =>
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description ?? string.Empty,
                ["parameters"] = TryGetToolParameters(tool) ?? new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                }
            }
        };

    private static JsonNode? TryGetToolParameters(AITool tool)
    {
        var toolType = tool.GetType();
        foreach (var propertyName in new[] { "JsonSchema", "Parameters", "Schema" })
        {
            var property = toolType.GetProperty(propertyName);
            if (property?.GetValue(tool) is not { } value)
                continue;

            if (value is JsonNode node)
                return node.DeepClone();

            if (value is JsonElement element)
                return JsonNode.Parse(element.GetRawText());

            var json = value.ToString();
            if (!string.IsNullOrWhiteSpace(json) && json.TrimStart().StartsWith("{", StringComparison.Ordinal))
                return JsonNode.Parse(json);
        }

        return null;
    }

    private void TrackProviderRequest(
        string capabilityPath,
        string model,
        string result,
        TimeSpan latency,
        AiTokenUsage? usage,
        string? errorCategory)
    {
        if (_analytics is not { IsEnabled: true })
            return;

        var properties = new Dictionary<string, string>
        {
            ["provider.id"] = AiProviderIds.XaiGrok,
            ["capability.path"] = capabilityPath,
            ["endpoint.class"] = "chat.completions",
            ["model.id"] = model,
            ["result"] = result,
            ["fallback.path"] = "none",
            ["latency.ms"] = ((int)latency.TotalMilliseconds).ToString(),
        };

        if (usage?.PromptTokens is not null)
            properties["usage.prompt_tokens"] = usage.PromptTokens.Value.ToString();
        if (usage?.CompletionTokens is not null)
            properties["usage.completion_tokens"] = usage.CompletionTokens.Value.ToString();
        if (usage?.TotalTokens is not null)
            properties["usage.total_tokens"] = usage.TotalTokens.Value.ToString();
        if (errorCategory is not null)
            properties["error.category"] = errorCategory;

        _analytics.TrackEvent("ai.provider.request", properties);
        _analytics.TrackMetric("ai.provider.latency_ms", latency.TotalMilliseconds, properties);
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
