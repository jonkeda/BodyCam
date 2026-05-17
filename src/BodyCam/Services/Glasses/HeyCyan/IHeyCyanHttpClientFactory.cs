namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Factory for creating platform-specific HTTP clients bound to the glasses P2P network.
/// Android: creates WiFiP2pHttpClient with process network binding.
/// iOS: creates HotspotHttpClient with NEHotspotConfiguration.
/// </summary>
public interface IHeyCyanHttpClientFactory
{
    Task<IHeyCyanHttpClient> CreateAsync(Uri baseUri, CancellationToken ct);
}
