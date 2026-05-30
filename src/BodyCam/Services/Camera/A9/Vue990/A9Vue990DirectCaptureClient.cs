using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace BodyCam.Services.Camera.A9.Vue990;

public interface IA9Vue990DirectCaptureClient
{
    Task<A9Vue990DirectCaptureResult> CaptureAsync(
        A9Vue990DirectCaptureOptions options,
        Action<string>? progress = null,
        CancellationToken ct = default);
}

public sealed class A9Vue990DirectCaptureClient : IA9Vue990DirectCaptureClient
{
    private const int DefaultFramesPerSecond = 2;

    private static readonly byte[][] LegacyPreambles =
    [
        Convert.FromHexString("00E876667C78B84C64CB4B94C90D982713"),
        Convert.FromHexString("009B2FA5823C60C29DC781071D1A12F134"),
    ];

    public async Task<A9Vue990DirectCaptureResult> CaptureAsync(
        A9Vue990DirectCaptureOptions options,
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new A9Vue990DirectCaptureResult
        {
            Timestamp = DateTimeOffset.Now,
            Host = options.Host,
            OutputDirectory = Path.GetFullPath(options.OutputDirectory),
        };

        var lines = new List<string>();
        void Add(string line)
        {
            lines.Add(line);
            progress?.Invoke(line);
        }

        try
        {
            Directory.CreateDirectory(result.OutputDirectory);
            Add("Vue990 managed direct C# capture:");
            Add($"- host: {options.Host}");
            Add($"- output: {result.OutputDirectory}");
            Add($"- post-hole control provider: {A9Vue990PostHoleControlProvider.Scope}");

            if (!IPAddress.TryParse(options.Host, out var hostAddress) ||
                hostAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                return Fail(result, lines, "Host must be an IPv4 address.");
            }

            var localIps = GetLocalIpv4Addresses();
            Add($"- local IPv4: {string.Join(",", localIps)}");

            await TryFetchStatusAsync(options.Host, lines, ct).ConfigureAwait(false);

            var frames = await CaptureDirectAsync(hostAddress, localIps, options, result, lines, ct)
                .ConfigureAwait(false);

            if (frames.Count > 0)
            {
                if (options.CaptureImage)
                    SaveFirstImage(result, frames, lines);
                if (options.CaptureVideo)
                    SaveMjpegAvi(result, frames, lines);
            }
            else
            {
                Add("- imageCapture=not saved, no managed JPEG frames");
                Add("- videoCapture=not saved, no managed JPEG frames");
            }

            result.Success = frames.Count > 0;
            result.CapturedImage = result.Artifacts.Any(artifact =>
                string.Equals(Path.GetFileName(artifact.LocalPath), "managed-direct-still.jpg", StringComparison.OrdinalIgnoreCase));
            result.CapturedVideo = result.Artifacts.Any(artifact =>
                string.Equals(Path.GetFileName(artifact.LocalPath), "managed-direct-video-mjpeg.avi", StringComparison.OrdinalIgnoreCase));
            result.Report = string.Join(Environment.NewLine, lines);

            if (!result.Success)
                result.Error = "No JPEG frames were received from the managed direct transport.";

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Fail(result, lines, "Capture cancelled.");
        }
        catch (Exception ex)
        {
            return Fail(result, lines, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<List<CapturedFrame>> CaptureDirectAsync(
        IPAddress hostAddress,
        IReadOnlyCollection<string> localIps,
        A9Vue990DirectCaptureOptions options,
        A9Vue990DirectCaptureResult result,
        ICollection<string> lines,
        CancellationToken ct)
    {
        var frames = new List<CapturedFrame>();
        using var udp = CreateUdpClient(null);
        lines.Add($"- socket: {udp.Client.LocalEndPoint}");

        var targets = BuildLanHoleTargets(hostAddress);
        A9Vue990Hlp2pLanHoleResponse? lanHoleResponse = null;
        A9Vue990Hlp2pLanHoleSeed? activeSeed = null;
        IPEndPoint? sessionRemote = null;

        foreach (var attempt in BuildLanHoleAttempts())
        {
            await SendLegacyPreamblesAsync(hostAddress, attempt.Name, lines, ct).ConfigureAwait(false);

            var probe = A9Vue990Hlp2pDirectPacket.BuildLanHoleProbe(attempt.Seed);
            foreach (var target in targets)
            {
                await udp.SendAsync(probe, target, ct).ConfigureAwait(false);
                await Task.Delay(35, ct).ConfigureAwait(false);
            }

            lines.Add($"- sent lan-hole attempt={attempt.Name} token={ToHex(attempt.Seed.SessionToken, 4)} uid=0x{attempt.Seed.UidLittleEndian:X8}");

            using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            responseTimeout.CancelAfter(TimeSpan.FromSeconds(3));
            while (!responseTimeout.IsCancellationRequested)
            {
                try
                {
                    var received = await udp.ReceiveAsync(responseTimeout.Token).ConfigureAwait(false);
                    if (localIps.Contains(received.RemoteEndPoint.Address.ToString()))
                        continue;

                    SaveRawPacket(result, "hlp2p-direct-lanhole-rx", received.RemoteEndPoint, received.Buffer);
                    lines.Add($"  lan-hole rx from {received.RemoteEndPoint} bytes={received.Buffer.Length} prefix={ToHex(received.Buffer, 32)}");

                    if (!A9Vue990Hlp2pDirectPacket.TryParseLanHoleResponse(received.Buffer, out var parsed))
                        continue;

                    lanHoleResponse = parsed;
                    activeSeed = attempt.Seed;
                    sessionRemote = received.RemoteEndPoint;
                    lines.Add($"  parsed lan-hole response aid=0x{parsed.AidLittleEndian:X8} status=0x{parsed.Status:X2}{parsed.StatusDetail:X2}");
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (lanHoleResponse is not null)
                break;

            lines.Add($"  no compact lan-hole response for attempt={attempt.Name}");
        }

        if (lanHoleResponse is null || activeSeed is null || sessionRemote is null)
        {
            lines.Add("- no compact lan-hole response; stopped before direct transport");
            return frames;
        }

        var ack = A9Vue990Hlp2pDirectPacket.BuildLanHoleAck(lanHoleResponse, activeSeed.UidLittleEndian);
        await udp.SendAsync(ack, sessionRemote, ct).ConfigureAwait(false);
        lines.Add($"- sent lan-hole ack remote={sessionRemote} hex={ToHex(ack, ack.Length)}");

        if (!await WaitForReadyAsync(udp, sessionRemote, localIps, result, lines, ct).ConfigureAwait(false))
            return frames;

        await CompleteAliveHandshakeAsync(udp, sessionRemote, localIps, result, lines, ct).ConfigureAwait(false);

        await SendPostHoleControlAsync(udp, sessionRemote, A9Vue990PostHoleControlKind.InitialShortRequest, "native-paced initial", lines, ct).ConfigureAwait(false);
        await SendPostHoleControlAsync(udp, sessionRemote, A9Vue990PostHoleControlKind.InitialLongRequest, "native-paced initial", lines, ct).ConfigureAwait(false);
        await Task.Delay(260, ct).ConfigureAwait(false);
        await SendPostHoleControlAsync(udp, sessionRemote, A9Vue990PostHoleControlKind.MediaShortRequest, "native-paced after first ack window", lines, ct).ConfigureAwait(false);
        await SendPostHoleControlAsync(udp, sessionRemote, A9Vue990PostHoleControlKind.MediaLongRequest, "native-paced after first ack window", lines, ct).ConfigureAwait(false);
        await Task.Delay(260, ct).ConfigureAwait(false);
        await SendPostHoleControlAsync(udp, sessionRemote, A9Vue990PostHoleControlKind.InitialLongRequest, "native-paced repeat before large response", lines, ct).ConfigureAwait(false);
        await Task.Delay(120, ct).ConfigureAwait(false);

        var assembler = new A9Vue990VideoFrameAssembler();
        using var channelBytes = new MemoryStream();
        var remotePacketCount = 0;
        var streamStarted = false;
        var repeatedControl3AfterLargeResponse = false;

        using var streamTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        streamTimeout.CancelAfter(TimeSpan.FromSeconds(options.StreamSeconds));
        while (!streamTimeout.IsCancellationRequested && frames.Count < options.MaxFrames)
        {
            try
            {
                var received = await udp.ReceiveAsync(streamTimeout.Token).ConfigureAwait(false);
                if (localIps.Contains(received.RemoteEndPoint.Address.ToString()))
                    continue;

                remotePacketCount++;
                if (remotePacketCount <= 24)
                {
                    SaveRawPacket(result, $"hlp2p-direct-stream-rx-{remotePacketCount:000}", received.RemoteEndPoint, received.Buffer);
                    lines.Add($"  stream rx[{remotePacketCount}] from {received.RemoteEndPoint} bytes={received.Buffer.Length} prefix={ToHex(received.Buffer, 48)}");
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
                var directAck = A9Vue990Hlp2pDirectPacket.BuildDirectAck(ackSequence, packet);
                await udp.SendAsync(directAck, sessionRemote, streamTimeout.Token).ConfigureAwait(false);
                lines.Add($"  sent direct ack seq=0x{ackSequence:X4} rxSeq=0x{packet.Sequence:X4} message=0x{packet.MessageId:X4} ackLen=0x{Math.Max(0, packet.TailLength - 8):X4}");

                if (!repeatedControl3AfterLargeResponse &&
                    packet.Operation == A9Vue990Hlp2pDirectPacket.DirectCommandOperation &&
                    packet.Sequence == 0 &&
                    packet.TailLength == 0x0339)
                {
                    repeatedControl3AfterLargeResponse = true;
                    await Task.Delay(220, streamTimeout.Token).ConfigureAwait(false);
                    await SendPostHoleControlAsync(udp, sessionRemote, A9Vue990PostHoleControlKind.MediaLongRequest, "after large response", lines, streamTimeout.Token).ConfigureAwait(false);
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
                        SaveFrame(result, frames, frame);
                        lines.Add($"  saved frame[{frames.Count - 1}] bytes={frame.Bytes.Length} dimensions={frame.Width}x{frame.Height} sha256={frame.Sha256}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (channelBytes.Length > 0)
        {
            var channelPath = Path.Combine(result.OutputDirectory, "managed-hlp2p-direct-channel.bin");
            await File.WriteAllBytesAsync(channelPath, channelBytes.ToArray(), ct).ConfigureAwait(false);
            AddArtifact(result, channelPath);
            lines.Add($"- saved channel bytes={channelBytes.Length}");

            if (frames.Count == 0)
            {
                foreach (var frame in ExtractJpegFrames(channelBytes.ToArray()))
                {
                    SaveFrame(result, frames, frame);
                    lines.Add($"  fallback saved frame[{frames.Count - 1}] bytes={frame.Bytes.Length} dimensions={frame.Width}x{frame.Height} sha256={frame.Sha256}");
                }
            }
        }

        if (frames.Count == 0)
            lines.Add(remotePacketCount == 0 ? "- no post-control remote packets" : "- no JPEG frames from managed direct transport");

        return frames;
    }

    private static async Task<bool> WaitForReadyAsync(
        UdpClient udp,
        IPEndPoint sessionRemote,
        IReadOnlyCollection<string> localIps,
        A9Vue990DirectCaptureResult result,
        ICollection<string> lines,
        CancellationToken ct)
    {
        using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readyTimeout.CancelAfter(TimeSpan.FromSeconds(4));
        while (!readyTimeout.IsCancellationRequested)
        {
            try
            {
                var received = await udp.ReceiveAsync(readyTimeout.Token).ConfigureAwait(false);
                if (localIps.Contains(received.RemoteEndPoint.Address.ToString()))
                    continue;

                SaveRawPacket(result, "hlp2p-direct-ready-rx", received.RemoteEndPoint, received.Buffer);
                lines.Add($"  ready rx from {received.RemoteEndPoint} bytes={received.Buffer.Length} prefix={ToHex(received.Buffer, 32)}");

                if (A9Vue990Hlp2pDirectPacket.TryParseLanHoleReady(received.Buffer, out var ready))
                {
                    lines.Add($"- parsed lan-hole ready aid=0x{ready.AidLittleEndian:X8}");
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        lines.Add("- no compact lan-hole ready packet");
        return false;
    }

    private static async Task CompleteAliveHandshakeAsync(
        UdpClient udp,
        IPEndPoint sessionRemote,
        IReadOnlyCollection<string> localIps,
        A9Vue990DirectCaptureResult result,
        ICollection<string> lines,
        CancellationToken ct)
    {
        await udp.SendAsync(A9Vue990Hlp2pDirectPacket.AliveProbe.ToArray(), sessionRemote, ct).ConfigureAwait(false);
        lines.Add("- sent compact alive probe 0B0000");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var received = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                if (localIps.Contains(received.RemoteEndPoint.Address.ToString()))
                    continue;

                SaveRawPacket(result, "hlp2p-direct-alive-rx", received.RemoteEndPoint, received.Buffer);
                lines.Add($"  alive rx from {received.RemoteEndPoint} bytes={received.Buffer.Length} prefix={ToHex(received.Buffer, 32)}");

                if (A9Vue990Hlp2pDirectPacket.IsAliveProbe(received.Buffer))
                {
                    await udp.SendAsync(A9Vue990Hlp2pDirectPacket.AliveAck.ToArray(), sessionRemote, timeout.Token)
                        .ConfigureAwait(false);
                    lines.Add("  sent compact alive ack 0C");
                    continue;
                }

                if (A9Vue990Hlp2pDirectPacket.IsAliveAck(received.Buffer))
                    break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task SendLegacyPreamblesAsync(
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
            foreach (var preamble in LegacyPreambles)
            {
                foreach (var target in targets)
                    await udp.SendAsync(preamble, target, ct).ConfigureAwait(false);
            }

            lines.Add($"- sent legacy preambles attempt={attemptName}");
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            lines.Add($"- legacy preamble send failed attempt={attemptName}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task SendPostHoleControlAsync(
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
        lines.Add($"- sent post-hole control[{control.Index}] name={control.Name} reason={reason} bytes={packet.Length}");
        await Task.Delay(45, ct).ConfigureAwait(false);
    }

    private static async Task TryFetchStatusAsync(string host, ICollection<string> lines, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var text = await http.GetStringAsync($"http://{host}:81/get_status.cgi", ct).ConfigureAwait(false);
            lines.Add($"- status bytes={Encoding.UTF8.GetByteCount(text)}");
            foreach (var name in new[] { "realdeviceid", "deviceid", "alias", "batteryRate", "isCharge" })
            {
                var value = ExtractJavaScriptValue(text, name);
                if (!string.IsNullOrWhiteSpace(value))
                    lines.Add($"  status {name}={value}");
            }
        }
        catch (Exception ex)
        {
            lines.Add($"- status fetch failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string? ExtractJavaScriptValue(string text, string name)
    {
        var pattern = "var " + name + "=";
        var index = text.IndexOf(pattern, StringComparison.Ordinal);
        if (index < 0)
            return null;

        index += pattern.Length;
        var end = text.IndexOf(';', index);
        if (end < 0)
            return null;

        return text[index..end].Trim().Trim('"');
    }

    private static IReadOnlyList<IPEndPoint> BuildLanHoleTargets(IPAddress hostAddress)
    {
        var addresses = new[] { IPAddress.Broadcast, IPAddress.Parse("192.168.168.255"), hostAddress };
        int[] ports = [65530, 65531];
        return addresses.Distinct().SelectMany(address => ports.Select(port => new IPEndPoint(address, port))).ToArray();
    }

    private static IReadOnlyList<LanHoleAttempt> BuildLanHoleAttempts()
    {
        return
        [
            new LanHoleAttempt(
                "observed-run2",
                new A9Vue990Hlp2pLanHoleSeed(
                    Convert.FromHexString("C1F3ECE4"),
                    Convert.FromHexString("8AB8F6F4"),
                    Convert.FromHexString("7A46F89D"),
                    Convert.FromHexString("B85C64E8"),
                    Convert.FromHexString("2EEA4A01"),
                    0x29E669CB)),
            new LanHoleAttempt(
                "observed-run1",
                new A9Vue990Hlp2pLanHoleSeed(
                    Convert.FromHexString("284A762B"),
                    Convert.FromHexString("B4A2BB48"),
                    Convert.FromHexString("707C9F63"),
                    Convert.FromHexString("1B9D3AB9"),
                    Convert.FromHexString("2DB65E01"),
                    0x24F4C665)),
            new LanHoleAttempt("random-shape", A9Vue990Hlp2pDirectPacket.CreateObservedShapeSeed()),
        ];
    }

    private static List<CapturedFrame> ExtractJpegFrames(byte[] bytes)
    {
        var frames = new List<CapturedFrame>();
        foreach (var extracted in A9Vue990ChannelMediaExtractor.ExtractJpegFrames(bytes).Take(12))
        {
            var (width, height) = TryReadJpegDimensions(extracted.Bytes);
            frames.Add(new CapturedFrame(extracted.Bytes, width, height, Convert.ToHexString(SHA256.HashData(extracted.Bytes)), null));
        }

        return frames;
    }

    private static void SaveFrame(A9Vue990DirectCaptureResult result, List<CapturedFrame> frames, CapturedFrame frame)
    {
        var path = Path.Combine(result.OutputDirectory, $"managed-hlp2p-direct-frame-{frames.Count:000}.jpg");
        File.WriteAllBytes(path, frame.Bytes);
        AddArtifact(result, path);
        frames.Add(frame with { LocalPath = path });
    }

    private static void SaveFirstImage(
        A9Vue990DirectCaptureResult result,
        IReadOnlyList<CapturedFrame> frames,
        ICollection<string> lines)
    {
        var path = Path.Combine(result.OutputDirectory, "managed-direct-still.jpg");
        File.WriteAllBytes(path, frames[0].Bytes);
        AddArtifact(result, path);
        lines.Add($"- imageCapture={path} bytes={frames[0].Bytes.Length} dimensions={frames[0].Width}x{frames[0].Height} sha256={frames[0].Sha256}");
    }

    private static void SaveMjpegAvi(
        A9Vue990DirectCaptureResult result,
        IReadOnlyList<CapturedFrame> frames,
        ICollection<string> lines)
    {
        var usable = frames
            .Where(frame => frame.Width > 0 && frame.Height > 0)
            .GroupBy(frame => (frame.Width, frame.Height))
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();
        if (usable is null)
        {
            lines.Add("- videoCapture=not saved, managed JPEG dimensions were unknown");
            return;
        }

        var path = Path.Combine(result.OutputDirectory, "managed-direct-video-mjpeg.avi");
        A9MjpegAviWriter.Write(path, usable.Select(frame => frame.Bytes).ToArray(), usable.Key.Width, usable.Key.Height, DefaultFramesPerSecond);
        AddArtifact(result, path);
        var bytes = File.ReadAllBytes(path);
        lines.Add($"- videoCapture={path} bytes={bytes.Length} frames={usable.Count()} dimensions={usable.Key.Width}x{usable.Key.Height} sha256={Convert.ToHexString(SHA256.HashData(bytes))}");
    }

    private static void SaveRawPacket(
        A9Vue990DirectCaptureResult result,
        string prefix,
        IPEndPoint remote,
        byte[] bytes)
    {
        var path = Path.Combine(
            result.OutputDirectory,
            $"{prefix}-{DateTimeOffset.Now:HHmmssfff}-{remote.Address.ToString().Replace('.', '-')}-{remote.Port}-{bytes.Length}.bin");
        File.WriteAllBytes(path, bytes);
        AddArtifact(result, path);
    }

    private static void AddArtifact(A9Vue990DirectCaptureResult result, string path)
    {
        var bytes = File.ReadAllBytes(path);
        result.Artifacts.Add(new A9Vue990DirectCaptureArtifact
        {
            LocalPath = path,
            Bytes = bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
        });
    }

    private static UdpClient CreateUdpClient(int? localPort)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        if (localPort is not null)
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Bind(new IPEndPoint(IPAddress.Any, localPort ?? 0));

        var udp = new UdpClient { Client = socket, EnableBroadcast = true };
        udp.Client.ReceiveBufferSize = Math.Max(udp.Client.ReceiveBufferSize, 1024 * 1024);
        udp.Client.SendBufferSize = Math.Max(udp.Client.SendBufferSize, 64 * 1024);
        return udp;
    }

    private static IReadOnlyList<string> GetLocalIpv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.Address.ToString())
            .Distinct()
            .ToArray();
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

    private static string ToHex(byte[] bytes, int maxBytes)
    {
        return Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, maxBytes))).ToLowerInvariant();
    }

    private static A9Vue990DirectCaptureResult Fail(
        A9Vue990DirectCaptureResult result,
        IReadOnlyCollection<string> lines,
        string error)
    {
        result.Success = false;
        result.Error = error;
        result.Report = string.Join(Environment.NewLine, lines.Concat(["- error: " + error]));
        return result;
    }

    private sealed record LanHoleAttempt(string Name, A9Vue990Hlp2pLanHoleSeed Seed);

    private sealed record CapturedFrame(byte[] Bytes, int Width, int Height, string Sha256, string? LocalPath);
}

public sealed class A9Vue990DirectCaptureOptions
{
    public string Host { get; init; } = "192.168.168.1";
    public string OutputDirectory { get; init; } = Path.Combine(
        Environment.CurrentDirectory,
        ".my",
        "plan",
        "m38-a9-camera",
        "captures",
        "phase-48-windows-direct");
    public bool CaptureImage { get; init; } = true;
    public bool CaptureVideo { get; init; } = true;
    public int StreamSeconds { get; init; } = 18;
    public int MaxFrames { get; init; } = 12;
}

public sealed class A9Vue990DirectCaptureResult
{
    public DateTimeOffset Timestamp { get; init; }
    public string Host { get; init; } = "192.168.168.1";
    public string OutputDirectory { get; init; } = string.Empty;
    public bool Success { get; set; }
    public bool CapturedImage { get; set; }
    public bool CapturedVideo { get; set; }
    public string? Error { get; set; }
    public string Report { get; set; } = string.Empty;
    public List<A9Vue990DirectCaptureArtifact> Artifacts { get; } = [];

    public string ToReadableString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("A9 Vue990 managed-direct C# capture");
        sb.AppendLine($"Timestamp: {Timestamp:O}");
        sb.AppendLine($"Success: {Success}");
        sb.AppendLine($"Captured image: {CapturedImage}");
        sb.AppendLine($"Captured video: {CapturedVideo}");
        sb.AppendLine($"Camera host: {Host}");
        sb.AppendLine($"Output: {OutputDirectory}");
        if (!string.IsNullOrWhiteSpace(Error))
            sb.AppendLine($"Error: {Error}");
        sb.AppendLine($"Artifacts: {Artifacts.Count}");
        foreach (var artifact in Artifacts)
            sb.AppendLine($"- {artifact.LocalPath} bytes={artifact.Bytes} sha256={artifact.Sha256}");
        return sb.ToString();
    }
}

public sealed class A9Vue990DirectCaptureArtifact
{
    public string LocalPath { get; init; } = string.Empty;
    public int Bytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}
