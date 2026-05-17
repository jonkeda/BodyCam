namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Platform-abstracted HTTP client for downloading media from HeyCyan glasses.
/// Android implementation wraps WiFiP2pHttpClient (process-bound to P2P network).
/// iOS implementation wraps HotspotHttpClient (NEHotspotConfiguration).
/// </summary>
public interface IHeyCyanHttpClient : IAsyncDisposable
{
    Uri BaseUri { get; }
    Task<string> GetStringAsync(string path, CancellationToken ct);
    Task<byte[]> GetByteArrayAsync(string path, CancellationToken ct);
    Task<Stream> GetStreamAsync(string path, CancellationToken ct);
}
