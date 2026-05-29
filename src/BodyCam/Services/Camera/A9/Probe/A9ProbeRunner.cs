using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Services.Camera.A9.Probe;

public sealed class A9ProbeRunner
{
    private const int RtspPort = 554;
    private const int V720Port = 6123;
    private const int PpppPort = A9Protocol.DefaultPort;
    private const int PpppAltDiscoveryPort = 20190;
    private const int MaxFrameBytes = 512 * 1024;

    private static readonly int[] HttpPorts = [80, 81];

    private static readonly string[] HttpProbePaths =
    [
        "/",
        "/video",
        "/video.cgi",
        "/videostream.cgi",
        "/mjpeg",
        "/mjpegstream.cgi",
        "/snapshot.jpg",
        "/?action=stream",
        "/cgi-bin/snapshot.cgi",
    ];

    public async Task<A9ProbeResult> RunAsync(
        A9ProbeOptions options,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var result = new A9ProbeResult
        {
            Timestamp = DateTimeOffset.Now,
        };

        result.LocalInterfaces.AddRange(GetLocalInterfaces());
        result.CandidateHosts.AddRange(BuildCandidateHosts(options, result.LocalInterfaces));

        log?.Invoke("A9 probe started.");
        log?.Invoke("Candidate hosts: " + (result.CandidateHosts.Count == 0
            ? "none"
            : string.Join(", ", result.CandidateHosts)));

        if (ShouldProbe(options, A9ProbeProtocol.Rtsp))
        {
            foreach (var host in result.CandidateHosts)
                result.Probes.Add(await ProbeRtspAsync(host, options.TimeoutMs, ct));
        }

        if (ShouldProbe(options, A9ProbeProtocol.HttpMjpeg))
        {
            foreach (var host in result.CandidateHosts)
            {
                foreach (var port in HttpPorts)
                    result.Probes.Add(await ProbeHttpMjpegAsync(host, port, options, ct));
            }
        }

        if (ShouldProbe(options, A9ProbeProtocol.V720Naxclow))
        {
            foreach (var host in result.CandidateHosts)
                result.Probes.Add(await ProbeTcpOnlyAsync(
                    A9ProbeProtocol.V720Naxclow,
                    host,
                    V720Port,
                    options.TimeoutMs,
                    "TCP 6123 open; V720/Naxclow protocol handshake is not implemented in Phase 0.",
                    ct));
        }

        if (ShouldProbe(options, A9ProbeProtocol.PpppUdpMjpeg))
        {
            foreach (var host in result.CandidateHosts)
                result.Probes.Add(await ProbePpppUdpAsync(host, PpppPort, A9ProbeProtocol.PpppUdpMjpeg, options.TimeoutMs, ct));

            result.Probes.AddRange(await ProbePpppBroadcastAsync(
                result.LocalInterfaces,
                PpppPort,
                A9ProbeProtocol.PpppUdpMjpeg,
                options.TimeoutMs,
                ct));
        }

        if (ShouldProbe(options, A9ProbeProtocol.PpppUdp20190))
        {
            foreach (var host in result.CandidateHosts)
                result.Probes.Add(await ProbePpppUdpAsync(host, PpppAltDiscoveryPort, A9ProbeProtocol.PpppUdp20190, options.TimeoutMs, ct));

            result.Probes.AddRange(await ProbePpppBroadcastAsync(
                result.LocalInterfaces,
                PpppAltDiscoveryPort,
                A9ProbeProtocol.PpppUdp20190,
                options.TimeoutMs,
                ct));
        }

        var selected = SelectBestProbe(result.Probes, options.Protocol);
        if (selected is not null)
        {
            result.SelectedProtocol = selected.Protocol;
            result.SelectedHost = selected.Host;
            log?.Invoke($"Selected {A9ProbeResult.Label(selected.Protocol)} on {selected.Host}.");
        }
        else
        {
            result.SelectedProtocol = A9ProbeProtocol.None;
            log?.Invoke("No supported A9 protocol was detected.");
        }

        if (options.FirstFrame && selected is not null)
            result.Frame = await CaptureFirstFrameAsync(selected, options, ct);

        return result;
    }

    private static bool ShouldProbe(A9ProbeOptions options, A9ProbeProtocol protocol)
    {
        return options.Protocol is A9ProbeProtocol.Auto or A9ProbeProtocol.None ||
               options.Protocol == protocol;
    }

    private static List<A9LocalInterfaceInfo> GetLocalInterfaces()
    {
        var interfaces = new List<A9LocalInterfaceInfo>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            IPInterfaceProperties properties;
            try
            {
                properties = nic.GetIPProperties();
            }
            catch
            {
                continue;
            }

            var gateways = properties.GatewayAddresses
                .Select(g => g.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                if (IPAddress.IsLoopback(unicast.Address))
                    continue;

                interfaces.Add(new A9LocalInterfaceInfo
                {
                    Name = nic.Name,
                    Description = nic.Description,
                    Address = unicast.Address.ToString(),
                    PrefixLength = unicast.PrefixLength,
                    Gateways = gateways,
                    Broadcast = GetBroadcastAddress(unicast.Address, unicast.IPv4Mask),
                });
            }
        }

        return interfaces;
    }

    private static List<string> BuildCandidateHosts(
        A9ProbeOptions options,
        IReadOnlyList<A9LocalInterfaceInfo> localInterfaces)
    {
        var hosts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? host)
        {
            host = NormalizeHost(host);
            if (string.IsNullOrWhiteSpace(host))
                return;

            if (seen.Add(host))
                hosts.Add(host);
        }

        Add(options.Host);
        foreach (var host in options.Hosts)
            Add(host);

        Add(Environment.GetEnvironmentVariable("A9_CAMERA_IP"));

        foreach (var local in localInterfaces)
        {
            foreach (var gateway in local.Gateways)
                Add(gateway);

            Add(GetNetworkDotOne(local.Address, local.PrefixLength));
        }

        Add("192.168.1.1");
        Add("192.168.169.1");
        Add("192.168.4.1");

        return hosts;
    }

    private static string? NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;

        host = host.Trim();
        if (Uri.TryCreate(host, UriKind.Absolute, out var uri))
            return uri.Host;

        var portSeparator = host.LastIndexOf(':');
        if (portSeparator > 0 && IPAddress.TryParse(host[..portSeparator], out _))
            return host[..portSeparator];

        return host;
    }

    private static string? GetNetworkDotOne(string address, int prefixLength)
    {
        if (!IPAddress.TryParse(address, out var ip) || prefixLength is < 1 or > 30)
            return null;

        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4)
            return null;

        var ipValue = ToUInt32(bytes);
        var mask = uint.MaxValue << (32 - prefixLength);
        var candidate = (ipValue & mask) + 1;
        return new IPAddress(FromUInt32(candidate)).ToString();
    }

    private static string? GetBroadcastAddress(IPAddress address, IPAddress? mask)
    {
        if (mask is null)
            return null;

        var ipBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        if (ipBytes.Length != 4 || maskBytes.Length != 4)
            return null;

        var broadcast = new byte[4];
        for (var i = 0; i < 4; i++)
            broadcast[i] = (byte)(ipBytes[i] | ~maskBytes[i]);

        return new IPAddress(broadcast).ToString();
    }

    private static uint ToUInt32(byte[] bytes)
    {
        return ((uint)bytes[0] << 24) |
               ((uint)bytes[1] << 16) |
               ((uint)bytes[2] << 8) |
               bytes[3];
    }

    private static byte[] FromUInt32(uint value)
    {
        return
        [
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value,
        ];
    }

    private static async Task<A9ProtocolProbeResult> ProbeRtspAsync(
        string host,
        int timeoutMs,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var timeout = CreateTimeout(timeoutMs, ct);
        using var tcp = new TcpClient(AddressFamily.InterNetwork);

        try
        {
            await tcp.ConnectAsync(host, RtspPort, timeout.Token);
            var stream = tcp.GetStream();
            var request = Encoding.ASCII.GetBytes(
                $"OPTIONS rtsp://{host}/ RTSP/1.0\r\nCSeq: 1\r\nUser-Agent: BodyCam-A9Probe\r\n\r\n");
            await stream.WriteAsync(request, timeout.Token);

            var buffer = new byte[512];
            var read = await stream.ReadAsync(buffer, timeout.Token);
            var response = Encoding.ASCII.GetString(buffer, 0, read);
            var isRtsp = response.StartsWith("RTSP/", StringComparison.OrdinalIgnoreCase);

            return new A9ProtocolProbeResult
            {
                Protocol = A9ProbeProtocol.Rtsp,
                Host = host,
                Port = RtspPort,
                Status = isRtsp ? A9ProbeStatus.Responded : A9ProbeStatus.Open,
                IsSupported = isRtsp,
                Message = isRtsp ? FirstLine(response) : "TCP 554 open, but no RTSP response signature was read.",
                DurationMs = sw.ElapsedMilliseconds,
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Timeout(A9ProbeProtocol.Rtsp, host, RtspPort, sw.ElapsedMilliseconds);
        }
        catch (SocketException ex)
        {
            return Closed(A9ProbeProtocol.Rtsp, host, RtspPort, ex.Message, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return Failed(A9ProbeProtocol.Rtsp, host, RtspPort, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static async Task<A9ProtocolProbeResult> ProbeHttpMjpegAsync(
        string host,
        int port,
        A9ProbeOptions options,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!await CanConnectTcpAsync(host, port, options.TimeoutMs, ct))
            return Closed(A9ProbeProtocol.HttpMjpeg, host, port, $"TCP {port} closed or timed out.", sw.ElapsedMilliseconds);

        using var timeout = CreateTimeout(options.TimeoutMs, ct);
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(Math.Max(500, options.TimeoutMs)),
        };

        foreach (var path in HttpProbePaths)
        {
            var endpoint = BuildHttpEndpoint(host, port, path);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                AddBasicAuth(request, options);

                using var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);

                var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                var statusCode = (int)response.StatusCode;
                var supported = IsHttpMjpegContent(contentType) ||
                                response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

                if (supported)
                {
                    return new A9ProtocolProbeResult
                    {
                        Protocol = A9ProbeProtocol.HttpMjpeg,
                        Host = host,
                        Port = port,
                        Endpoint = endpoint,
                        Status = A9ProbeStatus.Responded,
                        IsSupported = true,
                        Message = $"HTTP {statusCode}; content-type={contentType}",
                        HttpStatusCode = statusCode,
                        ContentType = contentType,
                        DurationMs = sw.ElapsedMilliseconds,
                    };
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Timeout(A9ProbeProtocol.HttpMjpeg, host, port, sw.ElapsedMilliseconds, endpoint);
            }
            catch (HttpRequestException)
            {
                continue;
            }
        }

        var controlProbe = await ProbeHttpControlApiAsync(host, port, options, http, sw, timeout.Token);
        if (controlProbe is not null)
            return controlProbe;

        return new A9ProtocolProbeResult
        {
            Protocol = A9ProbeProtocol.HttpMjpeg,
            Host = host,
            Port = port,
            Status = A9ProbeStatus.Open,
            IsSupported = false,
            Message = $"TCP {port} open, but no common MJPEG/snapshot endpoint matched.",
            DurationMs = sw.ElapsedMilliseconds,
        };
    }

    private static async Task<A9ProtocolProbeResult?> ProbeHttpControlApiAsync(
        string host,
        int port,
        A9ProbeOptions options,
        HttpClient http,
        Stopwatch sw,
        CancellationToken ct)
    {
        var statusEndpoint = BuildHttpEndpoint(
            host,
            port,
            $"/get_status.cgi?user={Uri.EscapeDataString(options.Username)}&pwd={Uri.EscapeDataString(options.Password)}");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, statusEndpoint);
            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var body = await ReadLimitedTextAsync(response.Content, 4096, ct);
            if (!LooksLikeA9StatusBody(body))
                return null;

            var statusCode = (int)response.StatusCode;
            var deviceId = ExtractJavaScriptStringVar(body, "realdeviceid") ??
                           ExtractJavaScriptStringVar(body, "deviceid");
            var alias = ExtractJavaScriptStringVar(body, "alias");
            var identity = string.IsNullOrWhiteSpace(alias)
                ? string.Empty
                : $" alias={alias}";

            return new A9ProtocolProbeResult
            {
                Protocol = A9ProbeProtocol.HttpMjpeg,
                Host = host,
                Port = port,
                Endpoint = statusEndpoint,
                Status = A9ProbeStatus.Responded,
                IsSupported = false,
                Message = $"HTTP control API responded with HTTP {statusCode}; content-type={contentType}; no stream endpoint matched.{identity}",
                DeviceId = deviceId,
                HttpStatusCode = statusCode,
                ContentType = contentType,
                DurationMs = sw.ElapsedMilliseconds,
            };
        }
        catch (OperationCanceledException)
        {
            return Timeout(A9ProbeProtocol.HttpMjpeg, host, port, sw.ElapsedMilliseconds, statusEndpoint);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static async Task<A9ProtocolProbeResult> ProbeTcpOnlyAsync(
        A9ProbeProtocol protocol,
        string host,
        int port,
        int timeoutMs,
        string openMessage,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var open = await CanConnectTcpAsync(host, port, timeoutMs, ct);
        return open
            ? new A9ProtocolProbeResult
            {
                Protocol = protocol,
                Host = host,
                Port = port,
                Status = A9ProbeStatus.Open,
                IsSupported = true,
                Message = openMessage,
                DurationMs = sw.ElapsedMilliseconds,
            }
            : Closed(protocol, host, port, $"TCP {port} closed or timed out.", sw.ElapsedMilliseconds);
    }

    private static async Task<bool> CanConnectTcpAsync(
        string host,
        int port,
        int timeoutMs,
        CancellationToken ct)
    {
        using var timeout = CreateTimeout(timeoutMs, ct);
        using var tcp = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            await tcp.ConnectAsync(host, port, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task<A9ProtocolProbeResult> ProbePpppUdpAsync(
        string host,
        int port,
        A9ProbeProtocol protocol,
        int timeoutMs,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var ip = await ResolveIpv4Async(host, ct);
        if (ip is null)
            return Failed(protocol, host, port, "Could not resolve host to IPv4.", sw.ElapsedMilliseconds);

        using var timeout = CreateTimeout(timeoutMs, ct);
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var packet = A9Protocol.BuildLanSearch();
        var endpoint = new IPEndPoint(ip, port);

        try
        {
            await udp.SendAsync(packet, packet.Length, endpoint).WaitAsync(timeout.Token);
            var response = await udp.ReceiveAsync(timeout.Token);
            return ParsePpppResponse(response, protocol, host, port, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Timeout(protocol, host, port, sw.ElapsedMilliseconds);
        }
        catch (SocketException ex)
        {
            return Closed(protocol, host, port, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static async Task<List<A9ProtocolProbeResult>> ProbePpppBroadcastAsync(
        IReadOnlyList<A9LocalInterfaceInfo> localInterfaces,
        int port,
        A9ProbeProtocol protocol,
        int timeoutMs,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var results = new List<A9ProtocolProbeResult>();
        var broadcastHosts = localInterfaces
            .Select(i => i.Broadcast)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Append("255.255.255.255")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        using var timeout = CreateTimeout(timeoutMs, ct);
        using var udp = new UdpClient(AddressFamily.InterNetwork)
        {
            EnableBroadcast = true,
        };

        var packet = A9Protocol.BuildLanSearch();
        foreach (var host in broadcastHosts)
        {
            if (!IPAddress.TryParse(host, out var ip))
                continue;

            try
            {
                await udp.SendAsync(packet, packet.Length, new IPEndPoint(ip, port)).WaitAsync(timeout.Token);
            }
            catch
            {
                // Best effort: one blocked broadcast address should not hide other probes.
            }
        }

        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var response = await udp.ReceiveAsync(timeout.Token);
                results.Add(ParsePpppResponse(
                    response,
                    protocol,
                    response.RemoteEndPoint.Address.ToString(),
                    port,
                    sw.ElapsedMilliseconds,
                    $"broadcast UDP {port}"));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException ex)
            {
                results.Add(Failed(protocol, null, port, ex.Message, sw.ElapsedMilliseconds, $"broadcast UDP {port}"));
                break;
            }
        }

        if (results.Count == 0)
        {
            results.Add(new A9ProtocolProbeResult
            {
                Protocol = protocol,
                Port = port,
                Endpoint = $"broadcast UDP {port}",
                Status = A9ProbeStatus.Timeout,
                IsSupported = false,
                Message = "No PunchPkt response to broadcast LanSearch.",
                DurationMs = sw.ElapsedMilliseconds,
            });
        }

        return results;
    }

    private static A9ProtocolProbeResult ParsePpppResponse(
        UdpReceiveResult response,
        A9ProbeProtocol protocol,
        string? host,
        int port,
        long durationMs,
        string? endpoint = null)
    {
        if (response.Buffer.Length < 4)
        {
            return Failed(protocol, host, port, "UDP response was shorter than a PPPP command header.", durationMs, endpoint);
        }

        var command = A9Protocol.ReadCommandId(response.Buffer);
        if (command != A9Protocol.CmdPunchPkt)
        {
            return new A9ProtocolProbeResult
            {
                Protocol = protocol,
                Host = host,
                Port = port,
                Endpoint = endpoint,
                Status = A9ProbeStatus.Responded,
                IsSupported = false,
                Message = $"UDP response command 0x{command:x4}, expected PunchPkt 0xf141.",
                DurationMs = durationMs,
            };
        }

        string? deviceId = null;
        try
        {
            deviceId = A9Protocol.ParsePunchPktDeviceId(response.Buffer);
        }
        catch
        {
            // Keep the probe useful even if a variant has a shorter PunchPkt.
        }

        return new A9ProtocolProbeResult
        {
            Protocol = protocol,
            Host = response.RemoteEndPoint.Address.ToString(),
            Port = port,
            Endpoint = endpoint,
            Status = A9ProbeStatus.Responded,
            IsSupported = true,
            Message = "Received PPPP PunchPkt.",
            DeviceId = deviceId,
            DurationMs = durationMs,
        };
    }

    private static async Task<A9FrameProbeResult> CaptureFirstFrameAsync(
        A9ProtocolProbeResult selected,
        A9ProbeOptions options,
        CancellationToken ct)
    {
        return selected.Protocol switch
        {
            A9ProbeProtocol.HttpMjpeg => await CaptureHttpFirstFrameAsync(selected, options, ct),
            A9ProbeProtocol.PpppUdpMjpeg => await CapturePpppFirstFrameAsync(selected.Host, options, ct),
            A9ProbeProtocol.Rtsp => SkippedFrame(selected, "RTSP first-frame capture needs a decoder/RTSP client and is outside Phase 0."),
            A9ProbeProtocol.V720Naxclow => SkippedFrame(selected, "V720/Naxclow first-frame capture belongs to Phase 14."),
            A9ProbeProtocol.PpppUdp20190 => SkippedFrame(selected, "UDP 20190 discovery is identified, but stream setup is not implemented."),
            _ => SkippedFrame(selected, "No selected protocol."),
        };
    }

    private static async Task<A9FrameProbeResult> CaptureHttpFirstFrameAsync(
        A9ProtocolProbeResult selected,
        A9ProbeOptions options,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var host = selected.Host;
        if (string.IsNullOrWhiteSpace(host))
            return FailedFrame(A9ProbeProtocol.HttpMjpeg, host, sw.ElapsedMilliseconds, "No HTTP host selected.");

        var port = selected.Port ?? 80;
        var endpoints = HttpProbePaths
            .Select(path => BuildHttpEndpoint(host, port, path))
            .Prepend(selected.Endpoint)
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        using var timeout = CreateTimeout(Math.Max(options.TimeoutMs * 3, 3000), ct);
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(Math.Max(options.TimeoutMs * 3, 3000)),
        };

        foreach (var endpoint in endpoints)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                AddBasicAuth(request, options);
                using var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);

                if (!response.IsSuccessStatusCode)
                    continue;

                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                var frame = await ReadJpegFrameAsync(stream, timeout.Token);
                if (frame is not null)
                {
                    return new A9FrameProbeResult
                    {
                        Protocol = A9ProbeProtocol.HttpMjpeg,
                        Host = host,
                        Success = true,
                        IsJpeg = StartsWithJpeg(frame),
                        Bytes = frame.Length,
                        DurationMs = sw.ElapsedMilliseconds,
                        Message = $"Captured first JPEG frame from {endpoint}.",
                    };
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                break;
            }
            catch (HttpRequestException)
            {
                continue;
            }
        }

        return FailedFrame(A9ProbeProtocol.HttpMjpeg, host, sw.ElapsedMilliseconds, "No HTTP endpoint returned a JPEG frame.");
    }

    private static async Task<A9FrameProbeResult> CapturePpppFirstFrameAsync(
        string? host,
        A9ProbeOptions options,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(host))
            return FailedFrame(A9ProbeProtocol.PpppUdpMjpeg, host, sw.ElapsedMilliseconds, "No PPPP host selected.");

        try
        {
            var frameReceived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var session = new A9Session(
                host,
                options.Username,
                options.Password,
                NullLogger.Instance,
                timeoutMs: Math.Max(options.TimeoutMs, 1000));

            session.FrameReceived += frame => frameReceived.TrySetResult(frame);

            using var timeout = CreateTimeout(Math.Max(options.TimeoutMs * 5, 8000), ct);
            await session.ConnectAsync(timeout.Token);
            var frame = await frameReceived.Task.WaitAsync(timeout.Token);

            return new A9FrameProbeResult
            {
                Protocol = A9ProbeProtocol.PpppUdpMjpeg,
                Host = host,
                Success = StartsWithJpeg(frame),
                IsJpeg = StartsWithJpeg(frame),
                Bytes = frame.Length,
                DurationMs = sw.ElapsedMilliseconds,
                Message = StartsWithJpeg(frame)
                    ? "Captured first PPPP/MJPEG frame."
                    : "Received a frame, but it did not start with a JPEG marker.",
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return FailedFrame(A9ProbeProtocol.PpppUdpMjpeg, host, sw.ElapsedMilliseconds, "Timed out waiting for PPPP/MJPEG first frame.");
        }
        catch (Exception ex)
        {
            return FailedFrame(A9ProbeProtocol.PpppUdpMjpeg, host, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private static async Task<byte[]?> ReadJpegFrameAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var memory = new MemoryStream();

        while (memory.Length < MaxFrameBytes)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
                break;

            memory.Write(buffer, 0, read);
            var data = memory.ToArray();
            var start = IndexOf(data, [0xff, 0xd8], 0);
            if (start < 0)
                continue;

            var end = IndexOf(data, [0xff, 0xd9], start + 2);
            if (end >= 0)
                return data[start..(end + 2)];
        }

        var bytes = memory.ToArray();
        return StartsWithJpeg(bytes) ? bytes : null;
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

    private static bool StartsWithJpeg(byte[] frame)
    {
        return frame.Length >= 2 && frame[0] == 0xff && frame[1] == 0xd8;
    }

    private static bool IsHttpMjpegContent(string contentType)
    {
        return contentType.Contains("multipart/x-mixed-replace", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("image/jpg", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildHttpEndpoint(string host, int port, string path)
    {
        var portSuffix = port == 80 ? string.Empty : $":{port}";
        return $"http://{host}{portSuffix}{path}";
    }

    private static async Task<string> ReadLimitedTextAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[1024];

        while (memory.Length < maxBytes)
        {
            var remaining = Math.Min(buffer.Length, maxBytes - (int)memory.Length);
            var read = await stream.ReadAsync(buffer.AsMemory(0, remaining), ct);
            if (read == 0)
                break;

            memory.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private static bool LooksLikeA9StatusBody(string body)
    {
        return body.Contains("var result=", StringComparison.OrdinalIgnoreCase) &&
               (body.Contains("realdeviceid", StringComparison.OrdinalIgnoreCase) ||
                body.Contains("deviceid", StringComparison.OrdinalIgnoreCase) ||
                body.Contains("sys_ver", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractJavaScriptStringVar(string body, string name)
    {
        var pattern = $"var {name}=\"";
        var start = body.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += pattern.Length;
        var end = body.IndexOf('"', start);
        if (end <= start)
            return null;

        return body[start..end];
    }

    private static void AddBasicAuth(HttpRequestMessage request, A9ProbeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Username))
            return;

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.Username}:{options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private static A9ProtocolProbeResult? SelectBestProbe(
        IReadOnlyList<A9ProtocolProbeResult> probes,
        A9ProbeProtocol requestedProtocol)
    {
        return probes
            .Where(p => p.IsSupported)
            .Where(p => requestedProtocol is A9ProbeProtocol.Auto or A9ProbeProtocol.None || p.Protocol == requestedProtocol)
            .OrderBy(p => ProtocolPriority(p.Protocol))
            .ThenBy(p => p.DurationMs)
            .FirstOrDefault();
    }

    private static int ProtocolPriority(A9ProbeProtocol protocol) => protocol switch
    {
        A9ProbeProtocol.Rtsp => 1,
        A9ProbeProtocol.HttpMjpeg => 2,
        A9ProbeProtocol.V720Naxclow => 3,
        A9ProbeProtocol.PpppUdpMjpeg => 4,
        A9ProbeProtocol.PpppUdp20190 => 5,
        _ => 99,
    };

    private static async Task<IPAddress?> ResolveIpv4Async(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var ip))
            return ip.AddressFamily == AddressFamily.InterNetwork ? ip : null;

        var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct);
        return addresses.FirstOrDefault();
    }

    private static CancellationTokenSource CreateTimeout(int timeoutMs, CancellationToken ct)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(100, timeoutMs)));
        return timeout;
    }

    private static string FirstLine(string value)
    {
        var lineEnd = value.IndexOfAny(['\r', '\n']);
        return lineEnd < 0 ? value.Trim() : value[..lineEnd].Trim();
    }

    private static A9ProtocolProbeResult Timeout(
        A9ProbeProtocol protocol,
        string? host,
        int port,
        long durationMs,
        string? endpoint = null)
    {
        return new A9ProtocolProbeResult
        {
            Protocol = protocol,
            Host = host,
            Port = port,
            Endpoint = endpoint,
            Status = A9ProbeStatus.Timeout,
            IsSupported = false,
            Message = "Timed out.",
            DurationMs = durationMs,
        };
    }

    private static A9ProtocolProbeResult Closed(
        A9ProbeProtocol protocol,
        string? host,
        int port,
        string message,
        long durationMs)
    {
        return new A9ProtocolProbeResult
        {
            Protocol = protocol,
            Host = host,
            Port = port,
            Status = A9ProbeStatus.Closed,
            IsSupported = false,
            Message = message,
            DurationMs = durationMs,
        };
    }

    private static A9ProtocolProbeResult Failed(
        A9ProbeProtocol protocol,
        string? host,
        int port,
        string message,
        long durationMs,
        string? endpoint = null)
    {
        return new A9ProtocolProbeResult
        {
            Protocol = protocol,
            Host = host,
            Port = port,
            Endpoint = endpoint,
            Status = A9ProbeStatus.Failed,
            IsSupported = false,
            Message = message,
            DurationMs = durationMs,
        };
    }

    private static A9FrameProbeResult SkippedFrame(A9ProtocolProbeResult selected, string message)
    {
        return new A9FrameProbeResult
        {
            Protocol = selected.Protocol,
            Host = selected.Host,
            Success = false,
            Skipped = true,
            Message = message,
        };
    }

    private static A9FrameProbeResult FailedFrame(
        A9ProbeProtocol protocol,
        string? host,
        long durationMs,
        string message)
    {
        return new A9FrameProbeResult
        {
            Protocol = protocol,
            Host = host,
            Success = false,
            DurationMs = durationMs,
            Message = message,
        };
    }
}
