using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace BodyCam.Services.Camera.A9.Vue990;

public sealed class A9Vue990HttpMediaProbeClient
{
    private static readonly string[] DefaultPaths =
    [
        "/livestream.cgi?streamid=10&substream=0&",
        "/livestream.cgi?streamid=10&substream=1&",
        "/livestream.cgi?streamid=11&substream=0&",
        "/videostream.cgi?streamid=10&substream=0&",
        "/video.cgi",
        "/video",
        "/mjpeg",
        "/mjpegstream.cgi",
        "/snapshot.cgi",
        "/snapshot.jpg",
        "/image.jpg",
        "/live.jpg",
        "/tmpfs/snap.jpg",
        "/webcapture.jpg?command=snap&channel=1",
        "/cgi-bin/snapshot.cgi",
        "/get_status.cgi",
    ];

    public async Task<A9Vue990HttpMediaProbeResult> ProbeAsync(
        A9Vue990HttpMediaProbeOptions options,
        CancellationToken ct = default)
    {
        var result = new A9Vue990HttpMediaProbeResult
        {
            Timestamp = DateTimeOffset.Now,
            Host = options.Host,
            Port = options.Port,
        };

        if (!string.IsNullOrWhiteSpace(options.OutputDirectory))
            Directory.CreateDirectory(options.OutputDirectory);

        using var http = new HttpClient
        {
            Timeout = options.EndpointTimeout + options.ReadDuration + TimeSpan.FromSeconds(1),
        };

        foreach (var endpoint in BuildEndpoints(options))
        {
            var attempt = await ProbeEndpointAsync(http, endpoint, options, ct).ConfigureAwait(false);
            result.Attempts.Add(attempt);

            if (options.StopAfterFirstImage && attempt.JpegFrames.Count > 0)
                break;
        }

        var allFrames = result.Attempts
            .SelectMany(attempt => attempt.JpegFrames.Select(frame => frame.Bytes))
            .ToList();
        result.TotalJpegFrames = allFrames.Count;

        if (!string.IsNullOrWhiteSpace(options.OutputDirectory) && allFrames.Count > 0)
            SaveMediaArtifacts(result, options, allFrames);

        return result;
    }

    private static async Task<A9Vue990HttpMediaProbeAttempt> ProbeEndpointAsync(
        HttpClient http,
        Uri endpoint,
        A9Vue990HttpMediaProbeOptions options,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.EndpointTimeout + options.ReadDuration);

        var attempt = new A9Vue990HttpMediaProbeAttempt
        {
            Endpoint = endpoint,
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token)
                .ConfigureAwait(false);

            attempt.HttpStatusCode = (int)response.StatusCode;
            attempt.ContentType = response.Content.Headers.ContentType?.MediaType;

            var bytes = await ReadBoundedAsync(response.Content, options, timeout.Token).ConfigureAwait(false);
            attempt.Bytes = bytes.Length;
            attempt.PrefixHex = ToHex(bytes, 24);
            attempt.PrefixText = ToSafeText(bytes, 160);
            attempt.Sha256 = bytes.Length == 0 ? null : Convert.ToHexString(SHA256.HashData(bytes));
            attempt.JpegFrames.AddRange(ExtractJpegFrames(bytes, options.MaxJpegFramesPerEndpoint));
            attempt.LooksLikeVideo = LooksLikeVideo(bytes, attempt.ContentType);
            attempt.LooksLikeTextStatus = LooksLikeTextStatus(bytes, attempt.ContentType);

            if (options.SaveRawSamples &&
                !string.IsNullOrWhiteSpace(options.OutputDirectory) &&
                bytes.Length > 0 &&
                ShouldSaveRawSample(attempt))
            {
                attempt.RawSamplePath = SaveRawSample(options.OutputDirectory, endpoint, bytes);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            attempt.Error = $"{ex.GetType().Name}: {ex.Message}";
        }

        return attempt;
    }

    private static IEnumerable<Uri> BuildEndpoints(A9Vue990HttpMediaProbeOptions options)
    {
        var paths = options.Paths.Count == 0 ? DefaultPaths : options.Paths;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            foreach (var authQuery in BuildAuthQueries(options))
            {
                var endpoint = BuildEndpoint(options.Host, options.Port, path, authQuery);
                if (seen.Add(endpoint.AbsoluteUri))
                    yield return endpoint;
            }
        }
    }

    private static string[] BuildAuthQueries(A9Vue990HttpMediaProbeOptions options)
    {
        var username = Uri.EscapeDataString(options.Username);
        var password = Uri.EscapeDataString(options.Password);
        return
        [
            string.Empty,
            $"loginuse={username}&loginpas={password}",
            $"user={username}&pwd={password}",
            $"usr={username}&pwd={password}",
        ];
    }

    private static Uri BuildEndpoint(string host, int port, string path, string authQuery)
    {
        if (!path.StartsWith('/'))
            path = "/" + path;

        if (!string.IsNullOrWhiteSpace(authQuery))
        {
            if (path.EndsWith("?", StringComparison.Ordinal) ||
                path.EndsWith("&", StringComparison.Ordinal))
            {
                path += authQuery;
            }
            else
            {
                path += path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                path += authQuery;
            }
        }

        var queryStart = path.IndexOf('?', StringComparison.Ordinal);
        var builder = new UriBuilder("http", host, port)
        {
            Path = queryStart < 0 ? path : path[..queryStart],
            Query = queryStart < 0 ? string.Empty : path[(queryStart + 1)..],
        };
        return builder.Uri;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        A9Vue990HttpMediaProbeOptions options,
        CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        var stopAt = DateTimeOffset.UtcNow + options.ReadDuration;

        while (memory.Length < options.MaxBytes && DateTimeOffset.UtcNow < stopAt)
        {
            var remaining = Math.Min(buffer.Length, options.MaxBytes - (int)memory.Length);
            try
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, remaining), ct).ConfigureAwait(false);
                if (read == 0)
                    break;

                memory.Write(buffer, 0, read);
            }
            catch (OperationCanceledException) when (memory.Length > 0)
            {
                break;
            }
        }

        return memory.ToArray();
    }

    private static List<A9Vue990JpegFrame> ExtractJpegFrames(byte[] bytes, int maxFrames)
    {
        var frames = new List<A9Vue990JpegFrame>();
        var offset = 0;

        while (frames.Count < maxFrames)
        {
            var start = IndexOf(bytes, [0xff, 0xd8], offset);
            if (start < 0)
                break;

            var end = IndexOf(bytes, [0xff, 0xd9], start + 2);
            if (end < 0)
                break;

            var frame = bytes[start..(end + 2)];
            var dimensions = TryReadJpegDimensions(frame);
            frames.Add(new A9Vue990JpegFrame
            {
                Offset = start,
                Bytes = frame,
                Width = dimensions.Width,
                Height = dimensions.Height,
                Sha256 = Convert.ToHexString(SHA256.HashData(frame)),
            });

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

            if (IsStartOfFrame(marker) && length >= 7)
            {
                var height = (jpeg[i + 3] << 8) | jpeg[i + 4];
                var width = (jpeg[i + 5] << 8) | jpeg[i + 6];
                return (width, height);
            }

            i += length;
        }

        return (0, 0);
    }

    private static bool IsStartOfFrame(byte marker)
    {
        return marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7
            or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf;
    }

    private static void SaveMediaArtifacts(
        A9Vue990HttpMediaProbeResult result,
        A9Vue990HttpMediaProbeOptions options,
        IReadOnlyList<byte[]> frames)
    {
        var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd-HHmmss");
        var first = result.Attempts.SelectMany(attempt => attempt.JpegFrames).First();
        var imagePath = Path.Combine(options.OutputDirectory!, $"a9-windows-http-{stamp}.jpg");
        File.WriteAllBytes(imagePath, first.Bytes);
        result.ImagePath = imagePath;

        if (frames.Count < 2)
            return;

        var width = first.Width > 0 ? first.Width : 640;
        var height = first.Height > 0 ? first.Height : 480;
        var videoPath = Path.Combine(options.OutputDirectory!, $"a9-windows-http-{stamp}-mjpeg.avi");
        A9MjpegAviWriter.Write(videoPath, frames, width, height, options.FramesPerSecond);
        result.VideoPath = videoPath;
    }

    private static bool ShouldSaveRawSample(A9Vue990HttpMediaProbeAttempt attempt)
    {
        return attempt.JpegFrames.Count > 0 ||
               attempt.LooksLikeVideo ||
               !attempt.LooksLikeTextStatus;
    }

    private static string SaveRawSample(string outputDirectory, Uri endpoint, byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint.AbsoluteUri)))[..12];
        var path = Path.Combine(outputDirectory, $"a9-windows-http-sample-{hash}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static bool LooksLikeVideo(byte[] bytes, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            (contentType.Contains("video", StringComparison.OrdinalIgnoreCase) ||
             contentType.Contains("multipart/x-mixed-replace", StringComparison.OrdinalIgnoreCase) ||
             contentType.Contains("image/jpeg", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return bytes.Length >= 12 &&
               (StartsWith(bytes, Encoding.ASCII.GetBytes("RIFF")) ||
                StartsWith(bytes.AsSpan(4).ToArray(), Encoding.ASCII.GetBytes("ftyp")) ||
                StartsWith(bytes, [0x00, 0x00, 0x00, 0x01]) ||
                StartsWith(bytes, [0x00, 0x00, 0x01]));
    }

    private static bool LooksLikeTextStatus(byte[] bytes, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            !contentType.Contains("text", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var text = ToSafeText(bytes, 256) ?? string.Empty;
        return text.Contains("var result=", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWith(byte[] bytes, byte[] prefix)
    {
        return bytes.Length >= prefix.Length && bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix);
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

    private static string? ToHex(byte[] bytes, int max)
    {
        if (bytes.Length == 0)
            return null;

        return string.Join(" ", bytes.Take(max).Select(value => value.ToString("X2")));
    }

    private static string? ToSafeText(byte[] bytes, int max)
    {
        if (bytes.Length == 0)
            return null;

        var text = Encoding.UTF8.GetString(bytes, 0, Math.Min(max, bytes.Length));
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
            builder.Append(char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t' ? '.' : ch);

        return builder.ToString();
    }
}

public sealed class A9Vue990HttpMediaProbeOptions
{
    public string Host { get; init; } = "192.168.168.1";

    public int Port { get; init; } = 81;

    public string Username { get; init; } = "admin";

    public string Password { get; init; } = "888888";

    public TimeSpan EndpointTimeout { get; init; } = TimeSpan.FromSeconds(4);

    public TimeSpan ReadDuration { get; init; } = TimeSpan.FromSeconds(2);

    public int MaxBytes { get; init; } = 1024 * 1024;

    public int MaxJpegFramesPerEndpoint { get; init; } = 8;

    public int FramesPerSecond { get; init; } = 2;

    public bool StopAfterFirstImage { get; init; } = false;

    public bool SaveRawSamples { get; init; } = false;

    public string? OutputDirectory { get; init; }

    public IReadOnlyList<string> Paths { get; init; } = [];
}

public sealed class A9Vue990HttpMediaProbeResult
{
    public DateTimeOffset Timestamp { get; init; }

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; }

    public List<A9Vue990HttpMediaProbeAttempt> Attempts { get; } = [];

    public int TotalJpegFrames { get; set; }

    public string? ImagePath { get; set; }

    public string? VideoPath { get; set; }

    public bool CapturedImage => !string.IsNullOrWhiteSpace(ImagePath);

    public bool CapturedVideo => !string.IsNullOrWhiteSpace(VideoPath);

    public string ToReadableString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("A9 Vue990 HTTP media probe");
        sb.AppendLine($"Timestamp: {Timestamp:O}");
        sb.AppendLine($"Target: {Host}:{Port}");
        sb.AppendLine($"Attempts: {Attempts.Count}");
        sb.AppendLine($"JPEG frames: {TotalJpegFrames}");
        sb.AppendLine($"Image: {ImagePath ?? "<none>"}");
        sb.AppendLine($"Video: {VideoPath ?? "<none>"}");

        foreach (var attempt in Attempts)
        {
            var status = attempt.Error is null ? $"HTTP {attempt.HttpStatusCode}" : attempt.Error;
            sb.AppendLine(
                $"- {attempt.Endpoint} -> {status}; type={attempt.ContentType ?? "<none>"}; bytes={attempt.Bytes}; " +
                $"jpeg={attempt.JpegFrames.Count}; videoLike={attempt.LooksLikeVideo}; raw={attempt.RawSamplePath ?? "<none>"}");
        }

        return sb.ToString();
    }
}

public sealed class A9Vue990HttpMediaProbeAttempt
{
    public required Uri Endpoint { get; init; }

    public int? HttpStatusCode { get; set; }

    public string? ContentType { get; set; }

    public int Bytes { get; set; }

    public string? PrefixHex { get; set; }

    public string? PrefixText { get; set; }

    public string? Sha256 { get; set; }

    public string? Error { get; set; }

    public bool LooksLikeVideo { get; set; }

    public bool LooksLikeTextStatus { get; set; }

    public string? RawSamplePath { get; set; }

    public List<A9Vue990JpegFrame> JpegFrames { get; } = [];
}

public sealed class A9Vue990JpegFrame
{
    public int Offset { get; init; }

    [JsonIgnore]
    public byte[] Bytes { get; init; } = [];

    public int Size => Bytes.Length;

    public int Width { get; init; }

    public int Height { get; init; }

    public required string Sha256 { get; init; }
}
