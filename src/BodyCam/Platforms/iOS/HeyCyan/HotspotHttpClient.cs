#if IOS
using NetworkExtension;
using Microsoft.Extensions.Logging;

namespace BodyCam.Platforms.iOS.HeyCyan;

/// <summary>
/// iOS-specific HTTP client that wraps NEHotspotConfiguration to join the HeyCyan
/// glasses' Wi-Fi hotspot, discovers the glasses' IP by probing candidate addresses,
/// and exposes HTTP GET operations for media.config and individual files.
/// </summary>
internal sealed class HotspotHttpClient : IAsyncDisposable
{
    private static readonly string[] CandidateIps =
    {
        "192.168.43.1", // Android-style tethering subnet (most common)
        "192.168.4.1",  // ESP32 / SoftAP default
        "192.168.1.1",  // Home-router style
        "192.168.0.1",  // Home-router alt
        "10.0.0.1",     // Carrier-style fallback
    };

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(800);
    private const string FallbackPassword = "123456789";
    private const string MediaConfigPath = "/files/media.config";

    private readonly ILogger<HotspotHttpClient> _log;
    private readonly HttpClient _http;
    private string? _currentSsid;
    private bool _disposed;

    public HotspotHttpClient(ILogger<HotspotHttpClient> log)
    {
        _log = log;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
    }

    /// <summary>
    /// Join the glasses' Wi-Fi hotspot using NEHotspotConfiguration with JoinOnce=true
    /// so the OS automatically removes the profile when the process exits.
    /// </summary>
    public Task JoinAsync(string ssid, string? password, CancellationToken ct)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HotspotHttpClient));

        _currentSsid = ssid;
        var actualPassword = password ?? FallbackPassword;

        _log.LogInformation("HotspotHttpClient: joining SSID {Ssid}", ssid);

        var config = new NEHotspotConfiguration(ssid, actualPassword, isWep: false)
        {
            JoinOnce = true,
        };

        var tcs = new TaskCompletionSource<bool>();
        NEHotspotConfigurationManager.SharedManager.ApplyConfiguration(config, err =>
        {
            if (err is null)
            {
                _log.LogInformation("HotspotHttpClient: joined {Ssid} successfully", ssid);
                tcs.TrySetResult(true);
            }
            else
            {
                _log.LogError("HotspotHttpClient: failed to join {Ssid}: {Error} (code {Code})",
                    ssid, err.LocalizedDescription, err.Code);
                tcs.TrySetException(new IOException(
                    $"NEHotspotConfiguration failed: {err.LocalizedDescription} (code {err.Code})"));
            }
        });

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    /// <summary>
    /// Probe candidate IPs in order until one responds to GET /files/media.config with 200 OK.
    /// Returns the base URL (e.g. "http://192.168.43.1") of the first responsive IP.
    /// </summary>
    public async Task<string> DiscoverGlassesIpAsync(CancellationToken ct)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HotspotHttpClient));

        _log.LogInformation("HotspotHttpClient: discovering glasses IP by probing {Count} candidates", CandidateIps.Length);

        foreach (var ip in CandidateIps)
        {
            ct.ThrowIfCancellationRequested();

            using var probeCts = new CancellationTokenSource(ProbeTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, probeCts.Token);

            try
            {
                var probeUrl = $"http://{ip}{MediaConfigPath}";
                _log.LogDebug("HotspotHttpClient: probing {Url}", probeUrl);

                using var resp = await _http.GetAsync(probeUrl, linkedCts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    _log.LogInformation("HotspotHttpClient: discovered glasses at {Ip}", ip);
                    return $"http://{ip}";
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogDebug("HotspotHttpClient: probe {Ip} failed: {Message}", ip, ex.Message);
                // Probe failed (timeout, refused, no route) — try next candidate.
            }
        }

        throw new IOException(
            $"No HeyCyan glasses found at any of: {string.Join(", ", CandidateIps)}");
    }

    /// <summary>
    /// Fetch /files/media.config and delegate parsing to MediaConfigParser.
    /// Returns a list of filenames (IMG_*.jpg, VID_*.mp4, REC_*.opus).
    /// </summary>
    public async Task<IReadOnlyList<string>> GetMediaConfigAsync(string baseUrl, CancellationToken ct)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HotspotHttpClient));

        var url = $"{baseUrl}{MediaConfigPath}";
        _log.LogInformation("HotspotHttpClient: fetching media.config from {Url}", url);

        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        
        // Parse using the shared MediaConfigParser
        var entries = Services.Glasses.HeyCyan.MediaConfigParser.Parse(text);
        return entries.Select(e => e.Name).ToArray();
    }

    /// <summary>
    /// Stream a media file directly to the destination stream without buffering in memory.
    /// Uses HttpCompletionOption.ResponseHeadersRead to start streaming immediately.
    /// </summary>
    public async Task DownloadFileAsync(string baseUrl, string fileName, Stream destination, CancellationToken ct)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HotspotHttpClient));

        var url = $"{baseUrl}/files/{Uri.EscapeDataString(fileName)}";
        _log.LogInformation("HotspotHttpClient: downloading {FileName} from {Url}", fileName, url);

        using var resp = await _http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await src.CopyToAsync(destination, 64 * 1024, ct).ConfigureAwait(false);

        _log.LogInformation("HotspotHttpClient: downloaded {FileName} successfully", fileName);
    }

    /// <summary>
    /// Tear down the hotspot configuration and restore the user's previous network.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return default;

        _disposed = true;

        if (_currentSsid is not null)
        {
            _log.LogInformation("HotspotHttpClient: removing hotspot configuration for {Ssid}", _currentSsid);
            NEHotspotConfigurationManager.SharedManager.RemoveConfiguration(_currentSsid);
            _currentSsid = null;
        }

        _http.Dispose();
        return default;
    }
}
#endif
