using System.Text;

namespace BodyCam.Services.Camera.A9.Probe;

public enum A9ProbeProtocol
{
    None,
    Auto,
    Rtsp,
    HttpMjpeg,
    V720Naxclow,
    PpppUdpMjpeg,
    PpppUdp20190,
}

public enum A9ProbeStatus
{
    Skipped,
    Timeout,
    Closed,
    Open,
    Responded,
    FrameReceived,
    Failed,
    Unsupported,
}

public sealed class A9ProbeOptions
{
    public string? Host { get; init; }

    public IReadOnlyList<string> Hosts { get; init; } = [];

    public A9ProbeProtocol Protocol { get; init; } = A9ProbeProtocol.Auto;

    public int TimeoutMs { get; init; } = 1200;

    public bool FirstFrame { get; init; }

    public string Username { get; init; } = "admin";

    public string Password { get; init; } = "admin";
}

public sealed class A9ProbeResult
{
    public DateTimeOffset Timestamp { get; init; }

    public List<A9LocalInterfaceInfo> LocalInterfaces { get; init; } = [];

    public List<string> CandidateHosts { get; init; } = [];

    public List<A9ProtocolProbeResult> Probes { get; init; } = [];

    public A9ProbeProtocol SelectedProtocol { get; set; } = A9ProbeProtocol.None;

    public string? SelectedHost { get; set; }

    public A9FrameProbeResult? Frame { get; set; }

    public bool Success => SelectedProtocol != A9ProbeProtocol.None;

    public string ToReadableString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("A9 probe");
        sb.AppendLine($"Timestamp: {Timestamp:O}");

        if (LocalInterfaces.Count == 0)
        {
            sb.AppendLine("Local IPv4: none");
        }
        else
        {
            foreach (var local in LocalInterfaces)
            {
                var gateways = local.Gateways.Count == 0
                    ? "no gateway"
                    : string.Join(", ", local.Gateways);
                sb.AppendLine($"Local IPv4: {local.Address}/{local.PrefixLength} on {local.Name} ({gateways})");
            }
        }

        sb.AppendLine("Candidates: " + (CandidateHosts.Count == 0
            ? "none"
            : string.Join(", ", CandidateHosts)));
        sb.AppendLine();

        foreach (var probe in Probes)
        {
            var endpoint = string.IsNullOrWhiteSpace(probe.Endpoint)
                ? $"{probe.Host}:{probe.Port}"
                : probe.Endpoint;
            var device = string.IsNullOrWhiteSpace(probe.DeviceId)
                ? string.Empty
                : $" device={probe.DeviceId}";
            sb.AppendLine($"[{Label(probe.Protocol)}] {endpoint} {probe.Status}: {probe.Message}{device}");
        }

        sb.AppendLine();
        sb.AppendLine(SelectedProtocol == A9ProbeProtocol.None
            ? "Selected: none"
            : $"Selected: {Label(SelectedProtocol)} on {SelectedHost}");

        if (Frame is not null)
        {
            var frameState = Frame.Success
                ? $"JPEG={Frame.IsJpeg}, bytes={Frame.Bytes}"
                : Frame.Skipped
                    ? "skipped"
                    : "failed";
            sb.AppendLine($"Frame: {frameState} ({Frame.Message})");
        }

        return sb.ToString();
    }

    public static string Label(A9ProbeProtocol protocol) => protocol switch
    {
        A9ProbeProtocol.Rtsp => "rtsp",
        A9ProbeProtocol.HttpMjpeg => "http-mjpeg",
        A9ProbeProtocol.V720Naxclow => "v720-naxclow",
        A9ProbeProtocol.PpppUdpMjpeg => "pppp-udp-32108",
        A9ProbeProtocol.PpppUdp20190 => "pppp-udp-20190",
        A9ProbeProtocol.Auto => "auto",
        _ => "none",
    };
}

public sealed class A9LocalInterfaceInfo
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public required string Address { get; init; }

    public int PrefixLength { get; init; }

    public List<string> Gateways { get; init; } = [];

    public string? Broadcast { get; init; }
}

public sealed class A9ProtocolProbeResult
{
    public required A9ProbeProtocol Protocol { get; init; }

    public string? Host { get; init; }

    public int? Port { get; init; }

    public string? Endpoint { get; init; }

    public A9ProbeStatus Status { get; init; }

    public bool IsSupported { get; init; }

    public required string Message { get; init; }

    public string? DeviceId { get; init; }

    public int? HttpStatusCode { get; init; }

    public string? ContentType { get; init; }

    public long DurationMs { get; init; }
}

public sealed class A9FrameProbeResult
{
    public A9ProbeProtocol Protocol { get; init; }

    public string? Host { get; init; }

    public bool Success { get; init; }

    public bool Skipped { get; init; }

    public bool IsJpeg { get; init; }

    public int Bytes { get; init; }

    public long DurationMs { get; init; }

    public required string Message { get; init; }
}
