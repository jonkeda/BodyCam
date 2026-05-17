#if IOS
using BodyCam.Services.Glasses.HeyCyan;
using Microsoft.Extensions.Logging;

namespace BodyCam.Platforms.iOS.HeyCyan;

/// <summary>
/// iOS implementation of IHeyCyanHttpClientFactory.
/// Wraps HotspotHttpClient (which uses NEHotspotConfiguration to join the glasses' hotspot)
/// behind the cross-platform IHeyCyanHttpClient interface.
/// </summary>
internal sealed class IosHeyCyanHttpClientFactory : IHeyCyanHttpClientFactory
{
    private readonly ILoggerFactory _logFactory;

    public IosHeyCyanHttpClientFactory(ILoggerFactory logFactory)
    {
        _logFactory = logFactory;
    }

    public async Task<IHeyCyanHttpClient> CreateAsync(Uri baseUri, CancellationToken ct)
    {
        // baseUri is the discovered glasses IP (e.g. http://192.168.43.1/)
        // We don't need to join the hotspot here — the session already did that.
        // Just wrap a configured HotspotHttpClient as the IHeyCyanHttpClient adapter.
        var hotspot = new HotspotHttpClient(_logFactory.CreateLogger<HotspotHttpClient>());
        return new IosHeyCyanHttpClient(baseUri, hotspot);
    }

    /// <summary>
    /// Adapter that wraps HotspotHttpClient as IHeyCyanHttpClient.
    /// </summary>
    private sealed class IosHeyCyanHttpClient : IHeyCyanHttpClient
    {
        private readonly Uri _baseUri;
        private readonly HotspotHttpClient _inner;

        public Uri BaseUri => _baseUri;

        public IosHeyCyanHttpClient(Uri baseUri, HotspotHttpClient inner)
        {
            _baseUri = baseUri;
            _inner = inner;
        }

        public async Task<string> GetStringAsync(string path, CancellationToken ct)
        {
            // For string content, we can use HttpClient directly via DownloadFileAsync to memory
            using var ms = new MemoryStream();
            await _inner.DownloadFileAsync(_baseUri.ToString().TrimEnd('/'), path.TrimStart('/'), ms, ct);
            ms.Position = 0;
            using var reader = new StreamReader(ms);
            return await reader.ReadToEndAsync(ct);
        }

        public async Task<byte[]> GetByteArrayAsync(string path, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            await _inner.DownloadFileAsync(_baseUri.ToString().TrimEnd('/'), path.TrimStart('/'), ms, ct);
            return ms.ToArray();
        }

        public async Task<Stream> GetStreamAsync(string path, CancellationToken ct)
        {
            // For streaming, we return a MemoryStream that we populate via DownloadFileAsync.
            // This isn't truly streaming from the network, but it matches the interface contract.
            // A better implementation would expose the HttpResponseMessage stream directly,
            // but HotspotHttpClient's current design requires a destination stream.
            var ms = new MemoryStream();
            await _inner.DownloadFileAsync(_baseUri.ToString().TrimEnd('/'), path.TrimStart('/'), ms, ct);
            ms.Position = 0;
            return ms;
        }

        public ValueTask DisposeAsync()
            => _inner.DisposeAsync();
    }
}
#endif
