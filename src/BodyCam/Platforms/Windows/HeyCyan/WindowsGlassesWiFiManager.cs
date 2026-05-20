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
    private readonly HashSet<string> _profilesSetManual = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Default hotspot password used by HeyCyan glasses (from iOS SDK fallback).</summary>
    public const string DefaultPassword = "123456789";

    /// <summary>Home WiFi SSID to reconnect to after leaving glasses network.</summary>
    private const string HomeWifiSsid = "jobaboe";

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
    public async Task<System.Net.IPAddress?> ForceJoinAsync(string ssid, string password, CancellationToken ct,
        Func<CancellationToken, Task>? bleKeepalive = null)
    {
        _log.LogInformation("Force-joining WiFi '{Ssid}' via WLAN profile...", ssid);
        Console.Error.WriteLine($"[WIFI] Force-joining '{ssid}' via WLAN profile...");

        // Strategy: Install a hidden-network profile and use netsh wlan connect.
        // Don't waste time on WinRT ScanAsync (can't find hidden networks).
        // Don't change band preference (adapter reset loses BSS cache).
        // Let netsh connect handle disconnect atomically.

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
                            <authentication>WPA2PSK</authentication>
                            <encryption>AES</encryption>
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

        var profilePath = Path.Combine(Path.GetTempPath(), $"bodycam_wifi_{Guid.NewGuid():N}.xml");

        // Disable auto-connect on saved profiles so the adapter doesn't snap back
        // to home WiFi after each failed association attempt.
        Console.Error.WriteLine("[WIFI] Disabling auto-connect on saved profiles...");
        var savedProfiles = await RunNetshAsync("wlan show profiles", ct).ConfigureAwait(false);
        var autoConnectProfiles = new List<string>();
        foreach (var profileLine in savedProfiles.Split('\n'))
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                profileLine, @"All User Profile\s*:\s*(.+)");
            if (match.Success)
            {
                var profileName = match.Groups[1].Value.Trim();
                if (!string.Equals(profileName, ssid, StringComparison.OrdinalIgnoreCase))
                {
                    await RunNetshAsync(
                        $"wlan set profileparameter name=\"{profileName}\" connectionMode=manual",
                        ct).ConfigureAwait(false);
                    autoConnectProfiles.Add(profileName);
                }
            }
        }
        Console.Error.WriteLine($"[WIFI] Disabled auto-connect on {autoConnectProfiles.Count} profiles");

        var joined = false;
        try
        {
            await File.WriteAllTextAsync(profilePath, profileXml, ct).ConfigureAwait(false);
            await RunNetshAsync($"wlan delete profile name=\"{ssid}\"", ct).ConfigureAwait(false);
            var addResult = await RunNetshAsync($"wlan add profile filename=\"{profilePath}\" user=current", ct)
                .ConfigureAwait(false);
            Console.Error.WriteLine($"[WIFI] Profile add: {addResult.TrimEnd()}");

            // Don't explicitly disconnect — let netsh connect handle it atomically.
            // This avoids a window where the adapter is idle and not scanning.
            const int maxConnectAttempts = 6;
            for (int attempt = 0; attempt < maxConnectAttempts; attempt++)
            {
                Console.Error.WriteLine($"[WIFI] Connect attempt {attempt + 1}/{maxConnectAttempts}...");

                var connectResult = await RunNetshAsync(
                    $"wlan connect name=\"{ssid}\" ssid=\"{ssid}\" interface=\"Wi-Fi\"", ct)
                    .ConfigureAwait(false);
                Console.Error.WriteLine($"[WIFI] Connect: {connectResult.TrimEnd()}");

                // Wait up to 15s for association — directed probes for hidden networks
                // need time to scan through all channels.
                bool sawAssociating = false;
                for (int i = 0; i < 15; i++)
                {
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                    var showResult = await RunNetshAsync("wlan show interfaces", ct).ConfigureAwait(false);

                    var stateLine = showResult.Split('\n')
                        .FirstOrDefault(l => l.Contains("State", StringComparison.OrdinalIgnoreCase)
                            && !l.Contains("Hosted", StringComparison.OrdinalIgnoreCase))
                        ?.Trim() ?? "no state";
                    var ssidLine = showResult.Split('\n')
                        .FirstOrDefault(l => l.TrimStart().StartsWith("SSID", StringComparison.OrdinalIgnoreCase)
                            && !l.Contains("BSSID", StringComparison.OrdinalIgnoreCase))
                        ?.Trim() ?? "";
                    Console.Error.WriteLine($"[WIFI] @{i + 1}s: {stateLine} | {ssidLine}");

                    if (stateLine.Contains("associating", StringComparison.OrdinalIgnoreCase) ||
                        stateLine.Contains("authenticating", StringComparison.OrdinalIgnoreCase) ||
                        stateLine.Contains("discovering", StringComparison.OrdinalIgnoreCase))
                    {
                        sawAssociating = true;
                        continue; // Keep waiting — the adapter is actively trying
                    }

                    if (showResult.Contains(ssid, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine($"[WIFI] Associated with '{ssid}' after {i + 1}s!");
                        var ip = await WaitForDhcpIpAsync(ssid, ct).ConfigureAwait(false);
                        if (ip is not null)
                        {
                            _joinedSsid = ssid;
                            joined = true;
                            foreach (var profileName in autoConnectProfiles)
                                _profilesSetManual.Add(profileName);
                            return ip;
                        }
                        break;
                    }

                    // If we've been disconnected for 5s+ (no progress), break for retry
                    if (!sawAssociating && i >= 5)
                        break;
                    // If we saw associating then lost it, wait a bit longer then retry
                    if (sawAssociating && i >= 3)
                        break;
                }

                // Send BLE keepalive between attempts
                if (bleKeepalive is not null)
                {
                    try { await bleKeepalive(ct).ConfigureAwait(false); }
                    catch { /* keepalive failure is non-fatal */ }
                }
            }

            // Log WLAN event log for diagnostics
            try
            {
                var wlanLog = await RunPowershellAsync(
                    "Get-WinEvent -LogName 'Microsoft-Windows-WLAN-AutoConfig/Operational' -MaxEvents 10 | " +
                    "Select-Object -Property TimeCreated, Id, Message | " +
                    "ForEach-Object { \"[$($_.TimeCreated.ToString('HH:mm:ss'))] Event $($_.Id): $($_.Message.Split([Environment]::NewLine)[0])\" }",
                    ct).ConfigureAwait(false);
                Console.Error.WriteLine("[WIFI-DIAG] Recent WLAN events:");
                foreach (var line in wlanLog.Split('\n').Take(10))
                {
                    var trimmed = line.TrimEnd();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        Console.Error.WriteLine($"[WIFI-DIAG]   {trimmed}");
                }
            }
            catch { /* diagnostic only */ }

            _log.LogWarning("Force-join to '{Ssid}' failed after {Attempts} attempts", ssid, maxConnectAttempts);
            Console.Error.WriteLine($"[WIFI] Force-join '{ssid}' failed after {maxConnectAttempts} attempts");
            return null;
        }
        finally
        {
            try { File.Delete(profilePath); } catch { /* best effort */ }

            if (!joined)
            {
                // Restore auto-connect on all profiles we disabled if we failed.
                // On success, keep them manual until LeaveAndReconnectAsync so Windows
                // does not snap back to home WiFi before the HTTP download starts.
                Console.Error.WriteLine($"[WIFI] Restoring auto-connect on {autoConnectProfiles.Count} profiles...");
                foreach (var profileName in autoConnectProfiles)
                {
                    await RunNetshAsync(
                        $"wlan set profileparameter name=\"{profileName}\" connectionMode=auto",
                        ct).ConfigureAwait(false);
                }

                Console.Error.WriteLine("[WIFI] Reconnecting to home WiFi...");
                await RunNetshAsync($"wlan connect name=\"{HomeWifiSsid}\" interface=\"Wi-Fi\"", ct)
                    .ConfigureAwait(false);
                await Task.Delay(2000, ct).ConfigureAwait(false);
            }
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

    private static async Task<string> RunPowershellAsync(string command, CancellationToken ct)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"{command}\"",
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
    /// Leave the glasses' WiFi and reconnect to the previous home network.
    /// </summary>
    public async Task LeaveAndReconnectAsync(CancellationToken ct = default)
    {
        var glassesSsid = _joinedSsid;
        _log.LogInformation("Leaving glasses WiFi '{Ssid}'", glassesSsid ?? "(none)");

        // Disconnect from glasses
        _adapter?.Disconnect();
        await RunNetshAsync("wlan disconnect interface=\"Wi-Fi\"", ct).ConfigureAwait(false);
        _joinedSsid = null;

        // Clean up the glasses profile
        if (glassesSsid is not null)
        {
            await RunNetshAsync($"wlan delete profile name=\"{glassesSsid}\"", ct).ConfigureAwait(false);
            Console.Error.WriteLine($"[WIFI] Deleted glasses profile '{glassesSsid}'");
        }

        // Restore profiles that were temporarily set to manual during ForceJoinAsync.
        if (_profilesSetManual.Count > 0)
        {
            Console.Error.WriteLine($"[WIFI] Restoring auto-connect on {_profilesSetManual.Count} profiles...");
            foreach (var profileName in _profilesSetManual.ToArray())
            {
                await RunNetshAsync(
                    $"wlan set profileparameter name=\"{profileName}\" connectionMode=auto",
                    ct).ConfigureAwait(false);
                _profilesSetManual.Remove(profileName);
            }
        }

        // Reconnect to home WiFi — try known home SSID, fall back to any saved profile
        var homeResult = await RunNetshAsync($"wlan connect name=\"{HomeWifiSsid}\" interface=\"Wi-Fi\"", ct)
            .ConfigureAwait(false);
        Console.Error.WriteLine($"[WIFI] Reconnecting to home WiFi: {homeResult.TrimEnd()}");

        // Wait up to 10s for home WiFi to come back
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(1000, ct).ConfigureAwait(false);
            var showResult = await RunNetshAsync("wlan show interfaces", ct).ConfigureAwait(false);
            if (showResult.Contains(HomeWifiSsid, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"[WIFI] Reconnected to home WiFi after {i + 1}s");
                return;
            }
        }

        Console.Error.WriteLine("[WIFI] Could not reconnect to home WiFi within 10s");
    }

    /// <summary>
    /// Leave the glasses' WiFi. Legacy sync version — prefer <see cref="LeaveAndReconnectAsync"/>.
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
