namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// No-op implementation of IHeyCyanHttpClientFactory for platforms that don't support HeyCyan
/// Wi-Fi media transfer (Windows, etc.).
/// </summary>
internal sealed class NullHeyCyanHttpClientFactory : IHeyCyanHttpClientFactory
{
    public Task<IHeyCyanHttpClient> CreateAsync(Uri baseUri, CancellationToken ct)
        => throw new NotSupportedException("HeyCyan media transfer is not supported on this platform.");
}
