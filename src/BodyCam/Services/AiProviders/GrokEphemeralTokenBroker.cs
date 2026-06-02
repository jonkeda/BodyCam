using System.Text;
using System.Text.Json;

namespace BodyCam.Services.AiProviders;

public sealed record GrokEphemeralClientSecret(string Value, DateTimeOffset? ExpiresAt);

public interface IGrokEphemeralTokenBroker
{
    Task<GrokEphemeralClientSecret> CreateClientSecretAsync(Uri brokerEndpoint, CancellationToken ct = default);
}

public sealed class GrokEphemeralTokenBroker : IGrokEphemeralTokenBroker
{
    private readonly Func<HttpClient> _httpClientFactory;

    public GrokEphemeralTokenBroker()
        : this(() => new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
    {
    }

    internal GrokEphemeralTokenBroker(Func<HttpClient> httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<GrokEphemeralClientSecret> CreateClientSecretAsync(Uri brokerEndpoint, CancellationToken ct = default)
    {
        using var http = _httpClientFactory();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(brokerEndpoint, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase)
                ? response.StatusCode.ToString()
                : response.ReasonPhrase;
            throw new InvalidOperationException($"Grok ephemeral token broker returned {(int)response.StatusCode}: {reason}");
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var value = ReadClientSecret(doc.RootElement);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Grok ephemeral token broker response did not include a client secret.");

        return new GrokEphemeralClientSecret(value, ReadExpiry(doc.RootElement));
    }

    private static string? ReadClientSecret(JsonElement root) =>
        ReadString(root, "value")
        ?? ReadString(root, "client_secret")
        ?? ReadString(root, "secret")
        ?? ReadNestedString(root, "client_secret", "value");

    private static DateTimeOffset? ReadExpiry(JsonElement root)
    {
        if (ReadString(root, "expires_at") is { } expiresAt
            && DateTimeOffset.TryParse(expiresAt, out var parsedExpiry))
        {
            return parsedExpiry;
        }

        if (root.TryGetProperty("expires_in", out var expiresIn)
            && expiresIn.ValueKind == JsonValueKind.Number
            && expiresIn.TryGetInt32(out var seconds))
        {
            return DateTimeOffset.UtcNow.AddSeconds(seconds);
        }

        if (root.TryGetProperty("expires_after", out var expiresAfter)
            && expiresAfter.ValueKind == JsonValueKind.Object
            && expiresAfter.TryGetProperty("seconds", out var expiresAfterSeconds)
            && expiresAfterSeconds.ValueKind == JsonValueKind.Number
            && expiresAfterSeconds.TryGetInt32(out var nestedSeconds))
        {
            return DateTimeOffset.UtcNow.AddSeconds(nestedSeconds);
        }

        return null;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
            return property.GetString();

        return null;
    }

    private static string? ReadNestedString(JsonElement root, string parent, string name)
    {
        if (root.TryGetProperty(parent, out var parentProperty)
            && parentProperty.ValueKind == JsonValueKind.Object)
        {
            return ReadString(parentProperty, name);
        }

        return null;
    }
}
