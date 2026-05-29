using System.Diagnostics;
using System.Text.RegularExpressions;

internal sealed class WifiApProbe
{
    public async Task<WifiApProbeResult> RunAsync(string ssid, bool runProtocolProbe)
    {
        var visibleNetworks = await GetVisibleNetworksAsync();
        var connection = await GetCurrentConnectionAsync();
        var profile = await GetSavedProfileAsync(ssid);
        var matchingNetworks = visibleNetworks
            .Where(n => string.Equals(n.Ssid, ssid, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var result = new WifiApProbeResult
        {
            Ssid = ssid,
            IsVisible = matchingNetworks.Count > 0,
            IsConnected = string.Equals(connection.Ssid, ssid, StringComparison.OrdinalIgnoreCase),
            ConnectedSsid = connection.Ssid,
            LocalIPv4 = connection.LocalIPv4,
            Profile = profile,
            Networks = matchingNetworks,
        };

        if (runProtocolProbe && result.IsConnected)
        {
            result.ShouldRunProtocolProbe = true;
        }

        return result;
    }

    public async Task<WifiConnectResult> ConnectAsync(string ssid, TimeSpan timeout)
    {
        var connectOutput = await RunNetshAsync($"wlan connect name=\"{ssid}\" ssid=\"{ssid}\" interface=\"Wi-Fi\"");
        var deadline = DateTimeOffset.Now + timeout;
        WifiApProbeResult? lastProbe = null;

        while (DateTimeOffset.Now < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            lastProbe = await RunAsync(ssid, runProtocolProbe: false);
            if (lastProbe.IsConnected)
            {
                return new WifiConnectResult
                {
                    Ssid = ssid,
                    Success = true,
                    Message = "Connected.",
                    NetshOutput = connectOutput.Trim(),
                    Probe = lastProbe,
                };
            }
        }

        lastProbe ??= await RunAsync(ssid, runProtocolProbe: false);
        return new WifiConnectResult
        {
            Ssid = ssid,
            Success = false,
            Message = "Timed out waiting for Wi-Fi connection.",
            NetshOutput = connectOutput.Trim(),
            Probe = lastProbe,
        };
    }

    private static async Task<List<WifiNetworkInfo>> GetVisibleNetworksAsync()
    {
        var output = await RunNetshAsync("wlan show networks mode=bssid");
        var networks = new List<WifiNetworkInfo>();
        WifiNetworkInfo? current = null;

        foreach (var rawLine in output.Split(Environment.NewLine))
        {
            var line = rawLine.Trim();
            var ssidMatch = Regex.Match(line, @"^SSID\s+\d+\s+:\s*(.*)$", RegexOptions.IgnoreCase);
            if (ssidMatch.Success)
            {
                current = new WifiNetworkInfo { Ssid = ssidMatch.Groups[1].Value.Trim() };
                networks.Add(current);
                continue;
            }

            if (current is null)
                continue;

            if (line.StartsWith("Authentication", StringComparison.OrdinalIgnoreCase))
                current.Authentication = ValueAfterColon(line);
            else if (line.StartsWith("Encryption", StringComparison.OrdinalIgnoreCase))
                current.Encryption = ValueAfterColon(line);
            else if (line.StartsWith("BSSID 1", StringComparison.OrdinalIgnoreCase))
                current.Bssid = ValueAfterColon(line);
            else if (line.StartsWith("Signal", StringComparison.OrdinalIgnoreCase))
                current.Signal = ValueAfterColon(line);
            else if (line.StartsWith("Radio type", StringComparison.OrdinalIgnoreCase))
                current.RadioType = ValueAfterColon(line);
            else if (line.StartsWith("Band", StringComparison.OrdinalIgnoreCase))
                current.Band = ValueAfterColon(line);
            else if (line.StartsWith("Channel", StringComparison.OrdinalIgnoreCase))
                current.Channel = ValueAfterColon(line);
        }

        return networks;
    }

    private static async Task<WifiConnectionInfo> GetCurrentConnectionAsync()
    {
        var output = await RunNetshAsync("wlan show interfaces");
        var connection = new WifiConnectionInfo();

        foreach (var rawLine in output.Split(Environment.NewLine))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
            {
                connection.Ssid = ValueAfterColon(line);
            }
            else if (line.StartsWith("Profile", StringComparison.OrdinalIgnoreCase))
            {
                connection.Profile = ValueAfterColon(line);
            }
        }

        connection.LocalIPv4 = await GetCurrentWifiIpv4Async();
        return connection;
    }

    private static async Task<WifiProfileInfo?> GetSavedProfileAsync(string ssid)
    {
        var profilesOutput = await RunNetshAsync("wlan show profiles");
        if (!profilesOutput.Contains(ssid, StringComparison.OrdinalIgnoreCase))
            return null;

        var profileOutput = await RunNetshAsync($"wlan show profile name=\"{ssid}\"");
        var profile = new WifiProfileInfo
        {
            Name = ssid,
            IsSaved = true,
        };

        foreach (var rawLine in profileOutput.Split(Environment.NewLine))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("Applied", StringComparison.OrdinalIgnoreCase))
                profile.Scope = ValueAfterColon(line);
            else if (line.StartsWith("Connection mode", StringComparison.OrdinalIgnoreCase))
                profile.ConnectionMode = ValueAfterColon(line);
            else if (line.StartsWith("Network broadcast", StringComparison.OrdinalIgnoreCase))
                profile.NetworkBroadcast = ValueAfterColon(line);
            else if (line.StartsWith("Authentication", StringComparison.OrdinalIgnoreCase))
                profile.Authentication.Add(ValueAfterColon(line));
            else if (line.StartsWith("Cipher", StringComparison.OrdinalIgnoreCase))
                profile.Ciphers.Add(ValueAfterColon(line));
            else if (line.StartsWith("Security key", StringComparison.OrdinalIgnoreCase))
                profile.SecurityKey = ValueAfterColon(line);
        }

        return profile;
    }

    private static async Task<string?> GetCurrentWifiIpv4Async()
    {
        var script = "Get-NetIPAddress -AddressFamily IPv4 -InterfaceAlias Wi-Fi -ErrorAction SilentlyContinue | " +
                     "Where-Object AddressState -eq 'Preferred' | Select-Object -First 1 -ExpandProperty IPAddress";
        var output = await RunProcessAsync("powershell", $"-NoProfile -Command \"{script}\"");
        return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
    }

    private static async Task<string> RunNetshAsync(string arguments)
    {
        return await RunProcessAsync("netsh", arguments);
    }

    private static async Task<string> RunProcessAsync(string fileName, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 ? stdout : stdout + Environment.NewLine + stderr;
    }

    private static string ValueAfterColon(string line)
    {
        var index = line.IndexOf(':');
        return index < 0 ? string.Empty : line[(index + 1)..].Trim();
    }
}

internal sealed class WifiApProbeResult
{
    public required string Ssid { get; init; }

    public bool IsVisible { get; init; }

    public bool IsConnected { get; init; }

    public string? ConnectedSsid { get; init; }

    public string? LocalIPv4 { get; init; }

    public WifiProfileInfo? Profile { get; init; }

    public bool ShouldRunProtocolProbe { get; set; }

    public List<WifiNetworkInfo> Networks { get; init; } = [];

    public string ToReadableString()
    {
        var lines = new List<string>
        {
            $"Wi-Fi AP probe: {Ssid}",
            $"Visible: {(IsVisible ? "yes" : "no")}",
            $"Saved profile: {(Profile?.IsSaved == true ? "yes" : "no")}",
            $"Connected: {(IsConnected ? "yes" : "no")}",
            $"Current SSID: {ConnectedSsid ?? "none"}",
            $"Local IPv4: {LocalIPv4 ?? "none"}",
        };

        if (Profile is not null)
        {
            lines.Add($"Profile scope: {Profile.Scope ?? "unknown"}");
            lines.Add($"Profile connection mode: {Profile.ConnectionMode ?? "unknown"}");
            lines.Add($"Profile broadcast: {Profile.NetworkBroadcast ?? "unknown"}");
            lines.Add($"Profile auth: {Distinct(Profile.Authentication)}");
            lines.Add($"Profile ciphers: {Distinct(Profile.Ciphers)}");
            lines.Add($"Profile key: {Profile.SecurityKey ?? "unknown"}");
        }

        foreach (var network in Networks)
        {
            lines.Add($"BSSID: {network.Bssid ?? "unknown"}");
            lines.Add($"Signal: {network.Signal ?? "unknown"}");
            lines.Add($"Auth: {network.Authentication ?? "unknown"}");
            lines.Add($"Encryption: {network.Encryption ?? "unknown"}");
            lines.Add($"Radio: {network.RadioType ?? "unknown"}");
            lines.Add($"Band: {network.Band ?? "unknown"}");
            lines.Add($"Channel: {network.Channel ?? "unknown"}");
        }

        if (IsVisible && !IsConnected)
            lines.Add("Reachability: AP is visible, but the PC is not connected to it yet.");
        else if (!IsVisible && Profile?.IsSaved == true && !IsConnected)
            lines.Add("Reachability: saved profile exists, but the AP is not visible in CLI scan and the PC is not connected.");
        else if (!IsVisible)
            lines.Add("Reachability: AP is not currently visible to Windows.");
        else
            lines.Add("Reachability: PC is connected to the AP; protocol probing can run.");

        return string.Join(Environment.NewLine, lines);
    }

    private static string Distinct(IEnumerable<string> values)
    {
        var distinct = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return distinct.Length == 0 ? "unknown" : string.Join(", ", distinct);
    }
}

internal sealed class WifiConnectResult
{
    public required string Ssid { get; init; }

    public bool Success { get; init; }

    public required string Message { get; init; }

    public string? NetshOutput { get; init; }

    public required WifiApProbeResult Probe { get; init; }

    public string ToReadableString()
    {
        return string.Join(Environment.NewLine, [
            $"Wi-Fi connect: {Ssid}",
            $"Success: {(Success ? "yes" : "no")}",
            $"Message: {Message}",
            $"Netsh: {NetshOutput ?? "none"}",
            Probe.ToReadableString(),
        ]);
    }
}

internal sealed class WifiNetworkInfo
{
    public required string Ssid { get; init; }

    public string? Authentication { get; set; }

    public string? Encryption { get; set; }

    public string? Bssid { get; set; }

    public string? Signal { get; set; }

    public string? RadioType { get; set; }

    public string? Band { get; set; }

    public string? Channel { get; set; }
}

internal sealed class WifiConnectionInfo
{
    public string? Ssid { get; set; }

    public string? Profile { get; set; }

    public string? LocalIPv4 { get; set; }
}

internal sealed class WifiProfileInfo
{
    public required string Name { get; init; }

    public bool IsSaved { get; init; }

    public string? Scope { get; set; }

    public string? ConnectionMode { get; set; }

    public string? NetworkBroadcast { get; set; }

    public List<string> Authentication { get; } = [];

    public List<string> Ciphers { get; } = [];

    public string? SecurityKey { get; set; }
}
