using BodyCam.Services.Glasses.HeyCyan;
using Microsoft.Extensions.Logging;

namespace BodyCam.Platforms.Windows.HeyCyan;

/// <summary>
/// Windows implementation of <see cref="IHeyCyanHttpClientFactory"/>.
/// On Windows, no special network binding is needed (unlike Android which must
/// <c>BindProcessToNetwork</c> to the P2P group). Standard <see cref="HttpClient"/>
/// routes to the glasses IP automatically once the WiFi hotspot is joined.
/// </summary>
internal sealed class WindowsHeyCyanHttpClientFactory : IHeyCyanHttpClientFactory
{
    private readonly ILogger<WindowsHeyCyanHttpClientFactory> _log;

    public WindowsHeyCyanHttpClientFactory(ILogger<WindowsHeyCyanHttpClientFactory> log)
    {
        _log = log;
    }

    public Task<IHeyCyanHttpClient> CreateAsync(Uri baseUri, CancellationToken ct)
    {
        _log.LogInformation("Creating Windows HTTP client for {BaseUri}", baseUri);

        var handler = new HttpClientHandler
        {
            // Glasses serve cleartext HTTP only — no TLS
            ServerCertificateCustomValidationCallback = null,
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(30),
        };

        IHeyCyanHttpClient client = new WindowsHeyCyanHttpClient(baseUri, httpClient);
        return Task.FromResult(client);
    }

    private sealed class WindowsHeyCyanHttpClient : IHeyCyanHttpClient
    {
        private readonly HttpClient _http;

        public Uri BaseUri { get; }

        public WindowsHeyCyanHttpClient(Uri baseUri, HttpClient http)
        {
            BaseUri = baseUri;
            _http = http;
        }

        public Task<string> GetStringAsync(string path, CancellationToken ct)
            => _http.GetStringAsync(new Uri(path, UriKind.Relative), ct);

        public Task<byte[]> GetByteArrayAsync(string path, CancellationToken ct)
            => _http.GetByteArrayAsync(new Uri(path, UriKind.Relative), ct);

        public Task<Stream> GetStreamAsync(string path, CancellationToken ct)
            => _http.GetStreamAsync(new Uri(path, UriKind.Relative), ct);

        public ValueTask DisposeAsync()
        {
            _http.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
