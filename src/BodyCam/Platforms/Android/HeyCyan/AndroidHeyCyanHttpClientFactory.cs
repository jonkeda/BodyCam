#if ANDROID
using BodyCam.Services.Glasses.HeyCyan;
using Microsoft.Extensions.Logging;

namespace BodyCam.Platforms.Android.HeyCyan;

/// <summary>
/// Android implementation of IHeyCyanHttpClientFactory.
/// Wraps WiFiP2pHttpClient (which binds the process to the P2P network) behind
/// the cross-platform IHeyCyanHttpClient interface.
/// </summary>
internal sealed class AndroidHeyCyanHttpClientFactory : IHeyCyanHttpClientFactory
{
    private readonly ILoggerFactory _logFactory;

    public AndroidHeyCyanHttpClientFactory(ILoggerFactory logFactory)
    {
        _logFactory = logFactory;
    }

    public async Task<IHeyCyanHttpClient> CreateAsync(Uri baseUri, CancellationToken ct)
    {
        // Extract the glasses IP from the base URI (e.g. http://192.168.49.x/)
        var host = baseUri.Host;
        
        var p2pClient = new WiFiP2pHttpClient(_logFactory.CreateLogger<WiFiP2pHttpClient>());
        await p2pClient.ConnectAsync(host, ct).ConfigureAwait(false);
        
        return new AndroidHeyCyanHttpClient(p2pClient);
    }

    /// <summary>
    /// Adapter that wraps WiFiP2pHttpClient as IHeyCyanHttpClient.
    /// </summary>
    private sealed class AndroidHeyCyanHttpClient : IHeyCyanHttpClient
    {
        private readonly WiFiP2pHttpClient _inner;

        public Uri BaseUri => _inner.BaseUri ?? throw new InvalidOperationException("Not connected");

        public AndroidHeyCyanHttpClient(WiFiP2pHttpClient inner)
        {
            _inner = inner;
        }

        public Task<string> GetStringAsync(string path, CancellationToken ct)
            => _inner.GetStringAsync(path, ct);

        public Task<byte[]> GetByteArrayAsync(string path, CancellationToken ct)
            => _inner.GetByteArrayAsync(path, ct);

        public Task<Stream> GetStreamAsync(string path, CancellationToken ct)
            => _inner.GetStreamAsync(path, ct);

        public ValueTask DisposeAsync()
            => _inner.DisposeAsync();
    }
}
#endif
