#if ANDROID
using Android.Content;
using Android.Net;
using Android.Net.Wifi.P2p;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using System.Net;

namespace BodyCam.Platforms.Android.HeyCyan;

/// <summary>
/// Android-only helper that manages the Wi-Fi P2P group lifecycle, binds the app
/// process to the P2P network, and exposes a process-bound HttpClient rooted at
/// the BLE-reported glasses IP. This is the lowest layer of the file-based camera
/// pipeline — every byte we read off the glasses flows through this client.
/// 
/// Critical: Uses the IP from BLE notify (LoadData[6] == 0x08), NOT
/// WifiP2pInfo.groupOwnerAddress (which resolves to 192.168.49.1 = the phone).
/// </summary>
internal sealed class WiFiP2pHttpClient : IAsyncDisposable
{
    private readonly WifiP2pManager _manager;
    private readonly WifiP2pManager.Channel _channel;
    private readonly ConnectivityManager _connectivity;
    private readonly ILogger<WiFiP2pHttpClient> _log;

    private Network? _p2pNetwork;
    private HttpClient? _http;
    private bool _disposed;

    public string? GlassesIp { get; private set; }
    public System.Uri? BaseUri => GlassesIp is null ? null : new System.Uri($"http://{GlassesIp}/");

    public WiFiP2pHttpClient(ILogger<WiFiP2pHttpClient> log)
    {
        _log = log;

        var context = Platform.CurrentActivity ?? Platform.AppContext;
        if (context is null)
            throw new InvalidOperationException("Cannot get Android context — MAUI Platform not initialized");

        _manager = (WifiP2pManager?)context.GetSystemService(Context.WifiP2pService)
            ?? throw new InvalidOperationException("WifiP2pManager not available");
        _channel = _manager.Initialize(context, context.MainLooper, null);

        _connectivity = (ConnectivityManager?)context.GetSystemService(Context.ConnectivityService)
            ?? throw new InvalidOperationException("ConnectivityManager not available");
    }

    /// <summary>
    /// Wait for the P2P group to form, resolve the P2P network handle, bind the process,
    /// and construct the HttpClient. The glassesIp comes from the BLE-reported
    /// GlassesDeviceNotifyRsp frame (LoadData[6] == 0x08, octets LoadData[7..10]).
    /// </summary>
    public async Task ConnectAsync(string glassesIp, CancellationToken ct)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WiFiP2pHttpClient));

        GlassesIp = glassesIp;
        _log.LogInformation("WiFiP2pHttpClient: connecting to glasses IP {Ip}", glassesIp);

        // Wait for P2P group formation.
        var groupFormed = await WaitForGroupFormationAsync(ct);
        if (!groupFormed)
            throw new InvalidOperationException("P2P group not formed within timeout");

        // Resolve the P2P Network handle from ConnectivityManager.
        _p2pNetwork = await ResolveP2pNetworkAsync(glassesIp, ct);
        if (_p2pNetwork is null)
            throw new InvalidOperationException("Failed to resolve P2P network handle");

        // Bind the process to the P2P network so HttpClient routes over P2P.
        // Without this, requests on Samsung route over cellular and time out.
        var bindResult = _connectivity.BindProcessToNetwork(_p2pNetwork);
        if (!bindResult)
            _log.LogWarning("BindProcessToNetwork returned false — HTTP routing may fail on multi-network devices");

        _log.LogInformation("WiFiP2pHttpClient: bound process to P2P network, constructing HttpClient");

        // Construct HttpClient with timeout and base address.
        _http = new HttpClient
        {
            BaseAddress = BaseUri,
            Timeout = TimeSpan.FromSeconds(20),
        };
    }

    public Task<string> GetStringAsync(string path, CancellationToken ct)
    {
        if (_http is null)
            throw new InvalidOperationException("Not connected — call ConnectAsync first");
        return _http.GetStringAsync(path, ct);
    }

    public Task<byte[]> GetByteArrayAsync(string path, CancellationToken ct)
    {
        if (_http is null)
            throw new InvalidOperationException("Not connected — call ConnectAsync first");
        return _http.GetByteArrayAsync(path, ct);
    }

    public async Task<Stream> GetStreamAsync(string path, CancellationToken ct)
    {
        if (_http is null)
            throw new InvalidOperationException("Not connected — call ConnectAsync first");
        var response = await _http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _http?.Dispose();
        _http = null;

        // Restore default routing (cellular/regular Wi-Fi).
        // Note: Do NOT tear down the P2P group here — that's the responsibility of
        // the BLE exit command (LargeDataHandler.GlassesControl(0x02, 0x01, 0x09)).
        _connectivity.BindProcessToNetwork(null);

        _log.LogInformation("WiFiP2pHttpClient: disposed, restored default routing");
    }

    /// <summary>
    /// Wait for WifiP2pManager.requestConnectionInfo to report groupFormed == true.
    /// </summary>
    private async Task<bool> WaitForGroupFormationAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));

        var listener = new ConnectionInfoListener(info =>
        {
            if (info?.GroupFormed == true)
            {
                _log.LogInformation("WiFiP2pHttpClient: P2P group formed");
                tcs.TrySetResult(true);
            }
        });

        _manager.RequestConnectionInfo(_channel, listener);

        // Timeout after 12s if group doesn't form.
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(12), ct);
        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            _log.LogError("WiFiP2pHttpClient: P2P group formation timeout");
            return false;
        }

        return await tcs.Task;
    }

    /// <summary>
    /// Resolve the P2P Network handle from ConnectivityManager by enumerating AllNetworks
    /// and matching NetworkCapabilities.HasTransport(TransportType.Wifi) plus a
    /// LinkProperties route on 192.168.49.0/24.
    /// </summary>
    private async Task<Network?> ResolveP2pNetworkAsync(string glassesIp, CancellationToken ct)
    {
        // Give the system a moment to register the P2P network.
        await Task.Delay(500, ct);

        var allNetworks = _connectivity.GetAllNetworks();
        if (allNetworks is null || allNetworks.Length == 0)
        {
            _log.LogWarning("WiFiP2pHttpClient: no networks reported by ConnectivityManager");
            return null;
        }

        foreach (var network in allNetworks)
        {
            var caps = _connectivity.GetNetworkCapabilities(network);
            if (caps?.HasTransport(TransportType.Wifi) != true)
                continue;

            var linkProps = _connectivity.GetLinkProperties(network);
            if (linkProps is null)
                continue;

            // Check if this network has a route to the glasses IP.
            // The P2P subnet is typically 192.168.49.0/24.
            var routes = linkProps.Routes;
            if (routes is null)
                continue;

            foreach (var route in routes)
            {
                var dest = route?.Destination?.ToString();
                if (dest is not null && (dest.StartsWith("192.168.49") || dest == "0.0.0.0/0"))
                {
                    _log.LogInformation("WiFiP2pHttpClient: resolved P2P network with route {Route}", dest);
                    return network;
                }
            }
        }

        _log.LogWarning("WiFiP2pHttpClient: no P2P network found with 192.168.49.x route");
        return null;
    }

    /// <summary>
    /// Simple WifiP2pManager.ConnectionInfoListener that invokes a callback when
    /// connection info is received.
    /// </summary>
    private sealed class ConnectionInfoListener : Java.Lang.Object, WifiP2pManager.IConnectionInfoListener
    {
        private readonly Action<WifiP2pInfo?> _callback;

        public ConnectionInfoListener(Action<WifiP2pInfo?> callback)
        {
            _callback = callback;
        }

        public void OnConnectionInfoAvailable(WifiP2pInfo? info)
        {
            _callback(info);
        }
    }
}
#endif
