namespace BodyCam.Services;

public interface IApiKeyService
{
    Task<string?> GetApiKeyAsync();
    Task SetApiKeyAsync(string apiKey);
    Task ClearApiKeyAsync();
    bool HasKey { get; }
}
