using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace BodyCam.Services.Camera.A9.Vue990;

public sealed class A9Vue990PpcsTransportProbeClient
{
    private static readonly A9Vue990UdpProbePayload[] UdpPayloads =
    [
        new("legacy LanSearch", [0xf1, 0x30, 0x00, 0x00]),
        new("SHIX/A9_PPPP seed", [0x2c, 0xba, 0x5f, 0x5d]),
        new("JSON discover", Encoding.ASCII.GetBytes("{\"cmd\":\"discover\"}")),
    ];

    public async Task<A9Vue990PpcsTransportProbeResult> ProbeAsync(
        A9Vue990PpcsTransportProbeOptions options,
        CancellationToken ct = default)
    {
        var result = new A9Vue990PpcsTransportProbeResult
        {
            Timestamp = DateTimeOffset.Now,
            Host = options.Host,
        };

        result.Status = await new A9Vue990StatusClient().GetStatusAsync(new A9Vue990StatusOptions
        {
            Host = options.Host,
            Port = options.StatusPort,
            Username = options.Username,
            Password = options.Password,
            Timeout = options.StatusTimeout,
        }, ct).ConfigureAwait(false);

        if (!result.Status.Success)
        {
            result.Error = "Status fetch failed; transport probe stopped.";
            return result;
        }

        if (!A9Vue990DasServerParameter.TryParse(result.Status.Server, out var das, out var dasError) ||
            das is null)
        {
            result.Error = dasError ?? "DAS parse failed.";
            return result;
        }

        result.Das = das;
        var hostAddress = await ResolveIpv4Async(options.Host, ct).ConfigureAwait(false);
        if (hostAddress is null)
        {
            result.Error = $"Could not resolve IPv4 address for {options.Host}.";
            return result;
        }

        foreach (var port in options.TcpPorts.Distinct().Where(IsValidPort))
        {
            result.Attempts.Add(await ProbeTcpAsync(hostAddress, port, options, ct).ConfigureAwait(false));
        }

        foreach (var port in options.UdpPorts.Distinct().Where(IsValidPort))
        {
            foreach (var payload in UdpPayloads)
            {
                result.Attempts.Add(await ProbeUdpAsync(hostAddress, port, payload, options, ct).ConfigureAwait(false));
            }
        }

        if (options.ProbeDecodedRelayHosts && das.DecodedPayload.RelayHosts.Count > 0)
        {
            foreach (var relayHost in das.DecodedPayload.RelayHosts)
            {
                var relayAddress = await ResolveIpv4Async(relayHost, ct).ConfigureAwait(false);
                if (relayAddress is null)
                {
                    result.Attempts.Add(new A9Vue990PpcsTransportAttempt
                    {
                        Protocol = A9Vue990PpcsTransportProtocol.Tcp,
                        TargetKind = A9Vue990PpcsTransportTargetKind.DecodedRelay,
                        Host = relayHost,
                        Port = 0,
                        Outcome = "Relay host could not be resolved.",
                    });
                    continue;
                }

                foreach (var port in options.RelayTcpPorts.Distinct().Where(IsValidPort))
                {
                    result.Attempts.Add(await ProbeTcpAsync(
                        relayAddress,
                        port,
                        options,
                        A9Vue990PpcsTransportTargetKind.DecodedRelay,
                        ct).ConfigureAwait(false));
                }

                foreach (var port in options.RelayUdpPorts.Distinct().Where(IsValidPort))
                {
                    foreach (var payload in UdpPayloads)
                    {
                        result.Attempts.Add(await ProbeUdpAsync(
                            relayAddress,
                            port,
                            payload,
                            options,
                            A9Vue990PpcsTransportTargetKind.DecodedRelay,
                            ct).ConfigureAwait(false));
                    }
                }
            }
        }

        result.Success = true;
        result.HasTransportSignal = result.Attempts.Any(attempt =>
            attempt.Protocol == A9Vue990PpcsTransportProtocol.Tcp
                ? attempt.Opened
                : attempt.BytesReceived > 0 && attempt.RemoteMatchesHost);
        return result;
    }

    private static async Task<A9Vue990PpcsTransportAttempt> ProbeTcpAsync(
        IPAddress hostAddress,
        int port,
        A9Vue990PpcsTransportProbeOptions options,
        CancellationToken ct)
    {
        return await ProbeTcpAsync(
            hostAddress,
            port,
            options,
            A9Vue990PpcsTransportTargetKind.CameraLan,
            ct).ConfigureAwait(false);
    }

    private static async Task<A9Vue990PpcsTransportAttempt> ProbeTcpAsync(
        IPAddress hostAddress,
        int port,
        A9Vue990PpcsTransportProbeOptions options,
        A9Vue990PpcsTransportTargetKind targetKind,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var attempt = new A9Vue990PpcsTransportAttempt
        {
            Protocol = A9Vue990PpcsTransportProtocol.Tcp,
            TargetKind = targetKind,
            Host = hostAddress.ToString(),
            Port = port,
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.ConnectTimeout);

            using var tcp = new TcpClient(AddressFamily.InterNetwork);
            await tcp.ConnectAsync(hostAddress, port, timeout.Token).ConfigureAwait(false);
            attempt.Opened = true;
            attempt.RemoteEndpoint = tcp.Client.RemoteEndPoint?.ToString();

            await using var stream = tcp.GetStream();
            var bytes = await ReadTcpBannerAsync(stream, options, ct).ConfigureAwait(false);
            ApplyBytes(attempt, bytes);
            attempt.Outcome = bytes.Length > 0
                ? "TCP connected and returned bytes."
                : "TCP connected; no banner bytes returned within read window.";
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
        {
            attempt.Error = $"{ex.GetType().Name}: {ex.Message}";
            attempt.Outcome = ex is OperationCanceledException
                ? "TCP connect/read timed out."
                : "TCP connect failed.";
        }
        finally
        {
            attempt.DurationMs = sw.ElapsedMilliseconds;
        }

        return attempt;
    }

    private static async Task<A9Vue990PpcsTransportAttempt> ProbeUdpAsync(
        IPAddress hostAddress,
        int port,
        A9Vue990UdpProbePayload payload,
        A9Vue990PpcsTransportProbeOptions options,
        CancellationToken ct)
    {
        return await ProbeUdpAsync(
            hostAddress,
            port,
            payload,
            options,
            A9Vue990PpcsTransportTargetKind.CameraLan,
            ct).ConfigureAwait(false);
    }

    private static async Task<A9Vue990PpcsTransportAttempt> ProbeUdpAsync(
        IPAddress hostAddress,
        int port,
        A9Vue990UdpProbePayload payload,
        A9Vue990PpcsTransportProbeOptions options,
        A9Vue990PpcsTransportTargetKind targetKind,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var attempt = new A9Vue990PpcsTransportAttempt
        {
            Protocol = A9Vue990PpcsTransportProtocol.Udp,
            TargetKind = targetKind,
            Host = hostAddress.ToString(),
            Port = port,
            PayloadName = payload.Name,
            BytesSent = payload.Bytes.Length,
        };

        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            await udp.SendAsync(payload.Bytes, new IPEndPoint(hostAddress, port), ct).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.ReadTimeout);
            var response = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
            attempt.RemoteEndpoint = response.RemoteEndPoint.ToString();
            attempt.RemoteMatchesHost = response.RemoteEndPoint.Address.Equals(hostAddress);
            ApplyBytes(attempt, response.Buffer.AsSpan(0, Math.Min(response.Buffer.Length, options.MaxBytes)).ToArray());
            attempt.Outcome = attempt.RemoteMatchesHost
                ? "UDP response received from target host."
                : "UDP response received from a different endpoint; treat as echo/noise.";
        }
        catch (OperationCanceledException)
        {
            attempt.Outcome = "No UDP response within read window.";
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            attempt.Error = $"{ex.GetType().Name}: {ex.Message}";
            attempt.Outcome = "UDP send/receive failed.";
        }
        finally
        {
            attempt.DurationMs = sw.ElapsedMilliseconds;
        }

        return attempt;
    }

    private static async Task<byte[]> ReadTcpBannerAsync(
        NetworkStream stream,
        A9Vue990PpcsTransportProbeOptions options,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.ReadTimeout);

        var buffer = new byte[options.MaxBytes];
        try
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token)
                .ConfigureAwait(false);
            return buffer[..read];
        }
        catch (OperationCanceledException)
        {
            return [];
        }
    }

    private static async Task<IPAddress?> ResolveIpv4Async(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var parsed) &&
            parsed.AddressFamily == AddressFamily.InterNetwork)
        {
            return parsed;
        }

        var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct)
            .ConfigureAwait(false);
        return addresses.FirstOrDefault();
    }

    private static void ApplyBytes(A9Vue990PpcsTransportAttempt attempt, byte[] bytes)
    {
        attempt.BytesReceived = bytes.Length;
        attempt.PrefixHex = ToHex(bytes, 48);
        attempt.PrefixText = ToSafeText(bytes, 160);
        attempt.Sha256 = bytes.Length == 0 ? null : Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string ToHex(byte[] bytes, int maxBytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        var slice = bytes.AsSpan(0, Math.Min(maxBytes, bytes.Length));
        var hex = Convert.ToHexString(slice);
        return bytes.Length > maxBytes ? hex + "..." : hex;
    }

    private static string ToSafeText(byte[] bytes, int maxChars)
    {
        if (bytes.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var value in bytes.AsSpan(0, Math.Min(bytes.Length, maxChars)))
        {
            sb.Append(value is >= 32 and <= 126 ? (char)value : '.');
        }

        if (bytes.Length > maxChars)
            sb.Append("...");

        return sb.ToString();
    }

    private static bool IsValidPort(int port)
    {
        return port is >= 1 and <= 65535;
    }

    private sealed record A9Vue990UdpProbePayload(string Name, byte[] Bytes);
}

public sealed class A9Vue990PpcsTransportProbeOptions
{
    public string Host { get; init; } = "192.168.168.1";

    public int StatusPort { get; init; } = 81;

    public string Username { get; init; } = "admin";

    public string Password { get; init; } = "888888";

    public TimeSpan StatusTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromMilliseconds(1200);

    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromMilliseconds(750);

    public int MaxBytes { get; init; } = 4096;

    public IReadOnlyList<int> TcpPorts { get; init; } = [65527, 20190, 32108, 15203, 3478];

    public IReadOnlyList<int> UdpPorts { get; init; } = [65531, 32108, 20190];

    public bool ProbeDecodedRelayHosts { get; init; }

    public IReadOnlyList<int> RelayTcpPorts { get; init; } =
    [
        32108,
        32100,
        32110,
        32117,
        32120,
        32130,
        20190,
        65527,
        15203,
        3478,
        80,
        443,
        8000,
        8080,
    ];

    public IReadOnlyList<int> RelayUdpPorts { get; init; } = [32108, 32100, 20190, 65531, 3478];
}

public sealed class A9Vue990PpcsTransportProbeResult
{
    public DateTimeOffset Timestamp { get; init; }

    public required string Host { get; init; }

    public bool Success { get; set; }

    public bool HasTransportSignal { get; set; }

    public string? Error { get; set; }

    public A9Vue990StatusResult? Status { get; set; }

    public A9Vue990DasServerParameter? Das { get; set; }

    public List<A9Vue990PpcsTransportAttempt> Attempts { get; } = [];

    public string ToReadableString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("A9 Vue990 PPCS transport probe");
        sb.AppendLine($"Timestamp: {Timestamp:O}");
        sb.AppendLine($"Host: {Host}");
        sb.AppendLine($"Success: {Success}");

        if (Error is not null)
        {
            sb.AppendLine($"Error: {Error}");
            return sb.ToString();
        }

        if (Status is not null)
        {
            sb.AppendLine($"Status: success={Status.Success} deviceid={Status.DeviceId ?? "<none>"} realdeviceid={Status.RealDeviceId ?? "<none>"} battery={Status.BatteryRate ?? "<none>"}");
        }

        if (Das is not null)
        {
            sb.AppendLine(
                $"DAS: bytes={Das.ByteLength} knownMagic={Das.HasKnownMagic} " +
                $"decoded={Das.HasDecodedPayload} relays={string.Join(", ", Das.DecodedPayload.RelayHosts.DefaultIfEmpty("<none>"))} " +
                $"plaintext={Das.LooksPlainText} endpointCandidates={Das.CandidateIpv4Endpoints.Count}");
        }

        sb.AppendLine($"Transport signal: {HasTransportSignal}");
        foreach (var attempt in Attempts)
        {
            var label = attempt.Protocol == A9Vue990PpcsTransportProtocol.Udp
                ? $"{attempt.TargetKind} {attempt.Host} {attempt.Protocol}/{attempt.Port} {attempt.PayloadName}"
                : $"{attempt.TargetKind} {attempt.Host} {attempt.Protocol}/{attempt.Port}";
            sb.AppendLine(
                $"- {label}: opened={attempt.Opened} bytes={attempt.BytesReceived} " +
                $"remote={attempt.RemoteEndpoint ?? "<none>"} targetRemote={attempt.RemoteMatchesHost} " +
                $"duration={attempt.DurationMs}ms outcome={attempt.Outcome ?? attempt.Error ?? "<none>"}");

            if (!string.IsNullOrWhiteSpace(attempt.PrefixHex))
                sb.AppendLine($"  prefix={attempt.PrefixHex}");
        }

        return sb.ToString();
    }
}

public sealed class A9Vue990PpcsTransportAttempt
{
    public A9Vue990PpcsTransportProtocol Protocol { get; init; }

    public A9Vue990PpcsTransportTargetKind TargetKind { get; init; } =
        A9Vue990PpcsTransportTargetKind.CameraLan;

    public required string Host { get; init; }

    public int Port { get; init; }

    public string? PayloadName { get; init; }

    public bool Opened { get; set; }

    public string? RemoteEndpoint { get; set; }

    public bool RemoteMatchesHost { get; set; }

    public int BytesSent { get; set; }

    public int BytesReceived { get; set; }

    public string? PrefixHex { get; set; }

    public string? PrefixText { get; set; }

    public string? Sha256 { get; set; }

    public long DurationMs { get; set; }

    public string? Outcome { get; set; }

    public string? Error { get; set; }
}

public enum A9Vue990PpcsTransportProtocol
{
    Tcp,
    Udp,
}

public enum A9Vue990PpcsTransportTargetKind
{
    CameraLan,
    DecodedRelay,
}
