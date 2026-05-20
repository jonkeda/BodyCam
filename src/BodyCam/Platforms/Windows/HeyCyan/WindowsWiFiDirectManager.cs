using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Windows.Devices.Enumeration;
using Windows.Devices.WiFiDirect;
using Windows.Networking;

namespace BodyCam.Platforms.Windows.HeyCyan;

/// <summary>
/// Manages WiFi Direct (P2P) connections to HeyCyan glasses on Windows.
/// The glasses use WiFi Direct for file transfer — they are not visible
/// via regular WiFi scans. This replaces the Android <c>WifiP2pManager</c>
/// and the iOS hotspot approach.
/// </summary>
internal sealed class WindowsWiFiDirectManager : IWindowsWiFiDirectConnector
{
    private readonly ILogger<WindowsWiFiDirectManager> _log;
    private readonly string? _bleMac; // e.g. "D879B87FE6C9" (no colons)

    private DeviceWatcher? _watcher;
    private WiFiDirectDevice? _wifiDirectDevice;
    private TaskCompletionSource<string>? _peerFoundTcs;
    private readonly object _lock = new();

    /// <summary>Remote IP of the connected glasses (gateway/group owner).</summary>
    public string? RemoteIp { get; private set; }

    /// <summary>Whether a WiFi Direct connection is active.</summary>
    public bool IsConnected => _wifiDirectDevice is not null;

    /// <summary>
    /// Group passphrase received from BLE notification (used for WPS pairing).
    /// Set this before calling <see cref="WaitForPeerAndConnectAsync"/>.
    /// </summary>
    public string? GroupPassword { get; set; }

    public WindowsWiFiDirectManager(
        ILogger<WindowsWiFiDirectManager> log,
        string? bleMacAddress = null)
    {
        _log = log;
        _bleMac = bleMacAddress?.Replace(":", "").Replace("-", "").ToUpperInvariant();
    }

    /// <summary>
    /// Start WiFi Direct peer discovery without waiting for a connection.
    /// Call this BEFORE sending the BLE transfer command (matching Android flow
    /// where <c>discoverPeers()</c> is called before <c>glassesControl(enterTransfer)</c>).
    /// </summary>
    public void StartDiscovery()
    {
        StopWatcher();

        _peerFoundTcs = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Try AssociationEndpoint first (matches WifiP2pManager on Android)
        var selector = WiFiDirectDevice.GetDeviceSelector(WiFiDirectDeviceSelectorType.AssociationEndpoint);

        _log.LogInformation("WiFi Direct selector: {Selector}", selector);
        Console.Error.WriteLine($"[WIFIDIRECT] Selector: {selector}");

        _watcher = DeviceInformation.CreateWatcher(
            selector,
            new[] { "System.Devices.WiFiDirect.InformationElements" });

        _watcher.Added += OnDeviceAdded;
        _watcher.Updated += OnDeviceUpdated;
        _watcher.Stopped += OnWatcherStopped;
        _watcher.EnumerationCompleted += OnEnumerationCompleted;

        _log.LogInformation("Starting WiFi Direct peer discovery...");
        Console.Error.WriteLine("[WIFIDIRECT] Starting peer discovery...");
        _watcher.Start();
    }

    /// <summary>
    /// Wait for a glasses peer to appear and connect to it.
    /// <see cref="StartDiscovery"/> must be called first.
    /// Returns the remote IP address of the glasses HTTP server.
    /// </summary>
    public async Task<string> WaitForPeerAndConnectAsync(CancellationToken ct)
    {
        if (_peerFoundTcs is null)
            throw new InvalidOperationException("Call StartDiscovery() first");

        // Register cancellation
        using var reg = ct.Register(() => _peerFoundTcs.TrySetCanceled(ct));

        try
        {
            var deviceId = await _peerFoundTcs.Task.ConfigureAwait(false);

            _log.LogInformation("Connecting to WiFi Direct device: {DeviceId}", deviceId);
            Console.Error.WriteLine($"[WIFIDIRECT] Connecting to: {deviceId}");

            // Pair with PIN if we have a group password from BLE
            var deviceInfo = await DeviceInformation.CreateFromIdAsync(deviceId).AsTask(ct)
                .ConfigureAwait(false);

            // Unpair first if previously paired (stale state from failed prior attempts)
            if (deviceInfo.Pairing.IsPaired)
            {
                _log.LogInformation("Unpairing stale WiFi Direct device...");
                Console.Error.WriteLine("[WIFIDIRECT] Unpairing stale device...");
                await deviceInfo.Pairing.UnpairAsync().AsTask(ct).ConfigureAwait(false);
                // Re-fetch device info after unpair
                deviceInfo = await DeviceInformation.CreateFromIdAsync(deviceId).AsTask(ct)
                    .ConfigureAwait(false);
            }

            var connectionParams = CreateConnectionParameters(WiFiDirectConfigurationMethod.PushButton);

            if (deviceInfo.Pairing.CanPair && !deviceInfo.Pairing.IsPaired)
            {
                _log.LogInformation("Pairing WiFi Direct device with WPS PushButton...");
                Console.Error.WriteLine("[WIFIDIRECT] Pairing with WPS PushButton...");

                var pairResult = await PairAsync(
                    deviceInfo,
                    connectionParams,
                    WiFiDirectConnectionParameters.GetDevicePairingKinds(WiFiDirectConfigurationMethod.PushButton),
                    pin: null,
                    ct).ConfigureAwait(false);

                _log.LogInformation("Pairing result: {Status}", pairResult.Status);
                Console.Error.WriteLine($"[WIFIDIRECT] Pairing result: {pairResult.Status}");

                if (pairResult.Status != DevicePairingResultStatus.Paired
                    && pairResult.Status != DevicePairingResultStatus.AlreadyPaired)
                {
                    _log.LogWarning("PushButton pairing failed: {Status}, attempting connection anyway", pairResult.Status);
                    Console.Error.WriteLine("[WIFIDIRECT] PushButton failed; attempting connection anyway...");
                }
            }

            _wifiDirectDevice = await WiFiDirectDevice.FromIdAsync(deviceId, connectionParams).AsTask(ct)
                .ConfigureAwait(false);

            if (_wifiDirectDevice is null)
                throw new InvalidOperationException("WiFiDirectDevice.FromIdAsync returned null");

            _wifiDirectDevice.ConnectionStatusChanged += OnConnectionStatusChanged;

            var endpointPairs = _wifiDirectDevice.GetConnectionEndpointPairs();
            if (endpointPairs.Count == 0)
                throw new InvalidOperationException("WiFi Direct connected but no endpoint pairs available");

            RemoteIp = endpointPairs[0].RemoteHostName?.CanonicalName;
            _log.LogInformation("WiFi Direct connected, remote IP: {Ip}", RemoteIp);
            Console.Error.WriteLine($"[WIFIDIRECT] Connected, remote IP: {RemoteIp}");

            return RemoteIp
                ?? throw new InvalidOperationException("Connected but remote IP is null");
        }
        finally
        {
            StopWatcher();
        }
    }

    /// <summary>
    /// Discover and connect to the glasses WiFi Direct peer (combined convenience method).
    /// Returns the remote IP address of the glasses HTTP server.
    /// </summary>
    public async Task<string> ConnectAsync(CancellationToken ct)
    {
        Disconnect();
        StartDiscovery();
        return await WaitForPeerAndConnectAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Disconnect from the glasses WiFi Direct group.</summary>
    public void Disconnect()
    {
        StopWatcher();

        if (_wifiDirectDevice is not null)
        {
            _log.LogInformation("Disconnecting WiFi Direct");
            _wifiDirectDevice.ConnectionStatusChanged -= OnConnectionStatusChanged;
            _wifiDirectDevice.Dispose();
            _wifiDirectDevice = null;
        }

        RemoteIp = null;
    }

    public void Dispose() => Disconnect();

    private static WiFiDirectConnectionParameters CreateConnectionParameters(
        WiFiDirectConfigurationMethod configurationMethod)
    {
        var connectionParams = new WiFiDirectConnectionParameters
        {
            // Android often becomes the P2P group owner for this flow; the
            // glasses then report their own HTTP IP over BLE. Prefer that role.
            GroupOwnerIntent = 15,
            PreferredPairingProcedure = WiFiDirectPairingProcedure.GroupOwnerNegotiation,
        };
        connectionParams.PreferenceOrderedConfigurationMethods.Add(configurationMethod);
        return connectionParams;
    }

    private async Task<DevicePairingResult> PairAsync(
        DeviceInformation deviceInfo,
        WiFiDirectConnectionParameters connectionParams,
        DevicePairingKinds pairingKinds,
        string? pin,
        CancellationToken ct)
    {
        var customPairing = deviceInfo.Pairing.Custom;

        void OnPairingRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
        {
            _log.LogInformation("Pairing requested: Kind={Kind}", args.PairingKind);
            Console.Error.WriteLine($"[WIFIDIRECT] Pairing requested: {args.PairingKind}");

            switch (args.PairingKind)
            {
                case DevicePairingKinds.ProvidePin when !string.IsNullOrEmpty(pin):
                    args.Accept(pin);
                    break;
                case DevicePairingKinds.ConfirmOnly:
                case DevicePairingKinds.DisplayPin:
                    args.Accept();
                    break;
                default:
                    if (!string.IsNullOrEmpty(pin))
                        args.Accept(pin);
                    else
                        args.Accept();
                    break;
            }
        }

        customPairing.PairingRequested += OnPairingRequested;
        try
        {
            return await customPairing.PairAsync(
                pairingKinds,
                DevicePairingProtectionLevel.None,
                connectionParams).AsTask(ct).ConfigureAwait(false);
        }
        finally
        {
            customPairing.PairingRequested -= OnPairingRequested;
        }
    }

    // ── Device watcher callbacks ────────────────────────────────────────

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation info)
    {
        _log.LogDebug("WiFi Direct peer found: Name='{Name}' Id='{Id}'",
            info.Name, info.Id);
        Console.Error.WriteLine($"[WIFIDIRECT] Peer found: '{info.Name}' (id={info.Id})");

        if (IsLikelyGlassesPeer(info.Name))
        {
            _log.LogInformation("Matched glasses peer: '{Name}'", info.Name);
            Console.Error.WriteLine($"[WIFIDIRECT] *** Matched glasses peer: '{info.Name}'");
            _peerFoundTcs?.TrySetResult(info.Id);
        }
    }

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        // If the peer was previously found and is updated (re-advertised), match by ID.
        // This handles the case where the glasses were found in a previous session
        // and Windows fires Updated instead of Added.
        _log.LogDebug("WiFi Direct peer updated: {Id}", update.Id);

        // Check if the ID contains the BLE MAC (format: WiFiDirect#XX:XX:XX:XX:XX:XX)
        if (!string.IsNullOrEmpty(_bleMac) && _peerFoundTcs is not null && !_peerFoundTcs.Task.IsCompleted)
        {
            var idUpper = update.Id.Replace(":", "").ToUpperInvariant();
            if (idUpper.Contains(_bleMac))
            {
                _log.LogInformation("Matched glasses peer on update: '{Id}'", update.Id);
                Console.Error.WriteLine($"[WIFIDIRECT] *** Matched glasses peer (update): {update.Id}");
                _peerFoundTcs.TrySetResult(update.Id);
            }
        }
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        _log.LogDebug("WiFi Direct enumeration completed, continuing to watch...");
        // Don't stop — keep watching for new peers
    }

    private void OnWatcherStopped(DeviceWatcher sender, object args)
    {
        _log.LogDebug("WiFi Direct watcher stopped");
    }

    private void OnConnectionStatusChanged(WiFiDirectDevice sender, object args)
    {
        if (sender.ConnectionStatus == WiFiDirectConnectionStatus.Disconnected)
        {
            _log.LogWarning("WiFi Direct connection lost");
            RemoteIp = null;
        }
    }

    // ── Peer matching ───────────────────────────────────────────────────

    /// <summary>
    /// Check if a WiFi Direct device name is likely a HeyCyan glasses peer.
    /// Matches the CyanBridge Android <c>isLikelyGlassesPeer()</c> heuristic:
    /// - Contains the BLE MAC address (without colons)
    /// - Starts with "AIM" or contains "AIMB-" or "GLASS"
    /// - Contains a 12-char hex string (MAC-like pattern)
    /// Also matches HeyCyan-specific patterns: QC, O_, M01, Cyan.
    /// </summary>
    internal bool IsLikelyGlassesPeer(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return false;

        var name = deviceName.ToUpperInvariant();

        // Match by BLE MAC (most reliable)
        if (!string.IsNullOrEmpty(_bleMac) && name.Contains(_bleMac))
            return true;

        // CyanBridge patterns
        if (name.StartsWith("AIM") || name.Contains("AIMB-") || name.Contains("GLASS"))
            return true;

        // HeyCyan-specific patterns
        if (name.StartsWith("QC") || name.StartsWith("O_") || name.StartsWith("M01"))
            return true;
        if (name.Contains("CYAN") || name.Contains("HEYCYAN"))
            return true;

        // WiFi Direct prefix
        if (name.StartsWith("DIRECT-"))
            return true;

        // 12-char hex string (MAC-like)
        if (Regex.IsMatch(name, "[A-F0-9]{12}"))
            return true;

        return false;
    }

    private void StopWatcher()
    {
        if (_watcher is not null)
        {
            if (_watcher.Status == DeviceWatcherStatus.Started
                || _watcher.Status == DeviceWatcherStatus.EnumerationCompleted)
            {
                _watcher.Stop();
            }

            _watcher.Added -= OnDeviceAdded;
            _watcher.Updated -= OnDeviceUpdated;
            _watcher.Stopped -= OnWatcherStopped;
            _watcher.EnumerationCompleted -= OnEnumerationCompleted;
            _watcher = null;
        }
    }
}
