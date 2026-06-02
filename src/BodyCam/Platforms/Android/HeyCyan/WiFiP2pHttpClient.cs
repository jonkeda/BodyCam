#if ANDROID
using Android.Content;
using Android.Net;
using Android.Net.Wifi;
using Android.Net.Wifi.P2p;
using Android.Util;
using BodyCam.Services.Glasses.HeyCyan;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using System.Text;

namespace BodyCam.Platforms.Android.HeyCyan;

/// <summary>
/// Android-only helper that manages the Wi-Fi P2P group lifecycle, binds the app
/// process to the P2P network, and opens HTTP requests on the resolved P2P Network at
/// the BLE-reported glasses IP. This is the lowest layer of the file-based camera
/// pipeline — every byte we read off the glasses flows through this client.
/// 
/// Critical: Uses the IP from BLE notify (LoadData[6] == 0x08), NOT
/// WifiP2pInfo.groupOwnerAddress (which resolves to 192.168.49.1 = the phone).
/// </summary>
internal sealed class WiFiP2pHttpClient : IAsyncDisposable
{
    private const string LogTag = "BodyCamHeyCyanP2p";

    private readonly Context _context;
    private readonly WifiP2pManager _manager;
    private readonly WifiP2pManager.Channel _channel;
    private readonly ConnectivityManager _connectivity;
    private readonly ILogger<WiFiP2pHttpClient> _log;

    private Network? _p2pNetwork;
    private bool _disposed;

    public string? GlassesIp { get; private set; }
    public System.Uri? BaseUri => GlassesIp is null ? null : new System.Uri($"http://{GlassesIp}/");

    public WiFiP2pHttpClient(ILogger<WiFiP2pHttpClient> log)
    {
        _log = log;

        _context = Platform.CurrentActivity ?? Platform.AppContext;
        if (_context is null)
            throw new InvalidOperationException("Cannot get Android context — MAUI Platform not initialized");

        _manager = (WifiP2pManager?)_context.GetSystemService(Context.WifiP2pService)
            ?? throw new InvalidOperationException("WifiP2pManager not available");
        _channel = _manager.Initialize(_context, _context.MainLooper, null)
            ?? throw new InvalidOperationException("WifiP2pManager channel initialization failed");

        _connectivity = (ConnectivityManager?)_context.GetSystemService(Context.ConnectivityService)
            ?? throw new InvalidOperationException("ConnectivityManager not available");
    }

    public async Task PrepareForTransferAsync(CancellationToken ct)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WiFiP2pHttpClient));

        _log.LogInformation("WiFiP2pHttpClient: preparing Wi-Fi Direct discovery before BLE transfer command");
        Log.Info(LogTag, "Preparing Wi-Fi Direct discovery before BLE transfer command");

        if (await IsGroupFormedAsync(ct).ConfigureAwait(false))
        {
            _log.LogInformation("WiFiP2pHttpClient: removing existing P2P group before HeyCyan transfer");
            Log.Info(LogTag, "Removing existing P2P group before HeyCyan transfer");
            await TryWifiActionAsync(
                "removeGroup",
                listener => _manager.RemoveGroup(_channel, listener),
                ct).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }

        await StartPeerDiscoveryAsync(ct).ConfigureAwait(false);
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

        _log.LogInformation("WiFiP2pHttpClient: connecting to glasses IP {Ip}", glassesIp);
        Log.Info(LogTag, $"Connecting to glasses IP {glassesIp}");

        // Start/confirm Wi-Fi Direct connection to the glasses.
        var groupFormed = await EnsureP2pGroupAsync(ct);
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

        _log.LogInformation("WiFiP2pHttpClient: bound process to P2P network");
        Log.Info(LogTag, $"Bound process to P2P network: {bindResult}");

        // The official app waits briefly after both the P2P connection and BLE IP
        // signal are present before it asks for media.config. Samsung can report
        // the 192.168.49.x route before sockets are actually usable, so give the
        // bound Network a little breathing room.
        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);

        GlassesIp = await ResolveMediaHostAsync(glassesIp, ct).ConfigureAwait(false);
        _log.LogInformation("WiFiP2pHttpClient: resolved glasses media host {Ip}", GlassesIp);
        Log.Info(LogTag, $"Resolved glasses media host {GlassesIp}");

    }

    public async Task<string> GetStringAsync(string path, CancellationToken ct)
    {
        if (GlassesIp is null)
            throw new InvalidOperationException("Not connected — call ConnectAsync first");

        var bytes = await GetBytesOnNetworkAsync(
            BuildRequestUri(GlassesIp, path),
            TimeSpan.FromSeconds(20),
            ct).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    public Task<byte[]> GetByteArrayAsync(string path, CancellationToken ct)
    {
        if (GlassesIp is null)
            throw new InvalidOperationException("Not connected — call ConnectAsync first");

        return GetBytesOnNetworkAsync(
            BuildRequestUri(GlassesIp, path),
            TimeSpan.FromSeconds(60),
            ct);
    }

    public async Task<Stream> GetStreamAsync(string path, CancellationToken ct)
    {
        var bytes = await GetByteArrayAsync(path, ct).ConfigureAwait(false);
        return new MemoryStream(bytes, writable: false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Restore default routing (cellular/regular Wi-Fi).
        // Note: Do NOT tear down the P2P group here — that's the responsibility of
        // the BLE exit command (LargeDataHandler.GlassesControl(0x02, 0x01, 0x09)).
        _connectivity.BindProcessToNetwork(null);

        _log.LogInformation("WiFiP2pHttpClient: disposed, restored default routing");
    }

    /// <summary>
    /// Validate the requested host against the real media endpoint. In the M46
    /// Android captures the phone is the P2P group owner at 192.168.49.1, while
    /// the glasses are a client such as 192.168.49.183.
    /// </summary>
    private async Task<string> ResolveMediaHostAsync(string requestedHost, CancellationToken ct)
    {
        var candidates = BuildP2pHostCandidates(requestedHost);
        var start = DateTimeOffset.UtcNow;
        var attempt = 0;

        while (DateTimeOffset.UtcNow - start < TimeSpan.FromSeconds(45))
        {
            ct.ThrowIfCancellationRequested();
            attempt++;

            foreach (var candidate in candidates)
            {
                var ok = await IsMediaHostAsync(candidate, timeout: TimeSpan.FromSeconds(2), ct)
                    .ConfigureAwait(false);
                if (ok)
                    return candidate;
            }

            _log.LogInformation(
                "WiFiP2pHttpClient: media.config not ready yet for {Host}, attempt {Attempt}, candidates [{Candidates}]",
                requestedHost,
                attempt,
                string.Join(",", candidates));
            Log.Info(LogTag, $"media.config not ready for {requestedHost}, attempt {attempt}, candidates=[{string.Join(",", candidates)}]");

            await Task.Delay(TimeSpan.FromSeconds(1.5), ct).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"P2P group formed, but no glasses media server answered /files/media.config. Requested host was {requestedHost}; candidates were {string.Join(", ", candidates)}.");
    }

    private async Task<bool> IsMediaHostAsync(string host, TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var url = BuildRequestUri(host, "/files/media.config");
            var bytes = await GetBytesOnNetworkAsync(url, timeout, timeoutCts.Token).ConfigureAwait(false);
            var raw = Encoding.UTF8.GetString(bytes);
            if (!LooksLikeMediaConfig(raw))
            {
                _log.LogDebug("WiFiP2pHttpClient: {Host} returned non-media.config content", host);
                return false;
            }

            _log.LogInformation("WiFiP2pHttpClient: media.config probe succeeded at {Host}", host);
            Log.Info(LogTag, $"media.config probe succeeded at {host}");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _log.LogDebug(ex, "WiFiP2pHttpClient: media.config probe failed for {Host}", host);
            if (IsPriorityHost(host))
                Log.Info(LogTag, $"media.config probe failed for {host}: {ex.GetType().Name}: {ex.Message}");

            if (IsNetworkNotReadyException(ex))
            {
                _log.LogInformation(
                    "WiFiP2pHttpClient: P2P network not ready while probing {Host}; refreshing binding",
                    host);
                Log.Warn(LogTag, $"P2P network not ready for {host}; refreshing binding");
                await RefreshP2pNetworkBindingAsync(host, ct).ConfigureAwait(false);
            }

            return false;
        }
    }

    private async Task<byte[]> GetBytesOnNetworkAsync(System.Uri uri, TimeSpan timeout, CancellationToken ct)
    {
        if (_p2pNetwork is null)
            throw new InvalidOperationException("P2P network is not resolved");

        try
        {
            return await Task.Run(
                () => GetBytesWithJavaConnection(uri, timeout, ct, useResolvedNetwork: true),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNetworkNotReadyException(ex))
        {
            _log.LogInformation(
                ex,
                "WiFiP2pHttpClient: Network.OpenConnection failed for {Uri}; retrying through process-bound default routing",
                uri);
            Log.Warn(LogTag, $"Network.OpenConnection failed with ENONET for {uri}; retrying process-bound URL connection");

            var bindResult = _connectivity.BindProcessToNetwork(_p2pNetwork);
            Log.Info(LogTag, $"Re-bound process to P2P network before fallback URL connection: {bindResult}");

            return await Task.Run(
                () => GetBytesWithJavaConnection(uri, timeout, ct, useResolvedNetwork: false),
                ct).ConfigureAwait(false);
        }
    }

    private byte[] GetBytesWithJavaConnection(
        System.Uri uri,
        TimeSpan timeout,
        CancellationToken ct,
        bool useResolvedNetwork)
    {
        if (_p2pNetwork is null)
            throw new InvalidOperationException("P2P network is not resolved");

        ct.ThrowIfCancellationRequested();

        Java.Net.HttpURLConnection? http = null;
        Stream? input = null;

        try
        {
            var url = new Java.Net.URL(uri.AbsoluteUri);
            var connection = useResolvedNetwork
                ? _p2pNetwork.OpenConnection(url)
                : url.OpenConnection();
            if (connection is null)
                throw new InvalidOperationException($"Failed to open connection for {uri}");

            if (connection is not Java.Net.HttpURLConnection httpConnection)
            {
                connection.Dispose();
                throw new InvalidOperationException($"Expected HTTP connection for {uri}");
            }

            http = httpConnection;

            var timeoutMs = checked((int)timeout.TotalMilliseconds);
            http.RequestMethod = "GET";
            http.ConnectTimeout = timeoutMs;
            http.ReadTimeout = timeoutMs;
            http.DoInput = true;
            http.UseCaches = false;
            http.SetRequestProperty("Accept", "*/*");
            http.SetRequestProperty("Connection", "close");
            http.SetRequestProperty("User-Agent", "GlassesApp/1.0");

            var status = (int)http.ResponseCode;
            input = status >= 400 ? http.ErrorStream : http.InputStream;

            var bytes = ReadAllBytes(input, ct);
            if (status is < 200 or >= 300)
                throw new HttpRequestException($"HTTP {status} from {uri}");

            return bytes;
        }
        finally
        {
            try
            {
                input?.Dispose();
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "WiFiP2pHttpClient: failed to close Java input stream");
            }

            try
            {
                http?.Disconnect();
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "WiFiP2pHttpClient: failed to disconnect HTTP connection");
            }

            try
            {
                http?.Dispose();
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "WiFiP2pHttpClient: failed to dispose HTTP connection");
            }
        }
    }

    private static byte[] ReadAllBytes(Stream? input, CancellationToken ct)
    {
        if (input is null)
            return [];

        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = input.Read(buffer, 0, buffer.Length);
            if (read <= 0)
                break;

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static System.Uri BuildRequestUri(string host, string path)
    {
        var normalized = path.StartsWith("/", StringComparison.Ordinal) ? path[1..] : path;
        return new System.Uri(new System.Uri($"http://{host}/"), normalized);
    }

    private static bool IsPriorityHost(string host)
        => string.Equals(host, "192.168.49.183", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "192.168.49.200", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeMediaConfig(string raw)
    {
        if (raw.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var entries = MediaConfigParser.Parse(raw);
        return entries.Count > 0
            && entries.All(e => e.Kind is HeyCyanMediaKind.Photo or HeyCyanMediaKind.Video or HeyCyanMediaKind.Audio);
    }

    private async Task RefreshP2pNetworkBindingAsync(string host, CancellationToken ct)
    {
        var network = await ResolveP2pNetworkAsync(host, ct).ConfigureAwait(false);
        if (network is null)
        {
            Log.Warn(LogTag, $"Could not refresh P2P network binding for {host}: no matching network");
            return;
        }

        _connectivity.BindProcessToNetwork(null);
        await Task.Delay(TimeSpan.FromMilliseconds(150), ct).ConfigureAwait(false);

        _p2pNetwork = network;
        var bindResult = _connectivity.BindProcessToNetwork(_p2pNetwork);
        Log.Info(LogTag, $"Refreshed P2P network binding for {host}: {bindResult}");
        await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
    }

    private static bool IsNetworkNotReadyException(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("ENONET", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Machine is not on the network", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> BuildP2pHostCandidates(string requestedHost)
    {
        var candidates = new List<string>();
        AddCandidate(requestedHost);

        // Observed during M46 official-app captures. Keep this list short and
        // sequential; the glasses media server is small and does not like bursts.
        AddCandidate("192.168.49.183");
        AddCandidate("192.168.49.200");

        return candidates;

        void AddCandidate(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return;
            if (candidates.Contains(host, StringComparer.OrdinalIgnoreCase))
                return;
            candidates.Add(host);
        }
    }

    private async Task<bool> EnsureP2pGroupAsync(CancellationToken ct)
    {
        if (await IsGroupFormedAsync(ct).ConfigureAwait(false))
            return true;

        await StartPeerDiscoveryAsync(ct).ConfigureAwait(false);

        var attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var start = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - start < TimeSpan.FromSeconds(45))
        {
            ct.ThrowIfCancellationRequested();

            if (await IsGroupFormedAsync(ct).ConfigureAwait(false))
                return true;

            var peers = await RequestPeersAsync(ct).ConfigureAwait(false);
            if (peers.Count > 0)
            {
                _log.LogInformation(
                    "WiFiP2pHttpClient: found {Count} P2P peer(s): {Peers}",
                    peers.Count,
                    string.Join(", ", peers.Select(DescribePeer)));
            }

            var target = SelectLikelyGlassesPeer(peers);
            var address = target?.DeviceAddress;
            if (!string.IsNullOrWhiteSpace(address) && attempted.Add(address))
            {
                _log.LogInformation("WiFiP2pHttpClient: connecting to P2P peer {Peer}", DescribePeer(target!));
                Log.Info(LogTag, $"Connecting to P2P peer {DescribePeer(target!)}");
                await TryWifiActionAsync(
                    "connect",
                    listener => _manager.Connect(_channel, BuildPeerConfig(target!), listener),
                    ct).ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromSeconds(1.5), ct).ConfigureAwait(false);
        }

        return await IsGroupFormedAsync(ct).ConfigureAwait(false);
    }

    private async Task StartPeerDiscoveryAsync(CancellationToken ct)
    {
        _log.LogInformation("WiFiP2pHttpClient: starting peer discovery");
        Log.Info(LogTag, "Starting Wi-Fi Direct peer discovery");
        await TryWifiActionAsync(
            "discoverPeers",
            listener => _manager.DiscoverPeers(_channel, listener),
            ct).ConfigureAwait(false);
    }

    private async Task<bool> IsGroupFormedAsync(CancellationToken ct)
    {
        try
        {
            var info = await RequestConnectionInfoAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            if (info?.GroupFormed == true)
            {
                _log.LogInformation(
                    "WiFiP2pHttpClient: P2P group formed, isGroupOwner={IsGroupOwner}, groupOwner={GroupOwner}",
                    info.IsGroupOwner,
                    info.GroupOwnerAddress?.HostAddress ?? "unknown");
                Log.Info(LogTag, $"P2P group formed, isGroupOwner={info.IsGroupOwner}, groupOwner={info.GroupOwnerAddress?.HostAddress ?? "unknown"}");
                return true;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "WiFiP2pHttpClient: requestConnectionInfo failed");
        }

        return false;
    }

    private async Task<WifiP2pInfo?> RequestConnectionInfoAsync(TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<WifiP2pInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        using var reg = timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token));

        var listener = new ConnectionInfoListener(info =>
        {
            tcs.TrySetResult(info);
        });

        _manager.RequestConnectionInfo(_channel, listener);
        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<WifiP2pDevice>> RequestPeersAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<WifiP2pDevice>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
        using var reg = timeoutCts.Token.Register(() => tcs.TrySetResult([]));

        _manager.RequestPeers(_channel, new PeerListListener(peers =>
        {
            var devices = peers?.DeviceList?.Where(p => p is not null).ToArray() ?? [];
            tcs.TrySetResult(devices!);
        }));

        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task TryWifiActionAsync(
        string actionName,
        Action<WifiP2pManager.IActionListener> invoke,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<WifiP2pFailureReason?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
        using var reg = timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token));

        invoke(new ActionListener(
            () => tcs.TrySetResult(null),
            reason => tcs.TrySetResult(reason)));

        try
        {
            var failure = await tcs.Task.ConfigureAwait(false);
            if (failure is null)
            {
                _log.LogInformation("WiFiP2pHttpClient: {Action} succeeded", actionName);
                Log.Info(LogTag, $"{actionName} succeeded");
            }
            else
            {
                _log.LogWarning("WiFiP2pHttpClient: {Action} failed: {Reason}", actionName, failure);
                Log.Warn(LogTag, $"{actionName} failed: {failure}");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("WiFiP2pHttpClient: {Action} callback timed out", actionName);
            Log.Warn(LogTag, $"{actionName} callback timed out");
        }
    }

    private static WifiP2pConfig BuildPeerConfig(WifiP2pDevice peer)
    {
        return new WifiP2pConfig
        {
            DeviceAddress = peer.DeviceAddress,
            GroupOwnerIntent = 0,
            Wps = new WpsInfo { Setup = WpsInfo.Pbc },
        };
    }

    private static WifiP2pDevice? SelectLikelyGlassesPeer(IReadOnlyList<WifiP2pDevice> peers)
    {
        return peers.FirstOrDefault(p => IsLikelyGlassesPeerName(p.DeviceName));
    }

    private static bool IsLikelyGlassesPeerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.StartsWith("M01", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("QC", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("O_", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Cyan", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribePeer(WifiP2pDevice peer)
        => $"{peer.DeviceName ?? "unknown"}/{peer.DeviceAddress ?? "unknown"}";

    /// <summary>
    /// Resolve the P2P Network handle from ConnectivityManager by enumerating AllNetworks
    /// and matching NetworkCapabilities.HasTransport(TransportType.Wifi) plus a
    /// LinkProperties address or route on 192.168.49.0/24.
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

            var addresses = linkProps.LinkAddresses?
                .Select(address => address?.Address?.HostAddress)
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .ToArray() ?? [];
            var routes = linkProps.Routes?
                .Select(route => route?.Destination?.ToString())
                .Where(route => !string.IsNullOrWhiteSpace(route))
                .ToArray() ?? [];

            Log.Info(
                LogTag,
                $"Observed Wi-Fi network links=[{string.Join(",", addresses)}] routes=[{string.Join(",", routes)}]");

            if (addresses.Any(IsP2pAddress) || routes.Any(IsP2pRoute))
            {
                _log.LogInformation(
                    "WiFiP2pHttpClient: resolved P2P network with links [{Links}] and routes [{Routes}]",
                    string.Join(",", addresses),
                    string.Join(",", routes));
                Log.Info(LogTag, "Resolved exact P2P network");
                return network;
            }
        }

        _log.LogWarning("WiFiP2pHttpClient: no P2P network found with 192.168.49.x route");
        Log.Warn(LogTag, "No P2P network found with 192.168.49.x address/route");
        return null;
    }

    private static bool IsP2pAddress(string? address)
        => address?.StartsWith("192.168.49.", StringComparison.Ordinal) == true;

    private static bool IsP2pRoute(string? destination)
        => destination?.StartsWith("192.168.49", StringComparison.Ordinal) == true;

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

    private sealed class PeerListListener : Java.Lang.Object, WifiP2pManager.IPeerListListener
    {
        private readonly Action<WifiP2pDeviceList?> _callback;

        public PeerListListener(Action<WifiP2pDeviceList?> callback)
        {
            _callback = callback;
        }

        public void OnPeersAvailable(WifiP2pDeviceList? peers)
        {
            _callback(peers);
        }
    }

    private sealed class ActionListener : Java.Lang.Object, WifiP2pManager.IActionListener
    {
        private readonly Action _onSuccess;
        private readonly Action<WifiP2pFailureReason> _onFailure;

        public ActionListener(Action onSuccess, Action<WifiP2pFailureReason> onFailure)
        {
            _onSuccess = onSuccess;
            _onFailure = onFailure;
        }

        public void OnSuccess()
        {
            _onSuccess();
        }

        public void OnFailure(WifiP2pFailureReason reason)
        {
            _onFailure(reason);
        }
    }
}
#endif
