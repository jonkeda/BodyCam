using BodyCam.Services.AiProviders;

namespace BodyCam.Services;

public class ApiKeyService : IApiKeyService
{
    private const string LegacyStorageKey = "openai_api_key";
    private const string XaiStorageKey = "xai_api_key";
    private readonly Dictionary<string, string?> _cachedKeys = new(StringComparer.OrdinalIgnoreCase);

    public bool HasKey => _cachedKeys.Values.Any(key => key is not null);

    public async Task<string?> GetApiKeyAsync()
    {
        if (_cachedKeys.TryGetValue(LegacyStorageKey, out var cachedKey) && cachedKey is not null)
            return cachedKey;

        // 1. Try MAUI SecureStorage (persisted from previous launch)
        var key = await SecureStorage.Default.GetAsync(LegacyStorageKey);
        if (key is not null)
        {
            _cachedKeys[LegacyStorageKey] = key;
            return key;
        }

        // 2. Try .env file (dev convenience)
        key = DotEnvReader.Read("AZURE_OPENAI_API_KEY")
           ?? DotEnvReader.Read("OPENAI_API_KEY");
        if (key is not null)
        {
            await SecureStorage.Default.SetAsync(LegacyStorageKey, key);
            _cachedKeys[LegacyStorageKey] = key;
            return key;
        }

        // 3. Try environment variable (CI / dev terminal)
        key = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
           ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (key is not null)
        {
            await SecureStorage.Default.SetAsync(LegacyStorageKey, key);
            _cachedKeys[LegacyStorageKey] = key;
            return key;
        }

        return null;
    }

    public async Task<string?> GetApiKeyAsync(string providerId)
    {
        var storageKey = GetStorageKey(providerId);
        if (_cachedKeys.TryGetValue(storageKey, out var cachedKey) && cachedKey is not null)
            return cachedKey;

        var key = await SecureStorage.Default.GetAsync(storageKey);
        if (key is not null)
        {
            _cachedKeys[storageKey] = key;
            return key;
        }

        key = ReadFirstDotEnv(GetEnvironmentVariableNames(providerId));
        if (key is not null)
        {
            await SecureStorage.Default.SetAsync(storageKey, key);
            _cachedKeys[storageKey] = key;
            return key;
        }

        key = ReadFirstEnvironment(GetEnvironmentVariableNames(providerId));
        if (key is not null)
        {
            await SecureStorage.Default.SetAsync(storageKey, key);
            _cachedKeys[storageKey] = key;
            return key;
        }

        return null;
    }

    public async Task SetApiKeyAsync(string apiKey)
    {
        await SecureStorage.Default.SetAsync(LegacyStorageKey, apiKey);
        _cachedKeys[LegacyStorageKey] = apiKey;
    }

    public async Task SetApiKeyAsync(string providerId, string apiKey)
    {
        var storageKey = GetStorageKey(providerId);
        await SecureStorage.Default.SetAsync(storageKey, apiKey);
        _cachedKeys[storageKey] = apiKey;
    }

    public Task ClearApiKeyAsync()
    {
        SecureStorage.Default.Remove(LegacyStorageKey);
        _cachedKeys.Remove(LegacyStorageKey);
        return Task.CompletedTask;
    }

    public Task ClearApiKeyAsync(string providerId)
    {
        var storageKey = GetStorageKey(providerId);
        SecureStorage.Default.Remove(storageKey);
        _cachedKeys.Remove(storageKey);
        return Task.CompletedTask;
    }

    private static string GetStorageKey(string providerId) =>
        AiProviderIds.Normalize(providerId) switch
        {
            AiProviderIds.XaiGrok => XaiStorageKey,
            _ => LegacyStorageKey
        };

    private static string[] GetEnvironmentVariableNames(string providerId) =>
        AiProviderIds.Normalize(providerId) switch
        {
            AiProviderIds.XaiGrok => ["XAI_API_KEY"],
            AiProviderIds.AzureOpenAi => ["AZURE_OPENAI_API_KEY", "OPENAI_API_KEY"],
            _ => ["OPENAI_API_KEY"]
        };

    private static string? ReadFirstDotEnv(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            var value = DotEnvReader.Read(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? ReadFirstEnvironment(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
