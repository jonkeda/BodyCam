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
    private readonly object _diagnosticsLock = new();
    private readonly List<string> _discoveryEvents = new();
    private static readonly IReadOnlyList<ConnectionAttempt> ConnectionAttempts =
    [
        new("pair-gon-pbc-go0-default", WiFiDirectConfigurationMethod.PushButton, WiFiDirectPairingProcedure.GroupOwnerNegotiation, 0, PairFirst: true, DevicePairingProtectionLevel.Default),
        new("pair-gon-pbc-go0-none", WiFiDirectConfigurationMethod.PushButton, WiFiDirectPairingProcedure.GroupOwnerNegotiation, 0, PairFirst: true, DevicePairingProtectionLevel.None),
        new("fromid-default", null, null, null, PairFirst: false, DevicePairingProtectionLevel.Default),
        new("fromid-gon-pbc-go0", WiFiDirectConfigurationMethod.PushButton, WiFiDirectPairingProcedure.GroupOwnerNegotiation, 0, PairFirst: false, DevicePairingProtectionLevel.Default),
        new("pair-gon-pbc-go15-default", WiFiDirectConfigurationMethod.PushButton, WiFiDirectPairingProcedure.GroupOwnerNegotiation, 15, PairFirst: true, DevicePairingProtectionLevel.Default),
        new("fromid-invitation-pbc-go0", WiFiDirectConfigurationMethod.PushButton, WiFiDirectPairingProcedure.Invitation, 0, PairFirst: false, DevicePairingProtectionLevel.Default),
    ];

    /// <summary>Remote IP of the connected glasses (gateway/group owner).</summary>
    public string? RemoteIp { get; private set; }

    /// <summary>Whether a WiFi Direct connection is active.</summary>
    public bool IsConnected => _wifiDirectDevice is not null;

    /// <summary>
    /// Group passphrase received from BLE notification (used for WPS pairing).
    /// Set this before calling <see cref="WaitForPeerAndConnectAsync"/>.
    /// </summary>
    public string? GroupPassword { get; set; }

    public string? MatchedPeerName { get; private set; }
    public string? MatchedPeerId { get; private set; }
    public IReadOnlyList<WindowsWiFiDirectEndpointPair> ConnectionEndpointPairs { get; private set; } = [];
    public IReadOnlyList<string> DiscoveryEvents
    {
        get
        {
            lock (_diagnosticsLock)
                return _discoveryEvents.ToArray();
        }
    }

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
        StartDiscovery(resetDiagnostics: true);
    }

    private void StartDiscovery(bool resetDiagnostics)
    {
        StopWatcher();
        if (resetDiagnostics)
            ResetDiscoveryDiagnostics();

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
        AddDiscoveryEvent("watcher:start");
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

        try
        {
            var deviceId = await WaitForPeerIdAsync(ct).ConfigureAwait(false);

            _log.LogInformation("Connecting to WiFi Direct device: {DeviceId}", deviceId);
            Console.Error.WriteLine($"[WIFIDIRECT] Connecting to: {deviceId}");

            for (var i = 0; i < ConnectionAttempts.Count; i++)
            {
                var attempt = ConnectionAttempts[i];
                try
                {
                    return await TryConnectWithAttemptAsync(deviceId, attempt, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _log.LogWarning(ex,
                        "WiFi Direct attempt {Attempt} failed (HRESULT 0x{HResult:X8})",
                        attempt.Name,
                        ex.HResult);
                    Console.Error.WriteLine(
                        $"[WIFIDIRECT] Attempt {attempt.Name} failed: {ex.GetType().Name} HRESULT=0x{ex.HResult:X8} {ex.Message}");
                    AddDiscoveryEvent($"connect-attempt-failed:name='{attempt.Name}',type='{ex.GetType().Name}',hresult='0x{ex.HResult:X8}',message='{ex.Message}'");

                    _wifiDirectDevice?.Dispose();
                    _wifiDirectDevice = null;
                    ConnectionEndpointPairs = [];

                    if (i < ConnectionAttempts.Count - 1)
                    {
                        var rediscovered = await TryRediscoverPeerAfterFailedAttemptAsync(
                                attempt.Name,
                                ct)
                            .ConfigureAwait(false);
                        if (rediscovered is null)
                            break;

                        deviceId = rediscovered;
                    }
                }
            }

            throw new InvalidOperationException("All WiFi Direct connection attempts failed");
        }
        finally
        {
            StopWatcher();
        }
    }

    private async Task<string> WaitForPeerIdAsync(CancellationToken ct)
    {
        if (_peerFoundTcs is null)
            throw new InvalidOperationException("Call StartDiscovery() first");

        using var reg = ct.Register(() => _peerFoundTcs.TrySetCanceled(ct));
        return await _peerFoundTcs.Task.ConfigureAwait(false);
    }

    private async Task<string?> TryRediscoverPeerAfterFailedAttemptAsync(
        string failedAttemptName,
        CancellationToken ct)
    {
        _log.LogInformation("Rediscovering WiFi Direct peer after failed attempt {Attempt}", failedAttemptName);
        Console.Error.WriteLine($"[WIFIDIRECT] Rediscovering peer after {failedAttemptName}...");
        AddDiscoveryEvent($"connect-attempt:rediscover-after-failure,name='{failedAttemptName}'");

        StartDiscovery(resetDiagnostics: false);

        using var rediscoverCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        rediscoverCts.CancelAfter(TimeSpan.FromSeconds(12));

        try
        {
            var peerId = await WaitForPeerIdAsync(rediscoverCts.Token).ConfigureAwait(false);
            Console.Error.WriteLine($"[WIFIDIRECT] Rediscovered peer: {peerId}");
            AddDiscoveryEvent($"connect-attempt:rediscovered-after-failure,name='{failedAttemptName}',id='{peerId}'");
            return peerId;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("WiFi Direct peer did not reappear after failed attempt {Attempt}", failedAttemptName);
            Console.Error.WriteLine($"[WIFIDIRECT] Peer did not reappear after {failedAttemptName}");
            AddDiscoveryEvent($"connect-attempt:rediscover-timeout,name='{failedAttemptName}'");
            return null;
        }
    }

    private async Task<string> TryConnectWithAttemptAsync(
        string deviceId,
        ConnectionAttempt attempt,
        CancellationToken ct)
    {
        _log.LogInformation(
            "WiFi Direct connection attempt {Attempt}: method={Method}, procedure={Procedure}, GO intent={GroupOwnerIntent}, pairFirst={PairFirst}",
            attempt.Name,
            attempt.ConfigurationMethod,
            attempt.PairingProcedure,
            attempt.GroupOwnerIntent,
            attempt.PairFirst);
        Console.Error.WriteLine(
            $"[WIFIDIRECT] Attempt {attempt.Name}: method={attempt.ConfigurationMethod}, procedure={attempt.PairingProcedure}, go={attempt.GroupOwnerIntent}, pairFirst={attempt.PairFirst}");
        AddDiscoveryEvent($"connect-attempt:start,name='{attempt.Name}',method='{attempt.ConfigurationMethod}',procedure='{attempt.PairingProcedure}',go='{attempt.GroupOwnerIntent}',pairFirst='{attempt.PairFirst}'");

        var deviceInfo = await DeviceInformation.CreateFromIdAsync(deviceId).AsTask(ct)
            .ConfigureAwait(false);

        if (deviceInfo.Pairing.IsPaired)
        {
            _log.LogInformation("Unpairing stale WiFi Direct device for attempt {Attempt}...", attempt.Name);
            Console.Error.WriteLine($"[WIFIDIRECT] Unpairing stale device for {attempt.Name}...");
            var unpairResult = await deviceInfo.Pairing.UnpairAsync().AsTask(ct).ConfigureAwait(false);
            AddDiscoveryEvent($"connect-attempt:unpair-result,name='{attempt.Name}',status='{unpairResult.Status}'");
            deviceInfo = await DeviceInformation.CreateFromIdAsync(deviceId).AsTask(ct)
                .ConfigureAwait(false);
        }

        var connectionParams = attempt.HasConnectionParameters
            ? CreateConnectionParameters(
                attempt.ConfigurationMethod!.Value,
                attempt.PairingProcedure!.Value,
                attempt.GroupOwnerIntent!.Value)
            : null;

        if (attempt.PairFirst && deviceInfo.Pairing.CanPair && !deviceInfo.Pairing.IsPaired)
        {
            _log.LogInformation("Pairing WiFi Direct device for attempt {Attempt}...", attempt.Name);
            Console.Error.WriteLine($"[WIFIDIRECT] Pairing for attempt {attempt.Name}...");

            var pairResult = await PairAsync(
                deviceInfo,
                connectionParams!,
                WiFiDirectConnectionParameters.GetDevicePairingKinds(attempt.ConfigurationMethod!.Value),
                attempt.ProtectionLevel,
                pin: GroupPassword,
                ct).ConfigureAwait(false);

            _log.LogInformation(
                "WiFi Direct attempt {Attempt} pairing result: {Status}, protection={Protection}",
                attempt.Name,
                pairResult.Status,
                pairResult.ProtectionLevelUsed);
            Console.Error.WriteLine(
                $"[WIFIDIRECT] Pairing result for {attempt.Name}: {pairResult.Status}, protection={pairResult.ProtectionLevelUsed}");
            AddDiscoveryEvent($"connect-attempt:pair-result,name='{attempt.Name}',status='{pairResult.Status}',protection='{pairResult.ProtectionLevelUsed}'");
        }

        _wifiDirectDevice = connectionParams is null
            ? await WiFiDirectDevice.FromIdAsync(deviceId).AsTask(ct).ConfigureAwait(false)
            : await WiFiDirectDevice.FromIdAsync(deviceId, connectionParams).AsTask(ct)
                .ConfigureAwait(false);

        if (_wifiDirectDevice is null)
            throw new InvalidOperationException("WiFiDirectDevice.FromIdAsync returned null");

        _wifiDirectDevice.ConnectionStatusChanged += OnConnectionStatusChanged;

        var endpointPairs = _wifiDirectDevice.GetConnectionEndpointPairs();
        if (endpointPairs.Count == 0)
            throw new InvalidOperationException("WiFi Direct connected but no endpoint pairs available");

        ConnectionEndpointPairs = endpointPairs
            .Select(pair => new WindowsWiFiDirectEndpointPair(
                pair.LocalHostName?.CanonicalName,
                pair.LocalServiceName,
                pair.RemoteHostName?.CanonicalName,
                pair.RemoteServiceName))
            .ToArray();

        AddDiscoveryEvent($"connect-attempt:success,name='{attempt.Name}',endpointPairs='{ConnectionEndpointPairs.Count}'");

        for (var i = 0; i < ConnectionEndpointPairs.Count; i++)
        {
            var pair = ConnectionEndpointPairs[i];
            _log.LogInformation(
                "WiFi Direct endpoint pair {Index}: local={LocalHost}:{LocalService}, remote={RemoteHost}:{RemoteService}",
                i,
                pair.LocalHost ?? "(null)",
                pair.LocalService ?? "(null)",
                pair.RemoteHost ?? "(null)",
                pair.RemoteService ?? "(null)");
            Console.Error.WriteLine(
                $"[WIFIDIRECT] Endpoint {i}: local={pair.LocalHost}:{pair.LocalService}, remote={pair.RemoteHost}:{pair.RemoteService}");
        }

        RemoteIp = endpointPairs[0].RemoteHostName?.CanonicalName;
        _log.LogInformation("WiFi Direct connected, remote IP: {Ip}", RemoteIp);
        Console.Error.WriteLine($"[WIFIDIRECT] Connected, remote IP: {RemoteIp}");

        return RemoteIp
            ?? throw new InvalidOperationException("Connected but remote IP is null");
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
        WiFiDirectConfigurationMethod configurationMethod,
        WiFiDirectPairingProcedure pairingProcedure,
        int groupOwnerIntent)
    {
        var connectionParams = new WiFiDirectConnectionParameters
        {
            GroupOwnerIntent = (short)groupOwnerIntent,
            PreferredPairingProcedure = pairingProcedure,
        };
        connectionParams.PreferenceOrderedConfigurationMethods.Add(configurationMethod);
        return connectionParams;
    }

    private async Task<DevicePairingResult> PairAsync(
        DeviceInformation deviceInfo,
        WiFiDirectConnectionParameters connectionParams,
        DevicePairingKinds pairingKinds,
        DevicePairingProtectionLevel protectionLevel,
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
                protectionLevel,
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
        AddDiscoveryEvent($"added:name='{info.Name}',id='{info.Id}'");

        if (IsLikelyGlassesPeer(info.Name))
        {
            _log.LogInformation("Matched glasses peer: '{Name}'", info.Name);
            Console.Error.WriteLine($"[WIFIDIRECT] *** Matched glasses peer: '{info.Name}'");
            MatchedPeerName = info.Name;
            MatchedPeerId = info.Id;
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
                AddDiscoveryEvent($"updated-match:id='{update.Id}'");
                MatchedPeerName = null;
                MatchedPeerId = update.Id;
                _peerFoundTcs.TrySetResult(update.Id);
            }
            else
            {
                AddDiscoveryEvent($"updated:id='{update.Id}'");
            }
        }
        else
        {
            AddDiscoveryEvent($"updated:id='{update.Id}'");
        }
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        _log.LogDebug("WiFi Direct enumeration completed, continuing to watch...");
        AddDiscoveryEvent("watcher:enumeration-completed");
        // Don't stop — keep watching for new peers
    }

    private void OnWatcherStopped(DeviceWatcher sender, object args)
    {
        _log.LogDebug("WiFi Direct watcher stopped");
        AddDiscoveryEvent("watcher:stopped");
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

    private void ResetDiscoveryDiagnostics()
    {
        MatchedPeerName = null;
        MatchedPeerId = null;
        ConnectionEndpointPairs = [];
        lock (_diagnosticsLock)
            _discoveryEvents.Clear();
    }

    private void AddDiscoveryEvent(string message)
    {
        lock (_diagnosticsLock)
        {
            if (_discoveryEvents.Count >= 250)
                _discoveryEvents.RemoveAt(0);
            _discoveryEvents.Add($"{DateTimeOffset.UtcNow:O} {message}");
        }
    }

    private sealed record ConnectionAttempt(
        string Name,
        WiFiDirectConfigurationMethod? ConfigurationMethod,
        WiFiDirectPairingProcedure? PairingProcedure,
        int? GroupOwnerIntent,
        bool PairFirst,
        DevicePairingProtectionLevel ProtectionLevel)
    {
        public bool HasConnectionParameters =>
            ConfigurationMethod.HasValue && PairingProcedure.HasValue && GroupOwnerIntent.HasValue;
    }
}
