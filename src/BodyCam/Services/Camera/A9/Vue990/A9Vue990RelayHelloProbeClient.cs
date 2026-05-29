using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace BodyCam.Services.Camera.A9.Vue990;

public sealed class A9Vue990RelayHelloProbeClient
{
    public async Task<A9Vue990RelayHelloProbeResult> ProbeAsync(
        A9Vue990RelayHelloProbeOptions options,
        CancellationToken ct = default)
    {
        var result = new A9Vue990RelayHelloProbeResult
        {
            Timestamp = DateTimeOffset.Now,
            Host = options.Host,
            RelayPort = options.RelayPort,
        };

        A9Vue990DasServerParameter? das = null;
        if (!string.IsNullOrWhiteSpace(options.ServerParameter))
        {
            if (!A9Vue990DasServerParameter.TryParse(options.ServerParameter, out das, out var dasError) ||
                das is null)
            {
                result.Error = dasError ?? "DAS parse failed.";
                return result;
            }
        }
        else
        {
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
                result.Error = "Status fetch failed; relay hello probe stopped.";
                return result;
            }

            if (!A9Vue990DasServerParameter.TryParse(result.Status.Server, out das, out var dasError) ||
                das is null)
            {
                result.Error = dasError ?? "DAS parse failed.";
                return result;
            }
        }

        result.Das = das;
        var clientId = options.ClientId ?? result.Status?.DeviceId ?? string.Empty;
        var vuid = options.Vuid ?? result.Status?.RealDeviceId ?? string.Empty;
        result.ClientId = clientId;
        result.Vuid = vuid;

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(vuid))
        {
            result.Error = "Client id and VUID are required when status is not fetched.";
            return result;
        }

        var relayHosts = options.RelayHosts.Count > 0
            ? options.RelayHosts
            : das.DecodedPayload.RelayHosts;

        if (relayHosts.Count == 0)
        {
            result.Error = "No relay hosts were available from options or decoded DAS.";
            return result;
        }

        foreach (var relayHost in relayHosts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var relayAddress = await ResolveIpv4Async(relayHost, ct).ConfigureAwait(false);
            if (relayAddress is null)
            {
                result.Attempts.Add(new A9Vue990RelayHelloAttempt
                {
                    Host = relayHost,
                    Port = options.RelayPort,
                    Candidate = "<resolve>",
                    Outcome = "Relay host could not be resolved.",
                });
                continue;
            }

            var candidates = BuildCandidates(das, clientId, vuid, relayAddress, options)
                .Take(options.MaxCandidates)
                .ToArray();
            if (result.Candidates.Count == 0)
            {
                result.Candidates = candidates
                    .Select(candidate => candidate.Describe())
                    .ToArray();
            }

            foreach (var candidate in candidates)
            {
                result.Attempts.Add(await TryCandidateAsync(
                    relayAddress,
                    relayHost,
                    options.RelayPort,
                    candidate,
                    options,
                    ct).ConfigureAwait(false));
            }
        }

        result.Success = true;
        result.HasResponseBytes = result.Attempts.Any(attempt => attempt.BytesReceived > 0);
        return result;
    }

    private static IReadOnlyList<A9Vue990RelayHelloCandidate> BuildCandidates(
        A9Vue990DasServerParameter das,
        string clientId,
        string vuid,
        IPAddress relayAddress,
        A9Vue990RelayHelloProbeOptions options)
    {
        var candidates = new List<A9Vue990RelayHelloCandidate>();
        candidates.AddRange(BuildManagedTcpRelayCandidates(das, clientId, vuid, relayAddress, options.RelayPort));

        candidates.AddRange([
            new("native-tcp-hello-f100-empty", A9Vue990P2pPacketBuilder.BuildTcpHello()),
            new("native-tcpsend-hello-oracle-12-latest", A9Vue990P2pPacketBuilder.BuildTcpSendHelloOracle()),
            new("native-tcpsend-hello-oracle-12-previous", A9Vue990P2pPacketBuilder.BuildTcpSendHelloOraclePrevious()),
            new("native-tcpsend-rlyreq-oracle-64", A9Vue990P2pPacketBuilder.BuildTcpSendRlyReqOracle()),
            new("native-tcpsend-rslgn-oracle-68", A9Vue990P2pPacketBuilder.BuildTcpSendRsLgnOracle()),
            new(
                "native-tcpsend-hello-then-rlyreq",
                A9Vue990P2pPacketBuilder.BuildSequence(
                    A9Vue990P2pPacketBuilder.BuildTcpSendHelloOracle(),
                    A9Vue990P2pPacketBuilder.BuildTcpSendRlyReqOracle())),
            new(
                "native-tcpsend-hello-then-rslgn",
                A9Vue990P2pPacketBuilder.BuildSequence(
                    A9Vue990P2pPacketBuilder.BuildTcpSendHelloOracle(),
                    A9Vue990P2pPacketBuilder.BuildTcpSendRsLgnOracle())),
            new("native-rly-hello-f170-empty", A9Vue990P2pPacketBuilder.BuildRelayHello()),
            new("native-svr-req-f210-empty", A9Vue990P2pPacketBuilder.BuildServerRequest()),
            new(
                "native-tcp-hello-then-svr-req",
                A9Vue990P2pPacketBuilder.BuildSequence(
                    A9Vue990P2pPacketBuilder.BuildTcpHello(),
                    A9Vue990P2pPacketBuilder.BuildServerRequest())),
            new(
                "native-rly-hello-then-svr-req",
                A9Vue990P2pPacketBuilder.BuildSequence(
                    A9Vue990P2pPacketBuilder.BuildRelayHello(),
                    A9Vue990P2pPacketBuilder.BuildServerRequest())),
            new(
                "native-tcp-hello-then-rly-hello",
                A9Vue990P2pPacketBuilder.BuildSequence(
                    A9Vue990P2pPacketBuilder.BuildTcpHello(),
                    A9Vue990P2pPacketBuilder.BuildRelayHello())),
            new("legacy-lansearch-f130-empty", A9Vue990P2pPacketBuilder.BuildHeader(A9Vue990P2pPacketBuilder.LanSearch)),
            new("pppp-rs-login-f150-empty", [0xF1, 0x50, 0x00, 0x00]),
            new("exploratory-tcp-hello-f100-zero-body", [0xF1, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00]),
            new("exploratory-tcp-hello-f100-das-token-body", BuildCommandWithAsciiPayload(A9Vue990P2pPacketBuilder.TcpHello, LastToken(das))),
            new("ascii-client-id-nul", NullTerminatedAscii(clientId)),
            new("ascii-vuid-nul", NullTerminatedAscii(vuid)),
        ]);

        foreach (var payload in options.ExtraPayloads)
        {
            candidates.Add(new A9Vue990RelayHelloCandidate(payload.Name, payload.Bytes));
        }

        return candidates
            .Where(candidate => candidate.Bytes.Length > 0)
            .Where(candidate => candidate.Bytes.Length <= options.MaxRequestBytes)
            .ToArray();
    }

    private static IEnumerable<A9Vue990RelayHelloCandidate> BuildManagedTcpRelayCandidates(
        A9Vue990DasServerParameter das,
        string clientId,
        string vuid,
        IPAddress relayAddress,
        int relayPort)
    {
        var relayNames = RelayNameCandidates(das, clientId).ToArray();
        var addressCandidates = new List<(string Name, byte[] Bytes)>
        {
            ("relay-address", A9Vue990TcpRelayPacketBuilder.BuildSockaddrCs2Network(relayAddress, (ushort)relayPort)),
            ("loopback-address", A9Vue990TcpRelayPacketBuilder.BuildSockaddrCs2Network(IPAddress.Loopback, (ushort)relayPort)),
        };
        addressCandidates.AddRange(LocalIpv4AddressCandidates(relayPort));

        foreach (var relayName in relayNames)
        {
            foreach (var address in addressCandidates)
            {
                var suffix = $"{SanitizeCandidateName(relayName)}-{address.Name}";
                var rlyReq = A9Vue990TcpRelayPacketBuilder.BuildTcpRlyReq(
                    clientId,
                    vuid,
                    relayName,
                    address.Bytes);
                var rsLgn = A9Vue990TcpRelayPacketBuilder.BuildTcpRsLgn(
                    clientId,
                    vuid,
                    relayName,
                    address.Bytes);

                yield return new A9Vue990RelayHelloCandidate($"managed-tcpsend-rlyreq-{suffix}", rlyReq);
                yield return new A9Vue990RelayHelloCandidate($"managed-tcpsend-rslgn-{suffix}", rsLgn);
                yield return new A9Vue990RelayHelloCandidate(
                    $"managed-tcpsend-hello-then-rlyreq-{suffix}",
                    A9Vue990P2pPacketBuilder.BuildSequence(
                        A9Vue990P2pPacketBuilder.BuildTcpSendHelloOracle(),
                        rlyReq));
                yield return new A9Vue990RelayHelloCandidate(
                    $"managed-tcpsend-hello-then-rslgn-{suffix}",
                    A9Vue990P2pPacketBuilder.BuildSequence(
                        A9Vue990P2pPacketBuilder.BuildTcpSendHelloOracle(),
                        rsLgn));
            }
        }
    }

    private static async Task<A9Vue990RelayHelloAttempt> TryCandidateAsync(
        IPAddress relayAddress,
        string relayHost,
        int relayPort,
        A9Vue990RelayHelloCandidate candidate,
        A9Vue990RelayHelloProbeOptions options,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var attempt = new A9Vue990RelayHelloAttempt
        {
            Host = relayHost,
            Address = relayAddress.ToString(),
            Port = relayPort,
            Candidate = candidate.Name,
            BytesSent = candidate.Bytes.Length,
            RequestPrefixHex = ToHex(candidate.Bytes, 96),
        };

        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectTimeout.CancelAfter(options.ConnectTimeout);

            using var tcp = new TcpClient(AddressFamily.InterNetwork);
            await tcp.ConnectAsync(relayAddress, relayPort, connectTimeout.Token).ConfigureAwait(false);
            tcp.NoDelay = true;
            attempt.Opened = true;
            attempt.RemoteEndpoint = tcp.Client.RemoteEndPoint?.ToString();

            await using var stream = tcp.GetStream();
            await stream.WriteAsync(candidate.Bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);

            var bytes = await ReadResponseAsync(stream, options, ct).ConfigureAwait(false);
            ApplyBytes(attempt, bytes);
            await SaveResponseAsync(attempt, bytes, options, ct).ConfigureAwait(false);
            attempt.Outcome = bytes.Length > 0
                ? "TCP relay returned bytes after candidate payload."
                : "TCP relay accepted connection and payload; no bytes returned within read window.";
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
        {
            attempt.Error = $"{ex.GetType().Name}: {ex.Message}";
            attempt.Outcome = ex is OperationCanceledException
                ? "TCP connect/write/read timed out."
                : "TCP relay candidate failed.";
        }
        finally
        {
            attempt.DurationMs = sw.ElapsedMilliseconds;
        }

        return attempt;
    }

    private static async Task<byte[]> ReadResponseAsync(
        NetworkStream stream,
        A9Vue990RelayHelloProbeOptions options,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.ReadTimeout);

        var buffer = new byte[options.MaxResponseBytes];
        using var memory = new MemoryStream();
        var deadline = DateTimeOffset.UtcNow + options.ReadTimeout;

        while (memory.Length < options.MaxResponseBytes && DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var remaining = options.MaxResponseBytes - (int)memory.Length;
                var read = await stream.ReadAsync(
                    buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                    timeout.Token).ConfigureAwait(false);

                if (read == 0)
                    break;

                memory.Write(buffer, 0, read);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return memory.ToArray();
    }

    private static byte[] BuildCommandWithAsciiPayload(ushort command, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var payload = Encoding.ASCII.GetBytes(value);
        var bytes = new byte[4 + payload.Length];
        bytes[0] = (byte)(command >> 8);
        bytes[1] = (byte)command;
        bytes[2] = (byte)(payload.Length >> 8);
        bytes[3] = (byte)payload.Length;
        payload.CopyTo(bytes.AsSpan(4));
        return bytes;
    }

    private static byte[] NullTerminatedAscii(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var text = Encoding.ASCII.GetBytes(value);
        var bytes = new byte[text.Length + 1];
        text.CopyTo(bytes, 0);
        return bytes;
    }

    private static string LastToken(A9Vue990DasServerParameter das)
    {
        return das.DecodedPayload.Tokens.LastOrDefault(token =>
            token.Length > 0 &&
            token.All(value => value is >= '0' and <= '9' or >= 'A' and <= 'F')) ?? string.Empty;
    }

    private static IEnumerable<string> RelayNameCandidates(A9Vue990DasServerParameter das, string clientId)
    {
        var candidates = new List<string>();

        var fourLetterToken = das.DecodedPayload.Tokens.FirstOrDefault(token =>
            token.Length == 4 &&
            token.All(value => value is >= 'A' and <= 'Z'));
        if (!string.IsNullOrWhiteSpace(fourLetterToken))
            candidates.Add(fourLetterToken);

        if (clientId.Length >= 4)
            candidates.Add(clientId[..4]);

        var lastToken = LastToken(das);
        if (!string.IsNullOrWhiteSpace(lastToken))
            candidates.Add(lastToken);

        var relayModeToken = das.DecodedPayload.Tokens.FirstOrDefault(token =>
            token.Contains('+', StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(relayModeToken))
            candidates.Add(relayModeToken);

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.Ordinal)
            .Take(4);
    }

    private static IEnumerable<(string Name, byte[] Bytes)> LocalIpv4AddressCandidates(int relayPort)
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.Address))
            .Where(address => !IPAddress.IsLoopback(address))
            .Distinct()
            .Select(address => (
                $"local-{SanitizeCandidateName(address.ToString())}",
                A9Vue990TcpRelayPacketBuilder.BuildSockaddrCs2Network(address, (ushort)relayPort)));
    }

    private static string SanitizeCandidateName(string value)
    {
        var sanitized = new string(value.Select(ch =>
            char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray());
        while (sanitized.Contains("--", StringComparison.Ordinal))
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);

        return sanitized.Trim('-');
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

    private static void ApplyBytes(A9Vue990RelayHelloAttempt attempt, byte[] bytes)
    {
        attempt.BytesReceived = bytes.Length;
        attempt.ResponsePrefixHex = ToHex(bytes, 160);
        attempt.ResponsePrefixText = ToSafeText(bytes, 160);
        attempt.Sha256 = bytes.Length == 0 ? null : Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static async Task SaveResponseAsync(
        A9Vue990RelayHelloAttempt attempt,
        byte[] bytes,
        A9Vue990RelayHelloProbeOptions options,
        CancellationToken ct)
    {
        if (bytes.Length == 0 || string.IsNullOrWhiteSpace(options.ResponseOutputDirectory))
            return;

        Directory.CreateDirectory(options.ResponseOutputDirectory);
        var fileName = string.Join(
            "-",
            new[]
            {
                "relay-response",
                SanitizeFilePart(attempt.Host),
                SanitizeFilePart(attempt.Candidate),
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
        var path = Path.Combine(options.ResponseOutputDirectory, fileName + ".bin");
        await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
        attempt.SavedResponsePath = path;
    }

    private static string SanitizeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : ch)
            .ToArray();
        var sanitized = new string(chars).Trim('-');
        while (sanitized.Contains("--", StringComparison.Ordinal))
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);

        return sanitized.Length <= 80 ? sanitized : sanitized[..80];
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
}

public sealed class A9Vue990RelayHelloProbeOptions
{
    public string Host { get; init; } = "192.168.168.1";

    public int StatusPort { get; init; } = 81;

    public string Username { get; init; } = "admin";

    public string Password { get; init; } = "888888";

    public string? ClientId { get; init; }

    public string? Vuid { get; init; }

    public string? ServerParameter { get; init; }

    public TimeSpan StatusTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromMilliseconds(1200);

    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromMilliseconds(1200);

    public int RelayPort { get; init; } = 65527;

    public int MaxRequestBytes { get; init; } = 256;

    public int MaxResponseBytes { get; init; } = 4096;

    public int MaxCandidates { get; init; } = int.MaxValue;

    public string? ResponseOutputDirectory { get; init; }

    public IReadOnlyList<string> RelayHosts { get; init; } = [];

    public IReadOnlyList<A9Vue990RelayHelloExtraPayload> ExtraPayloads { get; init; } = [];
}

public sealed class A9Vue990RelayHelloExtraPayload
{
    public required string Name { get; init; }

    public required byte[] Bytes { get; init; }
}

public sealed class A9Vue990RelayHelloProbeResult
{
    public DateTimeOffset Timestamp { get; init; }

    public required string Host { get; init; }

    public int RelayPort { get; init; }

    public bool Success { get; set; }

    public bool HasResponseBytes { get; set; }

    public string? Error { get; set; }

    public string? ClientId { get; set; }

    public string? Vuid { get; set; }

    public A9Vue990StatusResult? Status { get; set; }

    public A9Vue990DasServerParameter? Das { get; set; }

    public IReadOnlyList<A9Vue990RelayHelloCandidateDescription> Candidates { get; set; } = [];

    public List<A9Vue990RelayHelloAttempt> Attempts { get; } = [];

    public string ToReadableString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("A9 Vue990 relay hello probe");
        sb.AppendLine($"Timestamp: {Timestamp:O}");
        sb.AppendLine($"Host: {Host}");
        sb.AppendLine($"Relay port: {RelayPort}");
        sb.AppendLine($"Success: {Success}");

        if (Error is not null)
        {
            sb.AppendLine($"Error: {Error}");
            return sb.ToString();
        }

        if (Status is not null)
        {
            sb.AppendLine($"Status: success={Status.Success} clientId={ClientId ?? "<none>"} vuid={Vuid ?? "<none>"} battery={Status.BatteryRate ?? "<none>"}");
        }

        if (Das is not null)
        {
            sb.AppendLine(
                $"DAS: decoded={Das.HasDecodedPayload} relays={string.Join(", ", Das.DecodedPayload.RelayHosts.DefaultIfEmpty("<none>"))} " +
                $"tokens={string.Join(" | ", Das.DecodedPayload.Tokens)}");
        }

        sb.AppendLine($"Candidates: {Candidates.Count}");
        foreach (var candidate in Candidates)
        {
            sb.AppendLine($"- {candidate.Name}: bytes={candidate.Bytes} prefix={candidate.PrefixHex}");
        }

        sb.AppendLine($"Response bytes: {HasResponseBytes}");
        foreach (var attempt in Attempts)
        {
            sb.AppendLine(
                $"- {attempt.Host} ({attempt.Address ?? "<unresolved>"}) tcp/{attempt.Port} {attempt.Candidate}: " +
                $"opened={attempt.Opened} sent={attempt.BytesSent} received={attempt.BytesReceived} " +
                $"duration={attempt.DurationMs}ms outcome={attempt.Outcome ?? attempt.Error ?? "<none>"}");

            if (!string.IsNullOrWhiteSpace(attempt.ResponsePrefixHex))
                sb.AppendLine($"  response={attempt.ResponsePrefixHex}");
        }

        return sb.ToString();
    }
}

public sealed class A9Vue990RelayHelloAttempt
{
    public required string Host { get; init; }

    public string? Address { get; init; }

    public int Port { get; init; }

    public required string Candidate { get; init; }

    public bool Opened { get; set; }

    public string? RemoteEndpoint { get; set; }

    public int BytesSent { get; set; }

    public int BytesReceived { get; set; }

    public string? RequestPrefixHex { get; set; }

    public string? ResponsePrefixHex { get; set; }

    public string? ResponsePrefixText { get; set; }

    public string? Sha256 { get; set; }

    public string? SavedResponsePath { get; set; }

    public long DurationMs { get; set; }

    public string? Outcome { get; set; }

    public string? Error { get; set; }
}

public sealed record A9Vue990RelayHelloCandidateDescription(string Name, int Bytes, string PrefixHex);

internal sealed record A9Vue990RelayHelloCandidate(string Name, byte[] Bytes)
{
    public A9Vue990RelayHelloCandidateDescription Describe()
    {
        var prefix = Bytes.Length == 0
            ? string.Empty
            : Convert.ToHexString(Bytes.AsSpan(0, Math.Min(Bytes.Length, 48)));
        return new A9Vue990RelayHelloCandidateDescription(Name, Bytes.Length, prefix);
    }
}
