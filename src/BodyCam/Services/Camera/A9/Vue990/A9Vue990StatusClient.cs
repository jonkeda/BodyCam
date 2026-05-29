using System.Net;
using System.Text;

namespace BodyCam.Services.Camera.A9.Vue990;

public sealed class A9Vue990StatusClient
{
    public async Task<A9Vue990StatusResult> GetStatusAsync(
        A9Vue990StatusOptions options,
        CancellationToken ct = default)
    {
        var endpoint = BuildEndpoint(options);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Timeout);

        try
        {
            using var http = new HttpClient
            {
                Timeout = options.Timeout,
            };
            using var response = await http.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            var bytes = await ReadLimitedAsync(response.Content, options.MaxBytes, timeout.Token).ConfigureAwait(false);
            var body = Encoding.UTF8.GetString(bytes);
            var variables = JavaScriptVarParser.Parse(body);

            return new A9Vue990StatusResult
            {
                Timestamp = DateTimeOffset.Now,
                Endpoint = endpoint,
                Success = response.IsSuccessStatusCode && LooksLikeStatus(variables),
                HttpStatusCode = (int)response.StatusCode,
                ContentType = response.Content.Headers.ContentType?.MediaType,
                Bytes = bytes.Length,
                RawPrefix = Prefix(body, 256),
                Result = JavaScriptVarParser.GetString(variables, "result"),
                DeviceId = JavaScriptVarParser.GetString(variables, "deviceid"),
                RealDeviceId = JavaScriptVarParser.GetString(variables, "realdeviceid"),
                Alias = JavaScriptVarParser.GetString(variables, "alias"),
                SystemVersion = JavaScriptVarParser.GetString(variables, "sys_ver"),
                AppVersion = JavaScriptVarParser.GetString(variables, "appver"),
                Server = JavaScriptVarParser.GetString(variables, "server"),
                SupportVuid = JavaScriptVarParser.GetString(variables, "support_vuid"),
                VuidResult = JavaScriptVarParser.GetString(variables, "vuidResult"),
                BatteryRate = JavaScriptVarParser.GetString(variables, "batteryRate"),
                CurrentUsers = JavaScriptVarParser.GetString(variables, "current_users"),
                Variables = variables,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new A9Vue990StatusResult
            {
                Timestamp = DateTimeOffset.Now,
                Endpoint = endpoint,
                Success = false,
                Error = $"{ex.GetType().Name}: {ex.Message}",
            };
        }
    }

    public static Uri BuildEndpoint(A9Vue990StatusOptions options)
    {
        var builder = new UriBuilder("http", options.Host, options.Port, "get_status.cgi")
        {
            Query = $"loginuse={Uri.EscapeDataString(options.Username)}&loginpas={Uri.EscapeDataString(options.Password)}",
        };
        return builder.Uri;
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[4096];

        while (memory.Length < maxBytes)
        {
            var remaining = Math.Min(buffer.Length, maxBytes - (int)memory.Length);
            var read = await stream.ReadAsync(buffer.AsMemory(0, remaining), ct).ConfigureAwait(false);
            if (read == 0)
                break;

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private static bool LooksLikeStatus(IReadOnlyDictionary<string, string> variables)
    {
        return variables.ContainsKey("result") &&
               (variables.ContainsKey("deviceid") ||
                variables.ContainsKey("realdeviceid") ||
                variables.ContainsKey("sys_ver"));
    }

    private static string Prefix(string value, int max)
    {
        if (value.Length <= max)
            return value;

        return value[..max];
    }

    private static class JavaScriptVarParser
    {
        public static IReadOnlyDictionary<string, string> Parse(string body)
        {
            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in body.Replace("\r\n", "\n").Split('\n'))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("var ", StringComparison.OrdinalIgnoreCase))
                    continue;

                var equals = line.IndexOf('=');
                if (equals < 5)
                    continue;

                var name = line[4..equals].Trim();
                var value = line[(equals + 1)..].Trim().TrimEnd(';').Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = WebUtility.HtmlDecode(value[1..^1]);

                if (!string.IsNullOrWhiteSpace(name))
                    variables[name] = value;
            }

            return variables;
        }

        public static string? GetString(IReadOnlyDictionary<string, string> variables, string name)
        {
            return variables.TryGetValue(name, out var value) ? value : null;
        }
    }
}

public sealed class A9Vue990StatusOptions
{
    public string Host { get; init; } = "192.168.168.1";

    public int Port { get; init; } = 81;

    public string Username { get; init; } = "admin";

    public string Password { get; init; } = "888888";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public int MaxBytes { get; init; } = 512 * 1024;
}

public sealed class A9Vue990StatusResult
{
    public DateTimeOffset Timestamp { get; init; }

    public required Uri Endpoint { get; init; }

    public bool Success { get; init; }

    public int? HttpStatusCode { get; init; }

    public string? ContentType { get; init; }

    public int Bytes { get; init; }

    public string? Error { get; init; }

    public string? Result { get; init; }

    public string? DeviceId { get; init; }

    public string? RealDeviceId { get; init; }

    public string? Alias { get; init; }

    public string? SystemVersion { get; init; }

    public string? AppVersion { get; init; }

    public string? Server { get; init; }

    public string? SupportVuid { get; init; }

    public string? VuidResult { get; init; }

    public string? BatteryRate { get; init; }

    public string? CurrentUsers { get; init; }

    public string? RawPrefix { get; init; }

    public IReadOnlyDictionary<string, string> Variables { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string ToReadableString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("A9 Vue990 status");
        sb.AppendLine($"Timestamp: {Timestamp:O}");
        sb.AppendLine($"Endpoint: {Endpoint}");
        sb.AppendLine($"Success: {Success}");

        if (Error is not null)
        {
            sb.AppendLine($"Error: {Error}");
            return sb.ToString();
        }

        sb.AppendLine($"HTTP: {HttpStatusCode} content-type={ContentType ?? "<none>"} bytes={Bytes}");
        sb.AppendLine($"Device: deviceid={DeviceId ?? "<none>"} realdeviceid={RealDeviceId ?? "<none>"} alias={Alias ?? "<none>"}");
        sb.AppendLine($"Version: sys={SystemVersion ?? "<none>"} app={AppVersion ?? "<none>"}");
        sb.AppendLine($"VUID: support={SupportVuid ?? "<none>"} result={VuidResult ?? "<none>"}");
        sb.AppendLine($"Users/battery: current_users={CurrentUsers ?? "<none>"} battery={BatteryRate ?? "<none>"}");
        sb.AppendLine($"Server: len={Server?.Length ?? 0} prefix={Prefix(Server, 32)}");
        return sb.ToString();
    }

    private static string Prefix(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
            return "<none>";

        var prefix = value[..Math.Min(max, value.Length)];
        return value.Length > max ? $"{prefix}..." : prefix;
    }
}
