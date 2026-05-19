using System.Net.NetworkInformation;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security;
using Microsoft.Extensions.Logging;
using Windows.Devices.WiFi;
using Windows.Security.Credentials;

namespace BodyCam.Platforms.Windows.HeyCyan;

/// <summary>
/// Manages WiFi connectivity to HeyCyan glasses hotspot on Windows.
/// Uses the WinRT <see cref="WiFiAdapter"/> API (available to desktop apps on Win10 19041+).
/// </summary>
internal sealed class WindowsGlassesWiFiManager
{
    private readonly ILogger<WindowsGlassesWiFiManager> _log;

    private WiFiAdapter? _adapter;
    private string? _joinedSsid;

    /// <summary>Default hotspot password used by HeyCyan glasses (from iOS SDK fallback).</summary>
    public const string DefaultPassword = "123456789";

    public WindowsGlassesWiFiManager(ILogger<WindowsGlassesWiFiManager> log)
    {
        _log = log;
    }

    /// <summary>
    /// Discover the glasses' WiFi SSID by scanning available networks after
    /// transfer mode has been entered via BLE. Returns the first SSID matching
    /// known HeyCyan hotspot patterns, or null if none found.
    /// </summary>
    public async Task<string?> DiscoverGlassesSsidAsync(CancellationToken ct)
    {
        var adapter = await EnsureAdapterAsync(ct).ConfigureAwait(false);

        _log.LogInformation("Scanning WiFi networks for glasses hotspot...");
        await adapter.ScanAsync().AsTask(ct).ConfigureAwait(false);

        foreach (var network in adapter.NetworkReport.AvailableNetworks)
        {
            if (IsLikelyGlassesHotspot(network.Ssid))
            {
                _log.LogInformation("Found glasses hotspot: '{Ssid}' (signal={Signal}dBm)",
                    network.Ssid, network.NetworkRssiInDecibelMilliwatts);
                Console.Error.WriteLine($"[WIFI] Found glasses hotspot: '{network.Ssid}' (signal={network.NetworkRssiInDecibelMilliwatts}dBm)");
                return network.Ssid;
            }
        }

        // Log all visible SSIDs for diagnostics
        var ssids = adapter.NetworkReport.AvailableNetworks
            .Select(n => n.Ssid)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
        _log.LogWarning("No glasses hotspot found. Visible SSIDs: [{Ssids}]",
            string.Join(", ", ssids));
        Console.Error.WriteLine($"[WIFI] No glasses hotspot found. Visible SSIDs ({ssids.Count}): [{string.Join(", ", ssids)}]");

        return null;
    }

    /// <summary>
    /// Join the glasses' WiFi hotspot by SSID and password.
    /// </summary>
    public async Task JoinAsync(string ssid, string password, CancellationToken ct)
    {
        var adapter = await EnsureAdapterAsync(ct).ConfigureAwait(false);

        _log.LogInformation("Joining glasses WiFi '{Ssid}'...", ssid);

        // Scan to find the network
        await adapter.ScanAsync().AsTask(ct).ConfigureAwait(false);

        var network = adapter.NetworkReport.AvailableNetworks
            .FirstOrDefault(n => string.Equals(n.Ssid, ssid, StringComparison.OrdinalIgnoreCase));

        if (network is null)
        {
            // Retry scan once — the hotspot may take a moment to appear
            _log.LogDebug("SSID '{Ssid}' not found, retrying scan...", ssid);
            await Task.Delay(2000, ct).ConfigureAwait(false);
            await adapter.ScanAsync().AsTask(ct).ConfigureAwait(false);

            network = adapter.NetworkReport.AvailableNetworks
                .FirstOrDefault(n => string.Equals(n.Ssid, ssid, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Glasses WiFi '{ssid}' not found after two scans");
        }

        var credential = new PasswordCredential { Password = password };
        var result = await adapter.ConnectAsync(
            network, WiFiReconnectionKind.Manual, credential).AsTask(ct).ConfigureAwait(false);

        if (result.ConnectionStatus != WiFiConnectionStatus.Success)
            throw new InvalidOperationException(
                $"Failed to join glasses WiFi '{ssid}': {result.ConnectionStatus}");

        _joinedSsid = ssid;
        _log.LogInformation("Connected to glasses WiFi '{Ssid}'", ssid);
    }

    /// <summary>
    /// Force-connect to a glasses WiFi network by creating a WLAN profile via netsh.
    /// Returns the inferred glasses server IP, or null if connection failed.
    /// This works even when the SSID is not visible in WiFi scans (hidden network).
    /// </summary>
    public async Task<System.Net.IPAddress?> ForceJoinAsync(string ssid, string password, CancellationToken ct)
    {
        _log.LogInformation("Force-joining WiFi '{Ssid}' via WLAN profile...", ssid);
        Console.Error.WriteLine($"[WIFI] Force-joining '{ssid}' via WLAN profile...");

        var profileXml = $"""
            <?xml version="1.0"?>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
                <name>{SecurityElement.Escape(ssid)}</name>
                <SSIDConfig>
                    <SSID>
                        <name>{SecurityElement.Escape(ssid)}</name>
                    </SSID>
                    <nonBroadcast>true</nonBroadcast>
                </SSIDConfig>
                <connectionType>ESS</connectionType>
                <connectionMode>manual</connectionMode>
                <MSM>
                    <security>
                        <authEncryption>
                            <authentication>WPAPSK</authentication>
                            <encryption>TKIP</encryption>
                            <useOneX>false</useOneX>
                        </authEncryption>
                        <sharedKey>
                            <keyType>passPhrase</keyType>
                            <protected>false</protected>
                            <keyMaterial>{SecurityElement.Escape(password)}</keyMaterial>
                        </sharedKey>
                    </security>
                </MSM>
            </WLANProfile>
            """;

        // Write profile to temp file
        var profilePath = Path.Combine(Path.GetTempPath(), $"bodycam_wifi_{Guid.NewGuid():N}.xml");
        try
        {
            await File.WriteAllTextAsync(profilePath, profileXml, ct).ConfigureAwait(false);

            // Add the profile
            var addResult = await RunNetshAsync($"wlan add profile filename=\"{profilePath}\"", ct)
                .ConfigureAwait(false);
            _log.LogDebug("netsh add profile: {Result}", addResult);
            Console.Error.WriteLine($"[WIFI] Profile add: {addResult.TrimEnd()}");

            // The AP readiness is confirmed by BLE GetWifiIP polling before this method is called.
            // Disconnect from current network and connect to glasses.
            Console.Error.WriteLine("[WIFI] Disconnecting from current WiFi to switch to glasses...");
            await RunNetshAsync("wlan disconnect interface=\"Wi-Fi\"", ct).ConfigureAwait(false);
            await Task.Delay(2000, ct).ConfigureAwait(false);

            // Attempt to connect multiple times.
            // The glasses AP takes 30-60s to be fully ready for WPA2 associations.
            const int maxConnectAttempts = 8;
            for (int attempt = 0; attempt < maxConnectAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    Console.Error.WriteLine($"[WIFI] Connect attempt {attempt + 1}/{maxConnectAttempts}...");
                    // Disconnect first to prevent auto-reconnect to home WiFi
                    await RunNetshAsync("wlan disconnect interface=\"Wi-Fi\"", ct).ConfigureAwait(false);
                    await Task.Delay(3000, ct).ConfigureAwait(false);
                }

                var connectResult = await RunNetshAsync(
                    $"wlan connect name=\"{ssid}\" interface=\"Wi-Fi\"", ct)
                    .ConfigureAwait(false);
                _log.LogDebug("netsh connect: {Result}", connectResult);
                Console.Error.WriteLine($"[WIFI] Connect: {connectResult.TrimEnd()}");

                // Wait up to 10 seconds for association to complete
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                    var showResult = await RunNetshAsync("wlan show interfaces", ct).ConfigureAwait(false);

                    // Log state at every second for first attempt, 1s and 5s for subsequent
                    var stateLine = showResult.Split('\n')
                        .FirstOrDefault(l => l.Contains("State", StringComparison.OrdinalIgnoreCase))
                        ?.Trim() ?? "no state";
                    if (attempt == 0 || i == 0 || i == 4)
                    {
                        Console.Error.WriteLine($"[WIFI] Interface state @{i + 1}s: {stateLine}");
                    }
                    if (showResult.Contains(ssid, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine($"[WIFI] Associated with '{ssid}' after {i + 1}s");
                        // Connected! Now wait for DHCP IP assignment.
                        var ip = await WaitForDhcpIpAsync(ssid, ct).ConfigureAwait(false);
                        if (ip is not null)
                        {
                            _joinedSsid = ssid;
                            return ip;
                        }
                        // Got association but no IP — might need to retry
                        break;
                    }
                }
            }

            _log.LogWarning("Force-join to '{Ssid}' - could not connect after {Attempts} attempts", ssid, maxConnectAttempts);
            Console.Error.WriteLine($"[WIFI] Force-join '{ssid}' - no connection after {maxConnectAttempts} attempts");
            return null;
        }
        finally
        {
            // Clean up temp file only — keep profile so we stay connected
            try { File.Delete(profilePath); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Delete the WLAN profile (call when leaving the glasses network).
    /// </summary>
    public void DeleteProfile(string ssid)
    {
        _ = RunNetshAsync($"wlan delete profile name=\"{ssid}\"", CancellationToken.None);
    }

    /// <summary>
    /// Wait for DHCP to assign an IP on the Wi-Fi interface after association.
    /// Returns the inferred glasses IP (.1 in our subnet), or null if no IP assigned within timeout.
    /// </summary>
    private async Task<System.Net.IPAddress?> WaitForDhcpIpAsync(string ssid, CancellationToken ct)
    {
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(1000, ct).ConfigureAwait(false);

            var showResult = await RunNetshAsync("wlan show interfaces", ct).ConfigureAwait(false);

            // If we lost the connection, bail out
            if (!showResult.Contains(ssid, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"[WIFI] Lost association during DHCP wait");
                return null;
            }

            var ip = GetAssignedSubnetFirstIp(ssid, showResult);
            if (ip is not null)
            {
                _log.LogInformation("Force-joined WiFi '{Ssid}', glasses at: {Ip}", ssid, ip);
                Console.Error.WriteLine($"[WIFI] Force-joined '{ssid}', glasses at: {ip}");
                return ip;
            }
        }

        Console.Error.WriteLine($"[WIFI] Associated but no DHCP IP after 10s");
        return null;
    }

    /// <summary>
    /// Get the first IP (.1) in the subnet assigned on the Wi-Fi interface.
    /// The glasses AP server runs at this address.
    /// </summary>
    private System.Net.IPAddress? GetAssignedSubnetFirstIp(string expectedSsid, string showInterfacesOutput)
    {
        // Only proceed if netsh shows us connected to the expected SSID
        if (!showInterfacesOutput.Contains(expectedSsid, StringComparison.OrdinalIgnoreCase))
            return null;

        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (iface.OperationalStatus != OperationalStatus.Up) continue;
            if (!iface.Name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
                && !iface.Name.Contains("WiFi", StringComparison.OrdinalIgnoreCase)
                && !iface.Name.Contains("WLAN", StringComparison.OrdinalIgnoreCase))
                continue;

            var props = iface.GetIPProperties();
            foreach (var unicast in props.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                    continue;
                if (System.Net.IPAddress.IsLoopback(unicast.Address))
                    continue;

                var addrBytes = unicast.Address.GetAddressBytes();
                // Skip link-local (169.254.x.x)
                if (addrBytes[0] == 169 && addrBytes[1] == 254) continue;

                var maskBytes = unicast.IPv4Mask.GetAddressBytes();

                // Compute first IP in subnet (.1)
                var firstIp = new byte[4];
                for (int j = 0; j < 4; j++)
                    firstIp[j] = (byte)(addrBytes[j] & maskBytes[j]);
                firstIp[3] |= 1;

                var candidateIp = new System.Net.IPAddress(firstIp);
                Console.Error.WriteLine($"[WIFI] Interface '{iface.Name}' has IP {unicast.Address}/{unicast.IPv4Mask} → candidate: {candidateIp}");
                _log.LogInformation("WiFi {Iface}: {Ip}/{Mask} → glasses candidate: {Candidate}",
                    iface.Name, unicast.Address, unicast.IPv4Mask, candidateIp);
                return candidateIp;
            }
        }

        return null;
    }

    private static async Task<string> RunNetshAsync(string arguments, CancellationToken ct)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return output;
    }

    /// <summary>
    /// Leave the glasses' WiFi. Windows auto-reconnects to the previous known network.
    /// </summary>
    public void Leave()
    {
        if (_joinedSsid is null)
        {
            _log.LogDebug("Leave called but not connected to glasses WiFi");
            return;
        }

        _log.LogInformation("Leaving glasses WiFi '{Ssid}'", _joinedSsid);
        _adapter?.Disconnect();
        _joinedSsid = null;
    }

    /// <summary>
    /// Whether we are currently connected to a glasses WiFi hotspot.
    /// </summary>
    public bool IsJoined => _joinedSsid is not null;

    /// <summary>
    /// Get the gateway IP of the glasses WiFi connection.
    /// The glasses HTTP server typically runs on the gateway address.
    /// </summary>
    public System.Net.IPAddress? GetGatewayIp()
    {
        if (_adapter is null) return null;

        var adapterId = _adapter.NetworkAdapter.NetworkAdapterId;

        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            // Match by adapter ID suffix or operational status
            if (iface.OperationalStatus != OperationalStatus.Up) continue;

            var props = iface.GetIPProperties();
            foreach (var gw in props.GatewayAddresses)
            {
                if (gw.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && !System.Net.IPAddress.IsLoopback(gw.Address))
                {
                    // Check if this interface corresponds to our WiFi adapter
                    // by matching the network adapter ID
                    if (iface.Id.Contains(adapterId.ToString(), StringComparison.OrdinalIgnoreCase)
                        || iface.Name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
                        || iface.Name.Contains("WiFi", StringComparison.OrdinalIgnoreCase)
                        || iface.Name.Contains("WLAN", StringComparison.OrdinalIgnoreCase))
                    {
                        _log.LogInformation("Gateway IP on '{Iface}': {Ip}", iface.Name, gw.Address);
                        return gw.Address;
                    }
                }
            }
        }

        // Fallback: return any non-loopback gateway (simple setups have only one)
        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (iface.OperationalStatus != OperationalStatus.Up) continue;
            if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var props = iface.GetIPProperties();
            foreach (var gw in props.GatewayAddresses)
            {
                if (gw.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    _log.LogInformation("Fallback gateway IP on '{Iface}': {Ip}", iface.Name, gw.Address);
                    return gw.Address;
                }
            }
        }

        return null;
    }

    private async Task<WiFiAdapter> EnsureAdapterAsync(CancellationToken ct)
    {
        if (_adapter is not null)
            return _adapter;

        var access = await WiFiAdapter.RequestAccessAsync().AsTask(ct).ConfigureAwait(false);
        if (access != WiFiAccessStatus.Allowed)
            throw new InvalidOperationException($"WiFi access denied: {access}");

        var adapters = await WiFiAdapter.FindAllAdaptersAsync().AsTask(ct).ConfigureAwait(false);
        _adapter = adapters.FirstOrDefault()
            ?? throw new InvalidOperationException("No WiFi adapter found on this machine");

        _log.LogDebug("WiFi adapter: {Id}", _adapter.NetworkAdapter.NetworkAdapterId);
        return _adapter;
    }

    /// <summary>
    /// Check if an SSID is likely a HeyCyan glasses hotspot.
    /// Known patterns: device name prefixes (QC, O_, M01), "Cyan", "DIRECT-" (WiFi Direct).
    /// </summary>
    internal static bool IsLikelyGlassesHotspot(string? ssid)
    {
        if (string.IsNullOrEmpty(ssid)) return false;

        return ssid.StartsWith("QC", StringComparison.OrdinalIgnoreCase)
            || ssid.StartsWith("O_", StringComparison.OrdinalIgnoreCase)
            || ssid.StartsWith("M01", StringComparison.OrdinalIgnoreCase)
            || ssid.StartsWith("WIFI", StringComparison.OrdinalIgnoreCase)
            || ssid.Contains("Cyan", StringComparison.OrdinalIgnoreCase)
            || ssid.Contains("HeyCyan", StringComparison.OrdinalIgnoreCase)
            || ssid.StartsWith("DIRECT-", StringComparison.OrdinalIgnoreCase);
    }
}
