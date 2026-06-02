namespace BodyCam.Services;

public interface IApiKeyService
{
    Task<string?> GetApiKeyAsync();
    Task<string?> GetApiKeyAsync(string providerId);
    Task SetApiKeyAsync(string apiKey);
    Task SetApiKeyAsync(string providerId, string apiKey);
    Task ClearApiKeyAsync();
    Task ClearApiKeyAsync(string providerId);
    bool HasKey { get; }
}
