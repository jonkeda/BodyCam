namespace BodyCam.Services;

public class ApiKeyService : IApiKeyService
{
    private const string StorageKey = "openai_api_key";
    private string? _cachedKey;

    public bool HasKey => _cachedKey is not null;

    public async Task<string?> GetApiKeyAsync()
    {
        if (_cachedKey is not null)
            return _cachedKey;

        // 1. Try MAUI SecureStorage (persisted from previous launch)
        _cachedKey = await SecureStorage.Default.GetAsync(StorageKey);
        if (_cachedKey is not null)
            return _cachedKey;

        // 2. Try .env file (dev convenience)
        _cachedKey = DotEnvReader.Read("AZURE_OPENAI_API_KEY")
                  ?? DotEnvReader.Read("OPENAI_API_KEY");
        if (_cachedKey is not null)
        {
            await SecureStorage.Default.SetAsync(StorageKey, _cachedKey);
            return _cachedKey;
        }

        // 3. Try environment variable (CI / dev terminal)
        _cachedKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
                  ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (_cachedKey is not null)
        {
            await SecureStorage.Default.SetAsync(StorageKey, _cachedKey);
            return _cachedKey;
        }

        return null;
    }

    public async Task SetApiKeyAsync(string apiKey)
    {
        await SecureStorage.Default.SetAsync(StorageKey, apiKey);
        _cachedKey = apiKey;
    }

    public Task ClearApiKeyAsync()
    {
        SecureStorage.Default.Remove(StorageKey);
        _cachedKey = null;
        return Task.CompletedTask;
    }
}
