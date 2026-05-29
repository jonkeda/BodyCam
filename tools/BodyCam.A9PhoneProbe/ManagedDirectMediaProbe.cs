using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using BodyCam.Services.Camera.A9.Vue990;

namespace BodyCam.A9PhoneProbe;

internal sealed class ManagedDirectMediaProbe
{
    private const int DefaultFramesPerSecond = 2;
    private const string DefaultClientId = "BKGD00000100FMQLN";
    private const string DefaultVuid = "BK0025644WBPD";

    private static readonly int?[] PpcsLocalPorts = [null, 32108, 65529, 65531];
    private static readonly int[] PpcsRemotePorts = [32108, 32100, 20190, 65529, 65531];

    private static readonly byte[][] Hlp2pDirectLegacyPreambles =
    [
        Convert.FromHexString("00E876667C78B84C64CB4B94C90D982713"),
        Convert.FromHexString("009B2FA5823C60C29DC781071D1A12F134"),
    ];

    private static readonly string[] HttpPaths =
    [
        "/get_status.cgi",
        "/get_status.cgi?loginuse=admin&loginpas=888888",
        "/livestream.cgi?streamid=10&substream=0&",
        "/livestream.cgi?streamid=10&substream=1&",
        "/livestream.cgi?streamid=11&substream=0&",
        "/videostream.cgi?streamid=10&substream=0&",
        "/snapshot.cgi",
        "/snapshot.jpg",
        "/tmpfs/snap.jpg",
        "/tmpfs/auto.jpg",
        "/image.jpg",
        "/mjpeg",
        "/mjpeg.cgi",
        "/mjpegstream.cgi",
        "/mjpg/video.mjpg",
        "/video.mjpg",
        "/video.mjpeg",
        "/live",
        "/stream",
        "/stream.cgi",
        "/live_stream.cgi",
        "/webcapture.jpg?command=snap&channel=1",
        "/cgi-bin/snapshot.cgi",
        "/axis-cgi/mjpg/video.cgi",
        "/axis-cgi/jpg/image.cgi",
        "/nphMotionJpeg",
    ];

    private static readonly UdpProbe[] UdpProbes =
    [
        new("PPPP LanSearch", 32108, A9Vue990PpcsPacket.BuildLanSearch().ToArray()),
        new("PPPP LanSearch XOR1", 32108, A9Vue990PpcsEncryptionCodec.Xor1Encode(A9Vue990PpcsPacket.BuildLanSearch().ToArray())),
        new("PPPP LanSearchExt", 32108, A9Vue990PpcsPacket.Build(A9Vue990PpcsPacketType.LanSearchExtended).ToArray()),
        new("PPPP LanSearchExt XOR1", 32108, A9Vue990PpcsEncryptionCodec.Xor1Encode(A9Vue990PpcsPacket.Build(A9Vue990PpcsPacketType.LanSearchExtended).ToArray())),
        new("SHIX/A9 seed", 32108, Convert.FromHexString("2CBA5F5D")),
        new("VStarcam/XQP2P seed", 32108, Convert.FromHexString("2CD46006")),
        new("JSON discover", 32108, Encoding.ASCII.GetBytes("{\"cmd\":\"discover\"}")),
        new("PPPP LanSearch 20190", 20190, A9Vue990PpcsPacket.BuildLanSearch().ToArray()),
        new("PPPP LanSearch XOR1 20190", 20190, A9Vue990PpcsEncryptionCodec.Xor1Encode(A9Vue990PpcsPacket.BuildLanSearch().ToArray())),
        new("VStarcam/XQP2P seed 20190", 20190, Convert.FromHexString("2CD46006")),
        new("PPPP LanSearch 65531", 65531, A9Vue990PpcsPacket.BuildLanSearch().ToArray()),
        new("PPPP LanSearch XOR1 65531", 65531, A9Vue990PpcsEncryptionCodec.Xor1Encode(A9Vue990PpcsPacket.BuildLanSearch().ToArray())),
        new("VStarcam/XQP2P seed 65531", 65531, Convert.FromHexString("2CD46006")),
        new("PPPP LanSearch 65529", 65529, A9Vue990PpcsPacket.BuildLanSearch().ToArray()),
        new("PPPP LanSearch XOR1 65529", 65529, A9Vue990PpcsEncryptionCodec.Xor1Encode(A9Vue990PpcsPacket.BuildLanSearch().ToArray())),
        new("VStarcam/XQP2P seed 65529", 65529, Convert.FromHexString("2CD46006")),
    ];

    public async Task<string> RunLanHoleOnlyAsync(
        string host,
        string filesDir,
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        var lines = new ProgressLineCollection(progress);
        lines.Add("Managed C# HLP2P LAN-hole probe:");
        lines.Add($"- host: {host}");

        var captureDir = Path.Combine(
            string.IsNullOrWhiteSpace(filesDir) ? "/data/local/tmp" : filesDir,
            "captures",
            "phase-44-managed-lan-hole",
            DateTimeOffset.Now.ToString("yyyy-MM-dd-HHmmss"));
        Directory.CreateDirectory(captureDir);
        lines.Add($"- captureDir: {captureDir}");

        var localIps = GetLocalIpv4Addresses();
        lines.Add($"- local IPv4: {string.Join(",", localIps)}");

        await ProbeManagedLanHoleAsync(host, localIps, captureDir, lines, ct).ConfigureAwait(false);
        lines.Add("- managed-lan-hole summary: complete");
        return string.Join('\n', lines);
    }

    public async Task<string> RunAsync(
        string host,
        string filesDir,
        bool captureImage,
        bool captureVideo,
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        var lines = new ProgressLineCollection(progress);
        lines.Add("Managed direct C# local stream probe:");
        lines.Add($"- host: {host}");
        lines.Add($"- captureImage: {captureImage}");
        lines.Add($"- captureVideo: {captureVideo}");

        var captureDir = Path.Combine(
            string.IsNullOrWhiteSpace(filesDir) ? "/data/local/tmp" : filesDir,
            "captures",
            "phase-33",
            DateTimeOffset.Now.ToString("yyyy-MM-dd-HHmmss"));
        Directory.CreateDirectory(captureDir);
        lines.Add($"- captureDir: {captureDir}");

        var localIps = GetLocalIpv4Addresses();
        lines.Add($"- local IPv4: {string.Join(",", localIps)}");

        var openPorts = await ProbeTcpPortsAsync(host, lines, ct).ConfigureAwait(false);
        var allFrames = await ProbeHttpAsync(host, captureDir, lines, ct).ConfigureAwait(false);
        await ProbeUdpAsync(host, localIps, captureDir, lines, ct).ConfigureAwait(false);
        await ProbeManagedLanHoleAsync(host, localIps, captureDir, lines, ct).ConfigureAwait(false);
        allFrames.AddRange(await ProbeHlp2pDirectScopedControlAsync(host, captureDir, localIps, lines, ct).ConfigureAwait(false));
        allFrames.AddRange(await ProbeClassicPpcsStreamAsync(host, captureDir, localIps, lines, ct).ConfigureAwait(false));
        if (allFrames.Count == 0)
            await ProbeRelaySessionAsync(host, captureDir, lines, ct).ConfigureAwait(false);

        lines.Add("- managed-direct summary:");
        lines.Add($"  openTcpPorts={string.Join(",", openPorts)}");
        lines.Add($"  jpegFrames={allFrames.Count}");

        if (captureImage)
            SaveFirstImage(captureDir, allFrames, lines);

        if (captureVideo)
            SaveMjpegAvi(captureDir, allFrames, lines);

        return string.Join('\n', lines);
    }

    private static async Task<IReadOnlyList<int>> ProbeTcpPortsAsync(
        string host,
        ICollection<string> lines,
        CancellationToken ct)
    {
        int[] ports =
        [
            21, 23, 80, 81, 82, 83, 88, 443, 554, 1935,
            5000, 5001, 5002, 5544, 6123, 7070, 7447,
            8000, 8001, 8080, 8081, 8082, 8088, 8090,
            8554, 8555, 8899, 9000, 9999, 10000, 10080,
            10554, 15203, 1883, 20190, 32108, 34567, 37777,
            49152, 65527, 65531,
        ];
        var open = new List<int>();

        lines.Add("- TCP local ports:");
        foreach (var port in ports)
        {
            if (await CanConnectTcpAsync(host, port, TimeSpan.FromMilliseconds(650), ct).ConfigureAwait(false))
            {
                open.Add(port);
                lines.Add($"  OPEN tcp/{port}");
            }
        }

        if (open.Count == 0)
            lines.Add("  no open TCP ports in tested set");

        return open;
    }

    private static async Task<List<CapturedJpegFrame>> ProbeHttpAsync(
        string host,
        string captureDir,
        ICollection<string> lines,
        CancellationToken ct)
    {
        var frames = new List<CapturedJpegFrame>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(7),
        };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BodyCam-A9PhoneProbe", "1.0"));

        lines.Add("- HTTP/CGI local media:");
        foreach (var path in BuildHttpPathVariants().Where(IsFastHttpStatusProbe))
        {
            var endpoint = BuildHttpEndpoint(host, 81, path);
            if (!seen.Add(endpoint.AbsoluteUri))
                continue;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                using var response = await http.GetAsync(
                        endpoint,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token)
                    .ConfigureAwait(false);
                var bytes = await ReadBoundedAsync(
                        response.Content,
                        maxBytes: 512 * 1024,
                        readDuration: TimeSpan.FromSeconds(2),
                        timeout.Token)
                    .ConfigureAwait(false);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                var endpointFrames = ExtractJpegFrames(bytes);
                var videoLike = LooksLikeVideo(bytes, contentType);
                var interesting = response.IsSuccessStatusCode ||
                                  bytes.Length > 0 ||
                                  endpointFrames.Count > 0 ||
                                  videoLike;

                if (!interesting)
                    continue;

                lines.Add(
                    $"  HTTP {(int)response.StatusCode} {endpoint.PathAndQuery} bytes={bytes.Length} " +
                    $"type={contentType} jpeg={endpointFrames.Count} videoLike={videoLike} prefix={ToHex(bytes, 24)}");

                if (LooksLikeText(bytes))
                    lines.Add($"    text={ToSafeText(bytes, 160)}");

                if (endpointFrames.Count > 0)
                {
                    for (var i = 0; i < endpointFrames.Count; i++)
                    {
                        var frame = endpointFrames[i];
                        var local = Path.Combine(captureDir, $"http-frame-{frames.Count:000}.jpg");
                        await File.WriteAllBytesAsync(local, frame.Bytes, timeout.Token).ConfigureAwait(false);
                        frames.Add(frame with { LocalPath = local });
                        lines.Add(
                            $"    savedFrame[{frames.Count - 1}] path={local} bytes={frame.Bytes.Length} " +
                            $"dimensions={frame.Width}x{frame.Height} sha256={frame.Sha256}");
                    }
                }
                else if (videoLike && bytes.Length > 0)
                {
                    var rawPath = Path.Combine(captureDir, $"http-video-sample-{frames.Count:000}.bin");
                    await File.WriteAllBytesAsync(rawPath, bytes, timeout.Token).ConfigureAwait(false);
                    lines.Add($"    savedVideoLikeSample={rawPath}");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or WebException or IOException)
            {
                if (path.Contains("get_status.cgi", StringComparison.OrdinalIgnoreCase))
                    lines.Add($"  HTTP error {endpoint.PathAndQuery}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (frames.Count == 0)
            lines.Add("  no direct HTTP JPEG/MJPEG/H264 media found");

        return frames;
    }

    private static async Task ProbeUdpAsync(
        string host,
        IReadOnlyCollection<string> localIps,
        string captureDir,
        ICollection<string> lines,
        CancellationToken ct)
    {
        var targets = new[]
        {
            host,
            "192.168.168.255",
            "255.255.255.255",
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        lines.Add("- UDP local discovery:");
        await ProbeUdpSocketAsync(null, targets, localIps, captureDir, lines, ct).ConfigureAwait(false);
        await ProbeUdpSocketAsync(32108, targets, localIps, captureDir, lines, ct).ConfigureAwait(false);
        await ProbeUdpSocketAsync(65529, targets, localIps, captureDir, lines, ct).ConfigureAwait(false);
    }

    private static async Task ProbeUdpSocketAsync(
        int? localPort,
        IReadOnlyList<string> targets,
        IReadOnlyCollection<string> localIps,
        string captureDir,
        ICollection<string> lines,
        CancellationToken ct)
    {
        using var udp = CreateUdpClient(localPort);

        lines.Add(localPort is null
            ? "  socket=ephemeral"
            : $"  socket=fixed:{localPort.Value}");
        lines.Add($"    local={udp.Client.LocalEndPoint}");

        foreach (var probe in UdpProbes)
        {
            foreach (var target in targets)
            {
                if (!IPAddress.TryParse(target, out var ip))
                    continue;

                try
                {
                    await udp.SendAsync(probe.Payload, new IPEndPoint(ip, probe.Port), ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    lines.Add($"    send failed {probe.Name} {target}:{probe.Port}: {ex.Message}");
                }
            }
        }

        var responses = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var response = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                responses++;
                var address = response.RemoteEndPoint.Address.ToString();
                var origin = localIps.Contains(address) ? "self-echo" : "remote";
                var savedRaw = origin == "remote"
                    ? SaveRawPacket(captureDir, $"udp-discovery-rx-{responses:000}", response.RemoteEndPoint, response.Buffer)
                    : null;
                var packetInfo = A9Vue990PpcsPacket.TryDecode(
                    response.Buffer,
                    out var encryption,
                    out var packet)
                    ? $" ppcs={packet.Type}/enc={encryption}/payload={packet.Payload.Length}"
                    : string.Empty;

                if (packet.TryReadPunchDeviceId(out var deviceId))
                    packetInfo += $" deviceId={deviceId}";

                lines.Add(
                    $"    response {origin} from {response.RemoteEndPoint} bytes={response.Buffer.Length} " +
                    $"prefix={ToHex(response.Buffer, 48)}{packetInfo} text={ToSafeText(response.Buffer, 80)}" +
                    (savedRaw is null ? string.Empty : $" savedRaw={savedRaw}"));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                lines.Add($"    receive failed: {ex.Message}");
                break;
            }
        }

        if (responses == 0)
            lines.Add("    no UDP responses");
    }

    private static async Task ProbeManagedLanHoleAsync(
        string host,
        IReadOnlyCollection<string> localIps,
        string captureDir,
        ICollection<string> lines,
        CancellationToken ct)
    {
        lines.Add("- Managed C# HLP2P LAN-hole opener:");
        if (!IPAddress.TryParse(host, out var hostAddress) ||
            hostAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            lines.Add("  skipped: host is not an IPv4 address");
            return;
        }

        var localAddress = SelectManagedLanHoleLocalAddress(localIps);
        if (localAddress is null)
        {
            lines.Add("  skipped: no usable IPv4 phone Wi-Fi address found");
            return;
        }

        var statusBody = await TryFetchStatusBodyAsync(host, lines, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(statusBody))
        {
            lines.Add("  skipped: status body was empty, so DAS state is unavailable");
            return;
        }

        var clientId = ExtractJavaScriptStringVar(statusBody, "deviceid") ?? DefaultClientId;
        var vuid = ExtractJavaScriptStringVar(statusBody, "realdeviceid") ?? DefaultVuid;
        var server = ExtractJavaScriptStringVar(statusBody, "server") ?? string.Empty;
        lines.Add($"  status identity clientId={clientId} vuid={vuid} serverLen={server.Length}");

        if (!A9Vue990DasServerParameter.TryParse(server, out var das, out var dasError) || das is null)
        {
            lines.Add($"  skipped: DAS parse failed: {dasError}");
            return;
        }

        foreach (var requestedLocalPort in new int?[] { 65531, 65529, null })
        {
            using var udp = CreateUdpClient(requestedLocalPort);
            var bound = udp.Client.LocalEndPoint as IPEndPoint;
            if (bound is null)
            {
                lines.Add("  skipped socket: missing bound local endpoint");
                continue;
            }

            var stateEndpoint = new IPEndPoint(localAddress, bound.Port);
            if (!A9Vue990ConnectByServerState.TryCreate(
                    das,
                    clientId,
                    vuid,
                    stateEndpoint,
                    out var state,
                    out var stateError) ||
                state is null)
            {
                lines.Add($"  skipped socket={bound}: state build failed: {stateError}");
                continue;
            }

            var socketName = requestedLocalPort is null ? "ephemeral" : $"fixed:{requestedLocalPort.Value}";
            lines.Add(
                $"  socket={socketName} bound={bound} advertised={stateEndpoint} " +
                $"selector={state.Selector} connectText={state.CandidateDasConnectText}");

            var targets = BuildManagedLanHoleTargets(hostAddress, state.RelayHosts);
            var packets = state.BuildNativeClientSessionSetupPackets().ToArray();
            var alivePackets = state.BuildNativeAlivePackets();
            foreach (var packet in packets)
                lines.Add($"    tx-native-session-template {packet.Name} bytes={packet.Bytes.Length} hex={ToHex(packet.Bytes, 48)}");
            foreach (var packet in alivePackets)
                lines.Add($"    tx-native-alive-template {packet.Name} bytes={packet.Bytes.Length} hex={ToHex(packet.Bytes, 16)}");

            var sends = 0;
            foreach (var target in targets)
            {
                foreach (var packet in packets)
                {
                    try
                    {
                        await udp.SendAsync(packet.Bytes, target, ct).ConfigureAwait(false);
                        sends++;
                    }
                    catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                    {
                        lines.Add($"    send failed {packet.Name} to {target}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            lines.Add($"    sent native session-setup burst packets={sends} targets={targets.Count}");
            await ReceiveManagedLanHoleResponsesAsync(udp, localIps, captureDir, lines, ct).ConfigureAwait(false);
        }
    }

    private static async Task ReceiveManagedLanHoleResponsesAsync(
        UdpClient udp,
        IReadOnlyCollection<string> localIps,
        string captureDir,
        ICollection<string> lines,
        CancellationToken ct)
    {
        var responses = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var response = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                responses++;

                var address = response.RemoteEndPoint.Address.ToString();
                var origin = localIps.Contains(address) ? "self-echo" : "remote";
                var savedRaw = origin == "remote"
                    ? SaveRawPacket(captureDir, $"managed-lan-hole-rx-{responses:000}", response.RemoteEndPoint, response.Buffer)
                    : null;
                var header = TryReadHlp2pHeader(response.Buffer, out var command, out var payloadLength)
                    ? $" hlp2p=0x{command:X4}/payload={payloadLength}"
                    : string.Empty;

                lines.Add(
                    $"    response {origin} from {response.RemoteEndPoint} bytes={response.Buffer.Length} " +
                    $"prefix={ToHex(response.Buffer, 64)}{header}" +
                    (savedRaw is null ? string.Empty : $" savedRaw={savedRaw}"));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                lines.Add($"    receive failed: {ex.Message}");
                break;
            }
        }

        if (responses == 0)
            lines.Add("    no focused LAN-hole responses");
    }

    private static async Task<List<CapturedJpegFrame>> ProbeHlp2pDirectScopedControlAsync(
        string host,
        string captureDir,
        IReadOnlyCollection<string> localIps,
        ICollection<string> lines,
        CancellationToken ct)
    {
        var frames = new List<CapturedJpegFrame>();
        lines.Add("- Managed C# compact HLP2P direct transport replay:");
        lines.Add("  postHoleControlSource=phase-46 socket-hook scoped vectors; used until control payload derivation is implemented");

        if (!IPAddress.TryParse(host, out var hostAddress) ||
            hostAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            lines.Add("  skipped: host is not an IPv4 address");
            return frames;
        }

        using var udp = CreateUdpClient(null);
        var bound = udp.Client.LocalEndPoint as IPEndPoint;
        lines.Add($"  socket=ephemeral bound={bound?.ToString() ?? "<unknown>"}");

        var targets = BuildHlp2pDirectLanHoleTargets(hostAddress);
        A9Vue990Hlp2pLanHoleResponse? lanHoleResponse = null;
        A9Vue990Hlp2pLanHoleSeed? activeSeed = null;
        IPEndPoint? sessionRemote = null;
        foreach (var attempt in BuildHlp2pDirectLanHoleAttempts())
        {
            await SendHlp2pDirectLegacyPreamblesAsync(hostAddress, attempt.Name, lines, ct).ConfigureAwait(false);

            var probe = A9Vue990Hlp2pDirectPacket.BuildLanHoleProbe(attempt.Seed);
            foreach (var target in targets)
            {
                try
                {
                    await udp.SendAsync(probe, target, ct).ConfigureAwait(false);
                    await Task.Delay(35, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    lines.Add($"  lan-hole probe send failed attempt={attempt.Name} to {target}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            lines.Add(
                $"  sent compact lan-hole attempt={attempt.Name} bytes={probe.Length} " +
                $"token={ToHex(attempt.Seed.SessionToken, 4)} uid=0x{attempt.Seed.UidLittleEndian:X8} targets={targets.Count}");

            using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            responseTimeout.CancelAfter(TimeSpan.FromSeconds(3));
            while (!responseTimeout.IsCancellationRequested)
            {
                try
                {
                    var received = await udp.ReceiveAsync(responseTimeout.Token).ConfigureAwait(false);
                    var address = received.RemoteEndPoint.Address.ToString();
                    if (localIps.Contains(address))
                        continue;

                    var savedRaw = SaveRawPacket(captureDir, "hlp2p-direct-lanhole-rx", received.RemoteEndPoint, received.Buffer);
                    lines.Add(
                        $"  lan-hole rx attempt={attempt.Name} from {received.RemoteEndPoint} bytes={received.Buffer.Length} " +
                        $"prefix={ToHex(received.Buffer, 32)} savedRaw={savedRaw}");

                    if (!A9Vue990Hlp2pDirectPacket.TryParseLanHoleResponse(received.Buffer, out var parsed))
                        continue;

                    lanHoleResponse = parsed;
                    activeSeed = attempt.Seed;
                    sessionRemote = received.RemoteEndPoint;
                    lines.Add(
                        $"  parsed lan-hole response attempt={attempt.Name} aid=0x{parsed.AidLittleEndian:X8} " +
                        $"status=0x{parsed.Status:X2}{parsed.StatusDetail:X2} token={ToHex(parsed.SessionToken, 4)}");
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    lines.Add($"  lan-hole receive failed attempt={attempt.Name}: {ex.GetType().Name}: {ex.Message}");
                    break;
                }
            }

            if (lanHoleResponse is not null)
                break;
            else
            {
                lines.Add($"  no compact lan-hole response for attempt={attempt.Name}");
            }
        }

        if (lanHoleResponse is null || activeSeed is null || sessionRemote is null)
        {
            lines.Add("  no compact lan-hole response; direct post-hole path cannot continue");
            return frames;
        }

        var ack = A9Vue990Hlp2pDirectPacket.BuildLanHoleAck(lanHoleResponse, activeSeed.UidLittleEndian);
        await udp.SendAsync(ack, sessionRemote, ct).ConfigureAwait(false);
        lines.Add($"  sent lan-hole ack remote={sessionRemote} bytes={ack.Length} hex={ToHex(ack, ack.Length)}");

        var ready = false;
        using (var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            readyTimeout.CancelAfter(TimeSpan.FromSeconds(4));
            while (!readyTimeout.IsCancellationRequested)
            {
                try
                {
                    var received = await udp.ReceiveAsync(readyTimeout.Token).ConfigureAwait(false);
                    if (localIps.Contains(received.RemoteEndPoint.Address.ToString()))
                        continue;

                    var savedRaw = SaveRawPacket(captureDir, "hlp2p-direct-ready-rx", received.RemoteEndPoint, received.Buffer);
                    lines.Add(
                        $"  ready rx from {received.RemoteEndPoint} bytes={received.Buffer.Length} " +
                        $"prefix={ToHex(received.Buffer, 32)} savedRaw={savedRaw}");

                    if (A9Vue990Hlp2pDirectPacket.TryParseLanHoleReady(received.Buffer, out var parsedReady))
                    {
                        ready = true;
                        sessionRemote = received.RemoteEndPoint;
                        lines.Add($"  parsed lan-hole ready aid=0x{parsedReady.AidLittleEndian:X8}");
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    lines.Add($"  ready receive failed: {ex.GetType().Name}: {ex.Message}");
                    break;
                }
            }
        }

        if (!ready)
        {
            lines.Add("  no compact lan-hole ready packet; direct path stopped before 0x0D transport");
            return frames;
        }

        await CompleteHlp2pDirectAliveHandshakeAsync(udp, sessionRemote, localIps, captureDir, lines, ct).ConfigureAwait(false);

        lines.Add($"  post-hole control provider: {A9Vue990PostHoleControlProvider.Scope}");
        await SendHlp2pDirectPostHoleControlAsync(udp, sessionRemote, A9Vue990PostHoleControlKind.InitialShortRequest, "native-paced initial", lines, ct)
            .ConfigureAwait(false);
        await SendHlp2pDirectPostHoleControlAsync(udp, sessionRemote, A9Vue990PostHoleControlKind.InitialLongRequest, "native-paced initial", lines, ct)
            .ConfigureAwait(false);
        await Task.Delay(260, ct).ConfigureAwait(false);
        await SendHlp2pDirectPostHoleControlAsync(udp, sessionRemote, A9Vue990PostHoleControlKind.MediaShortRequest, "native-paced after first ack window", lines, ct)
            .ConfigureAwait(false);
        await SendHlp2pDirectPostHoleControlAsync(udp, sessionRemote, A9Vue990PostHoleControlKind.MediaLongRequest, "native-paced after first ack window", lines, ct)
            .ConfigureAwait(false);
        await Task.Delay(260, ct).ConfigureAwait(false);
        await SendHlp2pDirectPostHoleControlAsync(udp, sessionRemote, A9Vue990PostHoleControlKind.InitialLongRequest, "native-paced repeat before large response", lines, ct)
            .ConfigureAwait(false);
        await Task.Delay(120, ct).ConfigureAwait(false);

        var assembler = new A9Vue990VideoFrameAssembler();
        using var channelBytes = new MemoryStream();
        var remotePacketCount = 0;
        var streamStarted = false;
        var repeatedControl3AfterLargeResponse = false;
        using var streamTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        streamTimeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (!streamTimeout.IsCancellationRequested && frames.Count < 12)
        {
            try
            {
                var received = await udp.ReceiveAsync(streamTimeout.Token).ConfigureAwait(false);
                if (localIps.Contains(received.RemoteEndPoint.Address.ToString()))
                    continue;

                remotePacketCount++;
                if (remotePacketCount <= 24)
                {
                    var savedRaw = SaveRawPacket(captureDir, $"hlp2p-direct-stream-rx-{remotePacketCount:000}", received.RemoteEndPoint, received.Buffer);
                    lines.Add(
                        $"  stream rx[{remotePacketCount}] from {received.RemoteEndPoint} bytes={received.Buffer.Length} " +
                        $"prefix={ToHex(received.Buffer, 48)} savedRaw={savedRaw}");
                }

                if (A9Vue990Hlp2pDirectPacket.IsAliveProbe(received.Buffer))
                {
                    await udp.SendAsync(A9Vue990Hlp2pDirectPacket.AliveAck.ToArray(), sessionRemote, streamTimeout.Token)
                        .ConfigureAwait(false);
                    lines.Add("  sent compact alive ack 0C");
                    continue;
                }

                if (A9Vue990Hlp2pDirectPacket.IsAliveAck(received.Buffer))
                    continue;

                if (!A9Vue990Hlp2pDirectPacket.TryParseDirectDataPacket(received.Buffer, out var packet))
                    continue;

                var ackSequence = unchecked((ushort)(packet.Sequence + 1));
                await udp.SendAsync(
                        A9Vue990Hlp2pDirectPacket.BuildDirectAck(ackSequence, packet),
                        sessionRemote,
                        streamTimeout.Token)
                    .ConfigureAwait(false);
                lines.Add(
                    $"  sent direct ack seq=0x{ackSequence:X4} for rxSeq=0x{packet.Sequence:X4} " +
                    $"message=0x{packet.MessageId:X4} ackLen=0x{Math.Max(0, packet.TailLength - 8):X4}");

                if (!repeatedControl3AfterLargeResponse &&
                    packet.Operation == A9Vue990Hlp2pDirectPacket.DirectCommandOperation &&
                    packet.Sequence == 0 &&
                    packet.TailLength == 0x0339)
                {
                    repeatedControl3AfterLargeResponse = true;
                    await Task.Delay(220, streamTimeout.Token).ConfigureAwait(false);
                    var repeat = A9Vue990PostHoleControlProvider.GetControl(A9Vue990PostHoleControlKind.MediaLongRequest);
                    await udp.SendAsync(repeat.Bytes, sessionRemote, streamTimeout.Token).ConfigureAwait(false);
                    lines.Add($"  resent post-hole control[{repeat.Index}] name={repeat.Name} after large response bytes={repeat.Length} prefix={ToHex(repeat.Bytes, 32)}");
                }

                if (packet.Operation != A9Vue990Hlp2pDirectPacket.DirectDataOperation)
                    continue;

                if (packet.Payload.AsSpan().StartsWith(A9Vue990VideoFrameAssembler.VideoChunkMarker))
                    streamStarted = true;

                if (!streamStarted)
                    continue;

                channelBytes.Write(packet.Payload, 0, packet.Payload.Length);
                foreach (var rawFrame in assembler.AddVideoDrwChunk(packet.MessageId, packet.Payload))
                {
                    foreach (var frame in ExtractJpegFrames(rawFrame))
                    {
                        await SaveHlp2pDirectFrameAsync(captureDir, frames, frame, streamTimeout.Token).ConfigureAwait(false);
                        lines.Add(
                            $"  savedHlp2pDirectFrame[{frames.Count - 1}] path={frames[^1].LocalPath} " +
                            $"bytes={frames[^1].Bytes.Length} dimensions={frames[^1].Width}x{frames[^1].Height} " +
                            $"sha256={frames[^1].Sha256}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or IOException)
            {
                lines.Add($"  stream receive failed: {ex.GetType().Name}: {ex.Message}");
                break;
            }
        }

        if (channelBytes.Length > 0)
        {
            var channelPath = Path.Combine(captureDir, "managed-hlp2p-direct-channel.bin");
            await File.WriteAllBytesAsync(channelPath, channelBytes.ToArray(), ct).ConfigureAwait(false);
            lines.Add($"  savedDirectChannel path={channelPath} bytes={channelBytes.Length}");

            if (frames.Count == 0)
            {
                foreach (var frame in ExtractJpegFrames(channelBytes.ToArray()))
                {
                    await SaveHlp2pDirectFrameAsync(captureDir, frames, frame, ct).ConfigureAwait(false);
                    lines.Add(
                        $"  savedHlp2pDirectFrame[{frames.Count - 1}] path={frames[^1].LocalPath} " +
                        $"bytes={frames[^1].Bytes.Length} dimensions={frames[^1].Width}x{frames[^1].Height} " +
                        $"sha256={frames[^1].Sha256}");
                }
            }
        }

        if (frames.Count == 0)
        {
            lines.Add(
                remotePacketCount == 0
                    ? "  no post-replay remote packets; likely 0x0D control data is session-bound"
                    : "  no JPEG frames from managed compact replay");
        }

        return frames;
    }

    private static async Task<List<CapturedJpegFrame>> ProbeClassicPpcsStreamAsync(
        string host,
        string captureDir,
        IReadOnlyCollection<string> localIps,
        ICollection<string> lines,
        CancellationToken ct)
    {
        var frames = new List<CapturedJpegFrame>();
        lines.Add("- Managed C# classic PPPP stream attempt:");

        if (!IPAddress.TryParse(host, out var hostAddress))
        {
            lines.Add("  skipped: host is not an IPv4 address");
            return frames;
        }

        UdpClient? sessionUdp = null;
        ReceivedPpcsPacket? punch = null;
        IPEndPoint? remote = null;
        var targets = BuildPpcsTargets(hostAddress);
        foreach (var localPort in PpcsLocalPorts)
        {
            UdpClient? candidateUdp = null;
            try
            {
                candidateUdp = CreateUdpClient(localPort);
                var socketName = localPort is null ? "ephemeral" : $"fixed-{localPort.Value}";
                lines.Add($"  session socket={socketName} local={candidateUdp.Client.LocalEndPoint}");

                foreach (var remotePort in PpcsRemotePorts)
                {
                    foreach (var target in targets)
                    {
                        var candidateRemote = new IPEndPoint(target, remotePort);
                        punch = await TryPpcsHandshakeAsync(
                                candidateUdp,
                                candidateRemote,
                                localIps,
                                captureDir,
                                socketName,
                                lines,
                                ct)
                            .ConfigureAwait(false);
                        if (punch is null)
                            continue;

                        sessionUdp = candidateUdp;
                        remote = punch.RemoteEndPoint;
                        lines.Add($"  selected session remote={remote} via target={candidateRemote}");
                        break;
                    }

                    if (punch is not null)
                        break;
                }
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                lines.Add($"  session socket failed localPort={localPort?.ToString() ?? "ephemeral"}: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (!ReferenceEquals(candidateUdp, sessionUdp))
                    candidateUdp?.Dispose();
            }

            if (punch is not null)
                break;
        }

        if (punch is null || sessionUdp is null || remote is null)
        {
            lines.Add("  no remote PunchPkt/P2pReady response; classic PPPP stream path cannot start");
            return frames;
        }

        var udp = sessionUdp;
        if (punch.Packet.Type == A9Vue990PpcsPacketType.PunchPacket)
        {
            var p2pReady = A9Vue990PpcsPacket.BuildP2pReady(punch.Packet.Payload).ToArray();
            await udp.SendAsync(p2pReady, remote, ct).ConfigureAwait(false);
            lines.Add($"  sent P2pReady echo bytes={p2pReady.Length}");
        }

        ushort sequence = 0;
        byte[] ticket = [0, 0, 0, 0];
        var connectUser = A9Vue990PpcsControlCommandBuilder.BuildConnectUser(
            ref sequence,
            ticket,
            "admin",
            "888888");
        await udp.SendAsync(connectUser, remote, ct).ConfigureAwait(false);
        lines.Add($"  sent ConnectUser bytes={connectUser.Length}");

        using var loginTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        loginTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!loginTimeout.IsCancellationRequested)
        {
            var received = await TryReceivePpcsPacketAsync(
                    udp,
                    remote,
                    localIps,
                    captureDir,
                    "ppcs-login",
                    lines,
                    loginTimeout.Token)
                .ConfigureAwait(false);
            if (received is null)
                break;

            lines.Add(
                $"  login recv {received.RemoteEndPoint} bytes={received.Raw.Length} " +
                $"ppcs={received.Packet.Type}/enc={received.Encryption} prefix={ToHex(received.Raw, 48)}" +
                (received.SavedRawPath is null ? string.Empty : $" savedRaw={received.SavedRawPath}"));

            if (received.Packet.Type == A9Vue990PpcsPacketType.P2pAlive)
            {
                await udp.SendAsync(
                    A9Vue990PpcsPacket.Build(A9Vue990PpcsPacketType.P2pAliveAck).ToArray(),
                    remote,
                    ct).ConfigureAwait(false);
                continue;
            }

            if (!received.Packet.TryReadDrw(out var channel, out var commandIndex, out var drwPayload))
                continue;

            await udp.SendAsync(A9Vue990PpcsPacket.BuildDrwAck(channel, commandIndex).ToArray(), remote, ct)
                .ConfigureAwait(false);

            if (channel != A9Vue990PpcsPacket.CommandChannel ||
                !A9Vue990PpcsControlCommandBuilder.TryReadControlHeader(
                    drwPayload.Span,
                    out var controlCommand,
                    out var payloadLength,
                    out _))
            {
                continue;
            }

            if (payloadLength > 4 && drwPayload.Length >= 8 + payloadLength)
            {
                var payload = drwPayload.Span.Slice(12, payloadLength - 4).ToArray();
                A9Vue990PpcsControlCommandBuilder.XqBytesDec(payload, 4);
                if (controlCommand == A9Vue990PpcsControlCommandBuilder.ConnectUserAck && payload.Length >= 8)
                {
                    ticket = payload.AsSpan(4, 4).ToArray();
                    lines.Add($"  login ack ticket={ToHex(ticket, 4)}");
                    break;
                }
            }
        }

        if (ticket.All(value => value == 0))
        {
            lines.Add("  no ConnectUserAck ticket; classic PPPP stream path stopped before StartVideo");
            sessionUdp.Dispose();
            return frames;
        }

        var resolution = A9Vue990PpcsControlCommandBuilder.BuildVideoResolution(ref sequence, ticket, 2);
        var startVideo = A9Vue990PpcsControlCommandBuilder.BuildStartVideo(ref sequence, ticket);
        var liveCgiPayload = A9Vue990CgiCommandBuilder.BuildLiveStreamRequest(sequence);
        var liveCgi = A9Vue990PpcsPacket.BuildDrw(
                A9Vue990PpcsPacket.VideoChannel,
                sequence++,
                liveCgiPayload)
            .ToArray();
        await udp.SendAsync(resolution, remote, ct).ConfigureAwait(false);
        await udp.SendAsync(startVideo, remote, ct).ConfigureAwait(false);
        await udp.SendAsync(liveCgi, remote, ct).ConfigureAwait(false);
        lines.Add(
            $"  sent VideoResolution bytes={resolution.Length}, StartVideo bytes={startVideo.Length}, " +
            $"and live CGI channel-1 bytes={liveCgi.Length}");

        var assembler = new A9Vue990VideoFrameAssembler();
        using var streamTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        streamTimeout.CancelAfter(TimeSpan.FromSeconds(12));
        while (!streamTimeout.IsCancellationRequested && frames.Count < 12)
        {
            var received = await TryReceivePpcsPacketAsync(
                    udp,
                    remote,
                    localIps,
                    captureDir,
                    "ppcs-stream",
                    lines,
                    streamTimeout.Token)
                .ConfigureAwait(false);
            if (received is null)
                break;

            if (received.Packet.Type == A9Vue990PpcsPacketType.P2pAlive)
            {
                await udp.SendAsync(
                    A9Vue990PpcsPacket.Build(A9Vue990PpcsPacketType.P2pAliveAck).ToArray(),
                    remote,
                    ct).ConfigureAwait(false);
                continue;
            }

            if (!received.Packet.TryReadDrw(out var channel, out var commandIndex, out var drwPayload))
                continue;

            await udp.SendAsync(A9Vue990PpcsPacket.BuildDrwAck(channel, commandIndex).ToArray(), remote, ct)
                .ConfigureAwait(false);

            if (channel != A9Vue990PpcsPacket.VideoChannel)
                continue;

            foreach (var rawFrame in assembler.AddVideoDrwChunk(commandIndex, drwPayload.Span))
            {
                foreach (var frame in ExtractJpegFrames(rawFrame))
                {
                    var local = Path.Combine(captureDir, $"classic-ppcs-frame-{frames.Count:000}.jpg");
                    await File.WriteAllBytesAsync(local, frame.Bytes, streamTimeout.Token).ConfigureAwait(false);
                    frames.Add(frame with { LocalPath = local });
                    lines.Add(
                        $"  savedClassicFrame[{frames.Count - 1}] path={local} bytes={frame.Bytes.Length} " +
                        $"dimensions={frame.Width}x{frame.Height} sha256={frame.Sha256}");
                }
            }
        }

        if (frames.Count == 0)
            lines.Add("  no classic PPPP JPEG frames captured");

        sessionUdp.Dispose();
        return frames;
    }

    private static async Task<ReceivedPpcsPacket?> TryPpcsHandshakeAsync(
        UdpClient udp,
        IPEndPoint remote,
        IReadOnlyCollection<string> localIps,
        string captureDir,
        string socketName,
        ICollection<string> lines,
        CancellationToken ct)
    {
        var localEndpoint = udp.Client.LocalEndPoint as IPEndPoint;
        var probes = BuildPpcsHandshakeProbes(localEndpoint, localIps);
        foreach (var probe in probes)
        {
            try
            {
                await udp.SendAsync(probe.Payload, remote, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                lines.Add($"  send failed {socketName}/{probe.Name} to {remote}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        lines.Add(
            $"  sent handshake burst socket={socketName} target={remote} probes={probes.Count} " +
            $"local={localEndpoint?.ToString() ?? "<unknown>"}");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(750));
        while (!timeout.IsCancellationRequested)
        {
            var received = await TryReceivePpcsPacketAsync(
                    udp,
                    expectedRemote: null,
                    localIps,
                    captureDir,
                    $"ppcs-handshake-{socketName}",
                    lines,
                    timeout.Token)
                .ConfigureAwait(false);
            if (received is null)
                break;

            lines.Add(
                $"  recv handshake from {received.RemoteEndPoint} bytes={received.Raw.Length} " +
                $"ppcs={received.Packet.Type}/enc={received.Encryption} prefix={ToHex(received.Raw, 48)}" +
                (received.SavedRawPath is null ? string.Empty : $" savedRaw={received.SavedRawPath}"));

            if (received.Packet.Type is A9Vue990PpcsPacketType.PunchPacket or A9Vue990PpcsPacketType.P2pReady)
                return received;

            if (received.Packet.Type is A9Vue990PpcsPacketType.P2pRequestAck or A9Vue990PpcsPacketType.ListenRequestAck)
            {
                var responseCount = 0;
                foreach (var id in BuildHlp2pIdCandidates())
                {
                    await udp.SendAsync(A9Vue990Hlp2pPacketBuilder.BuildPunchPacket(id.P2pId), received.RemoteEndPoint, ct)
                        .ConfigureAwait(false);
                    await udp.SendAsync(A9Vue990Hlp2pPacketBuilder.BuildP2pReady(id.P2pId), received.RemoteEndPoint, ct)
                        .ConfigureAwait(false);
                    responseCount += 2;
                }

                lines.Add($"  sent HLP2P punch/ready responses to {received.RemoteEndPoint} count={responseCount}");
            }
        }

        return null;
    }

    private static IReadOnlyList<(string Name, byte[] Payload)> BuildPpcsHandshakeProbes(
        IPEndPoint? localEndpoint,
        IReadOnlyCollection<string> localIps)
    {
        var probes = new List<(string Name, byte[] Payload)>();
        AddProbe("hello", A9Vue990PpcsPacket.Build(A9Vue990PpcsPacketType.Hello).ToArray());
        AddProbe("p2p-request", A9Vue990PpcsPacket.Build(A9Vue990PpcsPacketType.P2pRequest).ToArray());
        AddProbe("p2p-request-id", A9Vue990PpcsPacket
            .Build(A9Vue990PpcsPacketType.P2pRequest, A9Vue990PpcsPacket.BuildIdentityPayload(DefaultClientId, DefaultVuid))
            .ToArray());
        AddProbe("punch-to", A9Vue990PpcsPacket.Build(A9Vue990PpcsPacketType.PunchTo).ToArray());
        AddProbe("punch-to-id", A9Vue990PpcsPacket
            .Build(A9Vue990PpcsPacketType.PunchTo, A9Vue990PpcsPacket.BuildIdentityPayload(DefaultClientId, DefaultVuid))
            .ToArray());
        AddProbe("lansearch", A9Vue990PpcsPacket.BuildLanSearch().ToArray());
        AddProbe("lansearch-ext", A9Vue990PpcsPacket.Build(A9Vue990PpcsPacketType.LanSearchExtended).ToArray());
        AddProbe("lansearch-ext-id", A9Vue990PpcsPacket.BuildLanSearchExtended(DefaultClientId, DefaultVuid).ToArray());
        AddProbe("list-request", A9Vue990PpcsPacket.Build(A9Vue990PpcsPacketType.ListRequest).ToArray());
        AddHlpProbe("hlp2p-lansearch", A9Vue990Hlp2pPacketBuilder.BuildLanSearch());
        AddHlpProbe("hlp2p-lansearch-ext", A9Vue990Hlp2pPacketBuilder.BuildLanSearchExtended());

        foreach (var id in BuildHlp2pIdCandidates())
        {
            AddHlpProbe($"hlp2p-list-{id.Name}", A9Vue990Hlp2pPacketBuilder.BuildListRequest(id.P2pId));
            AddHlpProbe($"hlp2p-punch-{id.Name}", A9Vue990Hlp2pPacketBuilder.BuildPunchPacket(id.P2pId));
            AddHlpProbe($"hlp2p-ready-{id.Name}", A9Vue990Hlp2pPacketBuilder.BuildP2pReady(id.P2pId));

            foreach (var endpoint in BuildLocalEndpointCandidates(localEndpoint, localIps))
            {
                AddHlpProbe(
                    $"hlp2p-p2preq4-{id.Name}-{endpoint.Address}-{endpoint.Port}-native",
                    A9Vue990Hlp2pPacketBuilder.BuildP2pRequest4(id.P2pId, endpoint.Address, (ushort)endpoint.Port));
                AddHlpProbe(
                    $"hlp2p-p2preq4-{id.Name}-{endpoint.Address}-{endpoint.Port}-readable",
                    A9Vue990Hlp2pPacketBuilder.BuildP2pRequest4Readable(id.P2pId, endpoint.Address, (ushort)endpoint.Port));
            }
        }

        return probes;

        void AddProbe(string name, byte[] payload)
        {
            probes.Add((name, payload));
            probes.Add(($"{name}-xor1", A9Vue990PpcsEncryptionCodec.Xor1Encode(payload)));
        }

        void AddHlpProbe(string name, byte[] payload)
        {
            probes.Add((name, payload));
        }
    }

    private static IReadOnlyList<(string Name, byte[] P2pId)> BuildHlp2pIdCandidates()
    {
        return A9Vue990Hlp2pPacketBuilder.BuildP2pIdCandidates(DefaultVuid)
            .Select(candidate => ($"vuid-{candidate.Name}", candidate.P2pId))
            .Concat(A9Vue990Hlp2pPacketBuilder.BuildP2pIdCandidates(DefaultClientId)
                .Select(candidate => ($"client-{candidate.Name}", candidate.P2pId)))
            .GroupBy(candidate => Convert.ToHexString(candidate.P2pId), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static IReadOnlyList<IPEndPoint> BuildLocalEndpointCandidates(
        IPEndPoint? localEndpoint,
        IReadOnlyCollection<string> localIps)
    {
        var addresses = new List<IPAddress>();
        if (localEndpoint?.Address.AddressFamily == AddressFamily.InterNetwork &&
            IsUsableLocalAddress(localEndpoint.Address))
        {
            addresses.Add(localEndpoint.Address);
        }

        foreach (var localIp in localIps)
        {
            if (IPAddress.TryParse(localIp, out var address) &&
                address.AddressFamily == AddressFamily.InterNetwork &&
                IsUsableLocalAddress(address) &&
                addresses.All(existing => !existing.Equals(address)))
            {
                addresses.Add(address);
            }
        }

        var ports = new List<int>();
        if (localEndpoint is not null && localEndpoint.Port is > 0 and <= 65535)
            ports.Add(localEndpoint.Port);
        foreach (var port in new[] { 65529, 32108 })
        {
            if (!ports.Contains(port))
                ports.Add(port);
        }

        return addresses
            .SelectMany(address => ports.Select(port => new IPEndPoint(address, port)))
            .ToArray();
    }

    private static bool IsUsableLocalAddress(IPAddress address)
    {
        return !IPAddress.IsLoopback(address) &&
            !address.Equals(IPAddress.Any) &&
            !address.Equals(IPAddress.None) &&
            !address.Equals(IPAddress.IPv6Any);
    }

    private static IReadOnlyList<IPAddress> BuildPpcsTargets(IPAddress hostAddress)
    {
        return
        [
            hostAddress,
            IPAddress.Parse("192.168.168.255"),
            IPAddress.Broadcast,
        ];
    }

    private static IReadOnlyList<IPEndPoint> BuildManagedLanHoleTargets(
        IPAddress hostAddress,
        IReadOnlyCollection<string> relayHosts)
    {
        var addresses = new List<IPAddress>
        {
            hostAddress,
            IPAddress.Parse("192.168.168.255"),
            IPAddress.Broadcast,
        };
        foreach (var relayHost in relayHosts)
        {
            if (IPAddress.TryParse(relayHost, out var relayAddress) &&
                relayAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                addresses.Add(relayAddress);
            }
        }

        int[] ports = [65531, 65529, 32108, 20190];

        return addresses
            .Distinct()
            .SelectMany(address => ports.Select(port => new IPEndPoint(address, port)))
            .ToArray();
    }

    private static IReadOnlyList<IPEndPoint> BuildHlp2pDirectLanHoleTargets(IPAddress hostAddress)
    {
        var addresses = new[]
        {
            IPAddress.Broadcast,
            IPAddress.Parse("192.168.168.255"),
            hostAddress,
        };
        int[] ports = [65530, 65531];

        return addresses
            .Distinct()
            .SelectMany(address => ports.Select(port => new IPEndPoint(address, port)))
            .ToArray();
    }

    private static async Task SendHlp2pDirectLegacyPreamblesAsync(
        IPAddress hostAddress,
        string attemptName,
        ICollection<string> lines,
        CancellationToken ct)
    {
        var targets = new[]
        {
            new IPEndPoint(IPAddress.Broadcast, 65529),
            new IPEndPoint(IPAddress.Parse("192.168.168.255"), 65529),
            new IPEndPoint(hostAddress, 65529),
        };

        try
        {
            using var udp = CreateUdpClient(65529);
            var sends = 0;
            foreach (var preamble in Hlp2pDirectLegacyPreambles)
            {
                foreach (var target in targets)
                {
                    await udp.SendAsync(preamble, target, ct).ConfigureAwait(false);
                    sends++;
                }
            }

            lines.Add($"  sent legacy 65529 preambles attempt={attemptName} sends={sends} local={udp.Client.LocalEndPoint}");
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            lines.Add($"  legacy 65529 preamble send failed attempt={attemptName}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task SendHlp2pDirectPostHoleControlAsync(
        UdpClient udp,
        IPEndPoint sessionRemote,
        A9Vue990PostHoleControlKind kind,
        string reason,
        ICollection<string> lines,
        CancellationToken ct)
    {
        var control = A9Vue990PostHoleControlProvider.GetControl(kind);
        var packet = control.Bytes;
        await udp.SendAsync(packet, sessionRemote, ct).ConfigureAwait(false);
        lines.Add($"  sent post-hole control[{control.Index}] name={control.Name} reason={reason} bytes={packet.Length} prefix={ToHex(packet, 32)}");
        await Task.Delay(45, ct).ConfigureAwait(false);
    }

    private static async Task CompleteHlp2pDirectAliveHandshakeAsync(
        UdpClient udp,
        IPEndPoint sessionRemote,
        IReadOnlyCollection<string> localIps,
        string captureDir,
        ICollection<string> lines,
        CancellationToken ct)
    {
        await udp.SendAsync(A9Vue990Hlp2pDirectPacket.AliveProbe.ToArray(), sessionRemote, ct).ConfigureAwait(false);
        lines.Add("  sent compact alive probe 0B0000");

        var sawAliveProbe = false;
        var sawAliveAck = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var received = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                if (localIps.Contains(received.RemoteEndPoint.Address.ToString()))
                    continue;

                var savedRaw = SaveRawPacket(captureDir, "hlp2p-direct-alive-rx", received.RemoteEndPoint, received.Buffer);
                lines.Add(
                    $"  alive rx from {received.RemoteEndPoint} bytes={received.Buffer.Length} " +
                    $"prefix={ToHex(received.Buffer, 32)} savedRaw={savedRaw}");

                if (A9Vue990Hlp2pDirectPacket.IsAliveProbe(received.Buffer))
                {
                    sawAliveProbe = true;
                    await udp.SendAsync(A9Vue990Hlp2pDirectPacket.AliveAck.ToArray(), sessionRemote, timeout.Token)
                        .ConfigureAwait(false);
                    lines.Add("  sent compact alive ack 0C");
                    continue;
                }

                if (A9Vue990Hlp2pDirectPacket.IsAliveAck(received.Buffer))
                {
                    sawAliveAck = true;
                    break;
                }

                if (A9Vue990Hlp2pDirectPacket.TryParseLanHoleReady(received.Buffer, out _))
                    continue;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                lines.Add($"  alive receive failed: {ex.GetType().Name}: {ex.Message}");
                break;
            }
        }

        lines.Add($"  alive summary sawProbe={sawAliveProbe} sawAck={sawAliveAck}");
    }

    private static IReadOnlyList<Hlp2pDirectLanHoleAttempt> BuildHlp2pDirectLanHoleAttempts()
    {
        return
        [
            new Hlp2pDirectLanHoleAttempt(
                "observed-run2",
                new A9Vue990Hlp2pLanHoleSeed(
                    Convert.FromHexString("C1F3ECE4"),
                    Convert.FromHexString("8AB8F6F4"),
                    Convert.FromHexString("7A46F89D"),
                    Convert.FromHexString("B85C64E8"),
                    Convert.FromHexString("2EEA4A01"),
                    0x29E669CB)),
            new Hlp2pDirectLanHoleAttempt(
                "observed-run1",
                new A9Vue990Hlp2pLanHoleSeed(
                    Convert.FromHexString("284A762B"),
                    Convert.FromHexString("B4A2BB48"),
                    Convert.FromHexString("707C9F63"),
                    Convert.FromHexString("1B9D3AB9"),
                    Convert.FromHexString("2DB65E01"),
                    0x24F4C665)),
            new Hlp2pDirectLanHoleAttempt(
                "random-shape",
                A9Vue990Hlp2pDirectPacket.CreateObservedShapeSeed()),
        ];
    }

    private static IPAddress? SelectManagedLanHoleLocalAddress(IReadOnlyCollection<string> localIps)
    {
        var candidates = localIps
            .Select(value => IPAddress.TryParse(value, out var address) ? address : null)
            .Where(address => address is not null &&
                              address.AddressFamily == AddressFamily.InterNetwork &&
                              IsUsableLocalAddress(address))
            .Cast<IPAddress>()
            .ToArray();

        return candidates.FirstOrDefault(address =>
                   address.ToString().StartsWith("192.168.168.", StringComparison.Ordinal)) ??
               candidates.FirstOrDefault();
    }

    private static async Task<string?> TryFetchStatusBodyAsync(
        string host,
        ICollection<string> lines,
        CancellationToken ct)
    {
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(6),
        };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BodyCam-A9PhoneProbe", "1.0"));

        var endpoint = BuildHttpEndpoint(host, 81, "/get_status.cgi?loginuse=admin&loginpas=888888");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var response = await http.GetAsync(
                    endpoint,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token)
                .ConfigureAwait(false);
            var bytes = await ReadBoundedAsync(
                    response.Content,
                    maxBytes: 512 * 1024,
                    readDuration: TimeSpan.FromSeconds(2),
                    timeout.Token)
                .ConfigureAwait(false);

            lines.Add($"  status fetch {(int)response.StatusCode} bytes={bytes.Length}");
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            lines.Add($"  status fetch failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static string? ExtractJavaScriptStringVar(string body, string name)
    {
        var pattern = $"var {name}=\"";
        var start = body.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += pattern.Length;
        var end = body.IndexOf('"', start);
        return end > start ? body[start..end] : null;
    }

    private static bool TryReadHlp2pHeader(byte[] bytes, out ushort command, out ushort payloadLength)
    {
        command = 0;
        payloadLength = 0;
        if (bytes.Length < 4)
            return false;

        command = (ushort)((bytes[0] << 8) | bytes[1]);
        payloadLength = (ushort)((bytes[2] << 8) | bytes[3]);
        return bytes[0] == 0xf1;
    }

    private static async Task<ReceivedPpcsPacket?> TryReceivePpcsPacketAsync(
        UdpClient udp,
        IPEndPoint? expectedRemote,
        IReadOnlyCollection<string> localIps,
        string captureDir,
        string label,
        ICollection<string> lines,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var response = await udp.ReceiveAsync(ct).ConfigureAwait(false);
                var remoteAddress = response.RemoteEndPoint.Address.ToString();
                if (localIps.Contains(remoteAddress))
                    continue;

                var savedRaw = SaveRawPacket(captureDir, $"{label}-rx", response.RemoteEndPoint, response.Buffer);
                if (expectedRemote is not null &&
                    !response.RemoteEndPoint.Address.Equals(expectedRemote.Address))
                {
                    lines.Add(
                        $"  rx non-session packet from {response.RemoteEndPoint}; expected={expectedRemote.Address} " +
                        $"bytes={response.Buffer.Length} prefix={ToHex(response.Buffer, 48)} savedRaw={savedRaw}");
                }

                if (!A9Vue990PpcsPacket.TryDecode(response.Buffer, out var encryption, out var packet))
                {
                    lines.Add(
                        $"  rx undecoded packet from {response.RemoteEndPoint} bytes={response.Buffer.Length} " +
                        $"prefix={ToHex(response.Buffer, 64)} savedRaw={savedRaw}");
                    continue;
                }

                return new ReceivedPpcsPacket(response.RemoteEndPoint, response.Buffer, encryption, packet, savedRaw);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }

        return null;
    }

    private static async Task ProbeRelaySessionAsync(
        string host,
        string captureDir,
        ICollection<string> lines,
        CancellationToken ct)
    {
        lines.Add("- Managed C# relay/session-open fallback:");
        try
        {
            var result = await new A9Vue990RelayHelloProbeClient().ProbeAsync(new A9Vue990RelayHelloProbeOptions
            {
                Host = host,
                StatusTimeout = TimeSpan.FromSeconds(4),
                ConnectTimeout = TimeSpan.FromMilliseconds(700),
                ReadTimeout = TimeSpan.FromMilliseconds(700),
                MaxCandidates = 8,
                MaxResponseBytes = 4096,
                ResponseOutputDirectory = captureDir,
            }, ct).ConfigureAwait(false);

            lines.Add($"  success={result.Success} responseBytes={result.HasResponseBytes} attempts={result.Attempts.Count}");
            if (!string.IsNullOrWhiteSpace(result.Error))
                lines.Add($"  error={result.Error}");
            if (result.Das is not null)
            {
                lines.Add(
                    $"  dasRelays={string.Join(",", result.Das.DecodedPayload.RelayHosts.DefaultIfEmpty("<none>"))} " +
                    $"tokens={string.Join("|", result.Das.DecodedPayload.Tokens)}");
            }

            foreach (var attempt in result.Attempts)
            {
                lines.Add(
                    $"  relay {attempt.Host} tcp/{attempt.Port} {attempt.Candidate}: " +
                    $"opened={attempt.Opened} sent={attempt.BytesSent} received={attempt.BytesReceived} " +
                    $"outcome={attempt.Outcome ?? attempt.Error ?? "<none>"}");

                if (!string.IsNullOrWhiteSpace(attempt.ResponsePrefixHex))
                    lines.Add($"    responsePrefix={attempt.ResponsePrefixHex}");
                if (!string.IsNullOrWhiteSpace(attempt.SavedResponsePath))
                    lines.Add($"    savedRelayResponse={attempt.SavedResponsePath}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or OperationCanceledException or InvalidOperationException)
        {
            lines.Add($"  relayFallbackError={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IEnumerable<string> BuildHttpPathVariants()
    {
        var authQueries = new[]
        {
            string.Empty,
            "loginuse=admin&loginpas=888888",
            "user=admin&pwd=888888",
            "usr=admin&pwd=888888",
        };

        foreach (var path in HttpPaths)
        {
            foreach (var auth in authQueries)
            {
                if (string.IsNullOrWhiteSpace(auth))
                {
                    yield return path;
                    continue;
                }

                if (path.EndsWith("?", StringComparison.Ordinal) ||
                    path.EndsWith("&", StringComparison.Ordinal))
                {
                    yield return path + auth;
                }
                else
                {
                    yield return path + (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + auth;
                }
            }
        }
    }

    private static bool IsFastHttpStatusProbe(string path)
    {
        return path.StartsWith("/get_status.cgi", StringComparison.OrdinalIgnoreCase);
    }

    private static void SaveFirstImage(
        string captureDir,
        IReadOnlyList<CapturedJpegFrame> frames,
        ICollection<string> lines)
    {
        if (frames.Count == 0)
        {
            lines.Add("  imageCapture=not saved, no managed JPEG frames");
            return;
        }

        var path = Path.Combine(captureDir, "managed-direct-still.jpg");
        File.WriteAllBytes(path, frames[0].Bytes);
        lines.Add(
            $"  imageCapture={path} bytes={frames[0].Bytes.Length} " +
            $"dimensions={frames[0].Width}x{frames[0].Height} sha256={frames[0].Sha256}");
    }

    private static void SaveMjpegAvi(
        string captureDir,
        IReadOnlyList<CapturedJpegFrame> frames,
        ICollection<string> lines)
    {
        if (frames.Count == 0)
        {
            lines.Add("  videoCapture=not saved, no managed JPEG frames");
            return;
        }

        var usable = frames
            .Where(frame => frame.Width > 0 && frame.Height > 0)
            .GroupBy(frame => (frame.Width, frame.Height))
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();
        if (usable is null)
        {
            lines.Add("  videoCapture=not saved, managed JPEG dimensions were unknown");
            return;
        }

        var path = Path.Combine(captureDir, "managed-direct-video-mjpeg.avi");
        MjpegAviWriter.Write(path, usable.Select(frame => frame.Bytes).ToArray(), usable.Key.Width, usable.Key.Height, DefaultFramesPerSecond);
        var bytes = File.ReadAllBytes(path);
        lines.Add(
            $"  videoCapture={path} bytes={bytes.Length} frames={usable.Count()} " +
            $"dimensions={usable.Key.Width}x{usable.Key.Height} sha256={Convert.ToHexString(SHA256.HashData(bytes))}");
    }

    private static async Task SaveHlp2pDirectFrameAsync(
        string captureDir,
        List<CapturedJpegFrame> frames,
        CapturedJpegFrame frame,
        CancellationToken ct)
    {
        var local = Path.Combine(captureDir, $"managed-hlp2p-direct-frame-{frames.Count:000}.jpg");
        await File.WriteAllBytesAsync(local, frame.Bytes, ct).ConfigureAwait(false);
        frames.Add(frame with { LocalPath = local });
    }

    private static async Task<bool> CanConnectTcpAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);
        using var tcp = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            await tcp.ConnectAsync(host, port, timeoutSource.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static UdpClient CreateUdpClient(int? localPort)
    {
        UdpClient udp;
        if (localPort is null)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            udp = new UdpClient { Client = socket };
        }
        else
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, localPort.Value));
            udp = new UdpClient { Client = socket };
        }

        udp.EnableBroadcast = true;
        udp.Client.ReceiveBufferSize = Math.Max(udp.Client.ReceiveBufferSize, 256 * 1024);
        udp.Client.SendBufferSize = Math.Max(udp.Client.SendBufferSize, 64 * 1024);
        return udp;
    }

    private static Uri BuildHttpEndpoint(string host, int port, string path)
    {
        if (!path.StartsWith('/'))
            path = "/" + path;

        var queryStart = path.IndexOf('?', StringComparison.Ordinal);
        return new UriBuilder("http", host, port)
        {
            Path = queryStart < 0 ? path : path[..queryStart],
            Query = queryStart < 0 ? string.Empty : path[(queryStart + 1)..],
        }.Uri;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maxBytes,
        TimeSpan readDuration,
        CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        var stopAt = DateTimeOffset.UtcNow + readDuration;

        while (memory.Length < maxBytes && DateTimeOffset.UtcNow < stopAt)
        {
            var remaining = Math.Min(buffer.Length, maxBytes - (int)memory.Length);
            var read = await stream.ReadAsync(buffer.AsMemory(0, remaining), ct).ConfigureAwait(false);
            if (read == 0)
                break;

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private static List<CapturedJpegFrame> ExtractJpegFrames(byte[] bytes)
    {
        var frames = new List<CapturedJpegFrame>();
        var offset = 0;

        while (frames.Count < 12)
        {
            var start = IndexOf(bytes, [0xff, 0xd8], offset);
            if (start < 0)
                break;

            var end = IndexOf(bytes, [0xff, 0xd9], start + 2);
            if (end < 0)
                break;

            var frame = bytes[start..(end + 2)];
            var (width, height) = TryReadJpegDimensions(frame);
            frames.Add(new CapturedJpegFrame(
                frame,
                width,
                height,
                Convert.ToHexString(SHA256.HashData(frame)),
                null));
            offset = end + 2;
        }

        return frames;
    }

    private static (int Width, int Height) TryReadJpegDimensions(byte[] jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xff || jpeg[1] != 0xd8)
            return (0, 0);

        var i = 2;
        while (i + 8 < jpeg.Length)
        {
            if (jpeg[i] != 0xff)
            {
                i++;
                continue;
            }

            while (i < jpeg.Length && jpeg[i] == 0xff)
                i++;

            if (i >= jpeg.Length)
                break;

            var marker = jpeg[i++];
            if (marker is 0xd8 or 0xd9)
                continue;

            if (i + 2 > jpeg.Length)
                break;

            var length = (jpeg[i] << 8) | jpeg[i + 1];
            if (length < 2 || i + length > jpeg.Length)
                break;

            if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf)
            {
                var height = (jpeg[i + 3] << 8) | jpeg[i + 4];
                var width = (jpeg[i + 5] << 8) | jpeg[i + 6];
                return (width, height);
            }

            i += length;
        }

        return (0, 0);
    }

    private static bool LooksLikeVideo(byte[] bytes, string contentType)
    {
        return contentType.Contains("video/", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase) ||
               IndexOf(bytes, [0x00, 0x00, 0x01], 0) >= 0 ||
               IndexOf(bytes, [0x00, 0x00, 0x00, 0x01], 0) >= 0;
    }

    private static bool LooksLikeText(byte[] bytes)
    {
        if (bytes.Length == 0)
            return false;

        var sample = bytes.AsSpan(0, Math.Min(bytes.Length, 80));
        var printable = 0;
        foreach (var value in sample)
        {
            if (value is >= 0x20 and <= 0x7e || value is 0x0a or 0x0d or 0x09)
                printable++;
        }

        return printable >= sample.Length * 0.8;
    }

    private static string ToSafeText(byte[] bytes, int maxBytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        var count = Math.Min(bytes.Length, maxBytes);
        var text = Encoding.UTF8.GetString(bytes, 0, count);
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            sb.Append(char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t' ? '.' : ch);
        }

        return sb.ToString().Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string ToHex(byte[] bytes, int maxBytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        return Convert.ToHexString(bytes.AsSpan(0, Math.Min(maxBytes, bytes.Length))).ToLowerInvariant();
    }

    private static int IndexOf(byte[] data, byte[] pattern, int start)
    {
        for (var i = Math.Max(0, start); i <= data.Length - pattern.Length; i++)
        {
            var found = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] == pattern[j])
                    continue;

                found = false;
                break;
            }

            if (found)
                return i;
        }

        return -1;
    }

    private static string SaveRawPacket(string captureDir, string label, IPEndPoint remoteEndPoint, byte[] bytes)
    {
        Directory.CreateDirectory(captureDir);
        var safeAddress = remoteEndPoint.Address.ToString()
            .Replace(':', '-')
            .Replace('.', '-');
        var safeLabel = new string(label.Select(ch =>
                char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')
            .ToArray());
        var path = Path.Combine(
            captureDir,
            $"{safeLabel}-{DateTimeOffset.Now:HHmmssfff}-{safeAddress}-{remoteEndPoint.Port}-{bytes.Length}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static IReadOnlyList<string> GetLocalIpv4Addresses()
    {
        var addresses = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                    addresses.Add(address.Address.ToString());
            }
        }

        return addresses;
    }

    private sealed record UdpProbe(string Name, int Port, byte[] Payload);

    private sealed class ProgressLineCollection(Action<string>? progress) : ICollection<string>
    {
        private readonly List<string> _lines = [];

        public int Count => _lines.Count;

        public bool IsReadOnly => false;

        public void Add(string item)
        {
            _lines.Add(item);
            progress?.Invoke(item);
        }

        public void Clear() => _lines.Clear();

        public bool Contains(string item) => _lines.Contains(item);

        public void CopyTo(string[] array, int arrayIndex) => _lines.CopyTo(array, arrayIndex);

        public bool Remove(string item) => _lines.Remove(item);

        public IEnumerator<string> GetEnumerator() => _lines.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed record ReceivedPpcsPacket(
        IPEndPoint RemoteEndPoint,
        byte[] Raw,
        A9Vue990PpcsEncryption Encryption,
        A9Vue990PpcsPacket Packet,
        string? SavedRawPath);

    private sealed record Hlp2pDirectLanHoleAttempt(
        string Name,
        A9Vue990Hlp2pLanHoleSeed Seed);

    private sealed record CapturedJpegFrame(
        byte[] Bytes,
        int Width,
        int Height,
        string Sha256,
        string? LocalPath);
}
