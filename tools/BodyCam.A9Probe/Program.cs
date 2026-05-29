using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BodyCam.Services.Camera.A9.Probe;
using BodyCam.Services.Camera.A9.Vue990;

var command = args.FirstOrDefault();
if (command is null or "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

if (!string.Equals(command, "probe", StringComparison.OrdinalIgnoreCase))
{
    if (string.Equals(command, "wifi-ap", StringComparison.OrdinalIgnoreCase))
    {
        return await RunWifiApCommandAsync(args.Skip(1).ToArray());
    }

    if (string.Equals(command, "vue990-status", StringComparison.OrdinalIgnoreCase))
    {
        return await RunVue990StatusCommandAsync(args.Skip(1).ToArray());
    }

    if (string.Equals(command, "vue990-das", StringComparison.OrdinalIgnoreCase))
    {
        return await RunVue990DasCommandAsync(args.Skip(1).ToArray());
    }

    if (string.Equals(command, "vue990-ppcs-transport", StringComparison.OrdinalIgnoreCase))
    {
        return await RunVue990PpcsTransportCommandAsync(args.Skip(1).ToArray());
    }

    if (string.Equals(command, "vue990-relay-hello", StringComparison.OrdinalIgnoreCase))
    {
        return await RunVue990RelayHelloCommandAsync(args.Skip(1).ToArray());
    }

    if (string.Equals(command, "vue990-android-capture", StringComparison.OrdinalIgnoreCase))
    {
        return await RunVue990AndroidCaptureCommandAsync(args.Skip(1).ToArray());
    }

    if (string.Equals(command, "vue990-android-channel-oracle", StringComparison.OrdinalIgnoreCase))
    {
        return await RunVue990AndroidCaptureCommandAsync(args.Skip(1).ToArray(), channelOracleDefault: true);
    }

    if (string.Equals(command, "vue990-android-managed-direct", StringComparison.OrdinalIgnoreCase))
    {
        return await RunVue990AndroidManagedDirectCommandAsync(args.Skip(1).ToArray());
    }

    if (string.Equals(command, "vue990-http-media", StringComparison.OrdinalIgnoreCase))
    {
        return await RunVue990HttpMediaCommandAsync(args.Skip(1).ToArray());
    }

    if (string.Equals(command, "vue990-direct-capture", StringComparison.OrdinalIgnoreCase))
    {
        return await RunVue990DirectCaptureCommandAsync(args.Skip(1).ToArray());
    }

    if (string.Equals(command, "mjpeg-avi", StringComparison.OrdinalIgnoreCase))
    {
        return RunMjpegAviCommand(args.Skip(1).ToArray());
    }

    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return 2;
}

var parse = ParseProbeOptions(args.Skip(1).ToArray());
if (parse.Error is not null)
{
    Console.Error.WriteLine(parse.Error);
    PrintUsage();
    return 2;
}

var runner = new A9ProbeRunner();
var result = await runner.RunAsync(parse.Options, Console.WriteLine);
Console.WriteLine();
Console.WriteLine(result.ToReadableString());

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true,
};
jsonOptions.Converters.Add(new JsonStringEnumConverter());

if (parse.Json || parse.OutputPath is not null)
{
    var json = JsonSerializer.Serialize(result, jsonOptions);
    if (parse.OutputPath is not null)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(parse.OutputPath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(parse.OutputPath, json);
        Console.WriteLine($"JSON written: {parse.OutputPath}");
    }
    else
    {
        Console.WriteLine(json);
    }
}

if (result.Frame is { Success: false, Skipped: false })
    return 1;

return 0;

static async Task<int> RunWifiApCommandAsync(string[] args)
{
    var ssid = "@MC-0025644";
    var runProtocolProbe = true;
    var connect = false;
    var connectTimeoutSeconds = 30;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--ssid":
                if (!TryReadValue(args, ref i, out ssid))
                {
                    Console.Error.WriteLine("--ssid requires a value.");
                    return 2;
                }
                break;

            case "--no-protocol-probe":
                runProtocolProbe = false;
                break;

            case "--connect":
                connect = true;
                break;

            case "--connect-timeout":
                if (!TryReadValue(args, ref i, out var timeoutValue) ||
                    !int.TryParse(timeoutValue, out connectTimeoutSeconds) ||
                    connectTimeoutSeconds < 1)
                {
                    Console.Error.WriteLine("--connect-timeout requires an integer value >= 1.");
                    return 2;
                }
                break;

            default:
                Console.Error.WriteLine($"Unknown wifi-ap option: {args[i]}");
                return 2;
        }
    }

    var wifiApProbe = new WifiApProbe();
    var wifiProbe = await wifiApProbe.RunAsync(ssid, runProtocolProbe);
    Console.WriteLine(wifiProbe.ToReadableString());

    if (connect && !wifiProbe.IsConnected)
    {
        Console.WriteLine();
        var connectResult = await wifiApProbe.ConnectAsync(ssid, TimeSpan.FromSeconds(connectTimeoutSeconds));
        Console.WriteLine(connectResult.ToReadableString());
        wifiProbe = connectResult.Probe;
    }

    if (wifiProbe.ShouldRunProtocolProbe)
    {
        Console.WriteLine();
        var probeOptions = new A9ProbeOptions
        {
            Hosts = ["192.168.1.1", "192.168.169.1", "192.168.4.1"],
            FirstFrame = true,
            TimeoutMs = 1200,
        };
        var result = await new A9ProbeRunner().RunAsync(probeOptions, Console.WriteLine);
        Console.WriteLine();
        Console.WriteLine(result.ToReadableString());
    }

    return wifiProbe.IsVisible || wifiProbe.Profile?.IsSaved == true ? 0 : 1;
}

static int RunMjpegAviCommand(string[] args)
{
    string? inputDir = null;
    string? outputPath = null;
    var pattern = "frame-*.jpg";
    var width = 640;
    var height = 480;
    var fps = 2;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--input-dir":
                if (!TryReadValue(args, ref i, out inputDir))
                {
                    Console.Error.WriteLine("--input-dir requires a directory path.");
                    return 2;
                }
                break;

            case "--output":
                if (!TryReadValue(args, ref i, out outputPath))
                {
                    Console.Error.WriteLine("--output requires a file path.");
                    return 2;
                }
                break;

            case "--pattern":
                if (!TryReadValue(args, ref i, out pattern))
                {
                    Console.Error.WriteLine("--pattern requires a glob pattern.");
                    return 2;
                }
                break;

            case "--width":
                if (!TryReadValue(args, ref i, out var widthValue) ||
                    !int.TryParse(widthValue, out width) ||
                    width < 1)
                {
                    Console.Error.WriteLine("--width requires an integer value >= 1.");
                    return 2;
                }
                break;

            case "--height":
                if (!TryReadValue(args, ref i, out var heightValue) ||
                    !int.TryParse(heightValue, out height) ||
                    height < 1)
                {
                    Console.Error.WriteLine("--height requires an integer value >= 1.");
                    return 2;
                }
                break;

            case "--fps":
                if (!TryReadValue(args, ref i, out var fpsValue) ||
                    !int.TryParse(fpsValue, out fps) ||
                    fps < 1)
                {
                    Console.Error.WriteLine("--fps requires an integer value >= 1.");
                    return 2;
                }
                break;

            default:
                Console.Error.WriteLine($"Unknown mjpeg-avi option: {args[i]}");
                return 2;
        }
    }

    if (string.IsNullOrWhiteSpace(inputDir))
    {
        Console.Error.WriteLine("--input-dir is required.");
        return 2;
    }

    if (!Directory.Exists(inputDir))
    {
        Console.Error.WriteLine($"Input directory does not exist: {inputDir}");
        return 1;
    }

    if (string.IsNullOrWhiteSpace(outputPath))
    {
        Console.Error.WriteLine("--output is required.");
        return 2;
    }

    var files = Directory.EnumerateFiles(inputDir, pattern)
        .Where(path => !Path.GetFileName(path).EndsWith("_smail", StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (files.Length == 0)
    {
        Console.Error.WriteLine($"No frame files matched {pattern} in {inputDir}.");
        return 1;
    }

    var frames = files.Select(File.ReadAllBytes).ToArray();
    A9MjpegAviWriter.Write(outputPath, frames, width, height, fps);

    var output = new FileInfo(outputPath);
    Console.WriteLine("MJPEG AVI written");
    Console.WriteLine($"Input: {inputDir}");
    Console.WriteLine($"Frames: {frames.Length}");
    Console.WriteLine($"Size: {output.Length}");
    Console.WriteLine($"Output: {output.FullName}");
    return 0;
}

static async Task<int> RunVue990StatusCommandAsync(string[] args)
{
    var host = Environment.GetEnvironmentVariable("A9_CAMERA_IP") ?? "192.168.168.1";
    var port = 81;
    var username = Environment.GetEnvironmentVariable("A9_CAMERA_USERNAME") ?? "admin";
    var password = Environment.GetEnvironmentVariable("A9_CAMERA_PASSWORD") ?? "888888";
    var timeoutSeconds = 5;
    var json = false;
    string? outputPath = null;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--host":
                if (!TryReadValue(args, ref i, out host))
                {
                    Console.Error.WriteLine("--host requires a value.");
                    return 2;
                }
                break;

            case "--port":
                if (!TryReadValue(args, ref i, out var portValue) ||
                    !int.TryParse(portValue, out port) ||
                    port is < 1 or > 65535)
                {
                    Console.Error.WriteLine("--port requires a valid TCP port.");
                    return 2;
                }
                break;

            case "--username":
                if (!TryReadValue(args, ref i, out username))
                {
                    Console.Error.WriteLine("--username requires a value.");
                    return 2;
                }
                break;

            case "--password":
                if (!TryReadValue(args, ref i, out password))
                {
                    Console.Error.WriteLine("--password requires a value.");
                    return 2;
                }
                break;

            case "--timeout":
            case "--timeout-seconds":
                if (!TryReadValue(args, ref i, out var timeoutValue) ||
                    !int.TryParse(timeoutValue, out timeoutSeconds) ||
                    timeoutSeconds < 1)
                {
                    Console.Error.WriteLine("--timeout requires an integer value >= 1.");
                    return 2;
                }
                break;

            case "--json":
                json = true;
                break;

            case "--output":
                if (!TryReadValue(args, ref i, out outputPath))
                {
                    Console.Error.WriteLine("--output requires a file path.");
                    return 2;
                }
                break;

            default:
                Console.Error.WriteLine($"Unknown vue990-status option: {args[i]}");
                return 2;
        }
    }

    var topology = WindowsTopology.Capture();
    var status = await new A9Vue990StatusClient().GetStatusAsync(new A9Vue990StatusOptions
    {
        Host = host,
        Port = port,
        Username = string.IsNullOrWhiteSpace(username) ? "admin" : username,
        Password = string.IsNullOrWhiteSpace(password) ? "888888" : password,
        Timeout = TimeSpan.FromSeconds(timeoutSeconds),
    });

    Console.WriteLine(WindowsTopology.ToReadableString(topology));
    Console.WriteLine(status.ToReadableString());

    if (json || outputPath is not null)
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var payload = JsonSerializer.Serialize(new
        {
            topology,
            status,
        }, jsonOptions);

        if (outputPath is not null)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(outputPath, payload);
            Console.WriteLine($"JSON written: {outputPath}");
        }
        else
        {
            Console.WriteLine(payload);
        }
    }

    return status.Success ? 0 : 1;
}

static async Task<int> RunVue990DasCommandAsync(string[] args)
{
    var host = Environment.GetEnvironmentVariable("A9_CAMERA_IP") ?? "192.168.168.1";
    var port = 81;
    var username = Environment.GetEnvironmentVariable("A9_CAMERA_USERNAME") ?? "admin";
    var password = Environment.GetEnvironmentVariable("A9_CAMERA_PASSWORD") ?? "888888";
    var timeoutSeconds = 5;
    var json = false;
    string? outputPath = null;
    string? server = null;
    string? replaceRelays = null;
    var serverOnly = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--server":
                if (!TryReadValue(args, ref i, out server))
                {
                    Console.Error.WriteLine("--server requires a DAS value.");
                    return 2;
                }
                break;

            case "--host":
                if (!TryReadValue(args, ref i, out host))
                {
                    Console.Error.WriteLine("--host requires a value.");
                    return 2;
                }
                break;

            case "--port":
                if (!TryReadValue(args, ref i, out var portValue) ||
                    !int.TryParse(portValue, out port) ||
                    port is < 1 or > 65535)
                {
                    Console.Error.WriteLine("--port requires a valid TCP port.");
                    return 2;
                }
                break;

            case "--username":
                if (!TryReadValue(args, ref i, out username))
                {
                    Console.Error.WriteLine("--username requires a value.");
                    return 2;
                }
                break;

            case "--password":
                if (!TryReadValue(args, ref i, out password))
                {
                    Console.Error.WriteLine("--password requires a value.");
                    return 2;
                }
                break;

            case "--timeout":
            case "--timeout-seconds":
                if (!TryReadValue(args, ref i, out var timeoutValue) ||
                    !int.TryParse(timeoutValue, out timeoutSeconds) ||
                    timeoutSeconds < 1)
                {
                    Console.Error.WriteLine("--timeout requires an integer value >= 1.");
                    return 2;
                }
                break;

            case "--replace-relays":
            case "--relay-hosts":
                if (!TryReadValue(args, ref i, out replaceRelays))
                {
                    Console.Error.WriteLine("--replace-relays requires a hyphen-separated relay host token.");
                    return 2;
                }
                break;

            case "--server-only":
                serverOnly = true;
                break;

            case "--json":
                json = true;
                break;

            case "--output":
                if (!TryReadValue(args, ref i, out outputPath))
                {
                    Console.Error.WriteLine("--output requires a file path.");
                    return 2;
                }
                break;

            default:
                Console.Error.WriteLine($"Unknown vue990-das option: {args[i]}");
                return 2;
        }
    }

    var topology = WindowsTopology.Capture();
    A9Vue990StatusResult? status = null;
    if (string.IsNullOrWhiteSpace(server))
    {
        status = await new A9Vue990StatusClient().GetStatusAsync(new A9Vue990StatusOptions
        {
            Host = host,
            Port = port,
            Username = string.IsNullOrWhiteSpace(username) ? "admin" : username,
            Password = string.IsNullOrWhiteSpace(password) ? "888888" : password,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        });

        server = status.Server;
    }

    if (status is not null && !status.Success)
    {
        Console.Error.WriteLine("Status fetch failed; cannot read DAS server parameter.");
        return 1;
    }

    if (!A9Vue990DasServerParameter.TryParse(server, out var das, out var parseError) || das is null)
    {
        Console.Error.WriteLine(parseError);
        return 1;
    }

    string? rewrittenServer = null;
    if (!string.IsNullOrWhiteSpace(replaceRelays))
    {
        if (!TryBuildRewrittenDasServer(das, replaceRelays, out rewrittenServer, out var rewriteError))
        {
            Console.Error.WriteLine(rewriteError);
            return 1;
        }

        if (!A9Vue990DasServerParameter.TryParse(rewrittenServer, out var rewrittenDas, out var rewrittenParseError) ||
            rewrittenDas is null)
        {
            Console.Error.WriteLine(rewrittenParseError);
            return 1;
        }

        das = rewrittenDas;
        server = rewrittenServer;
    }

    if (serverOnly)
    {
        Console.WriteLine(server);
        return 0;
    }

    Console.WriteLine(WindowsTopology.ToReadableString(topology));
    if (status is not null)
        Console.WriteLine(status.ToReadableString());

    Console.WriteLine(das.ToReadableString());
    if (!string.IsNullOrWhiteSpace(replaceRelays))
        Console.WriteLine($"Rewritten DAS server parameter: {rewrittenServer}");

    if (json || outputPath is not null)
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var payload = JsonSerializer.Serialize(new
        {
            topology,
            status,
            das,
            rewrittenServer,
        }, jsonOptions);

        if (outputPath is not null)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(outputPath, payload);
            Console.WriteLine($"JSON written: {outputPath}");
        }
        else
        {
            Console.WriteLine(payload);
        }
    }

    return 0;
}

static async Task<int> RunVue990HttpMediaCommandAsync(string[] args)
{
    var host = Environment.GetEnvironmentVariable("A9_CAMERA_IP") ?? "192.168.168.1";
    var port = 81;
    var username = Environment.GetEnvironmentVariable("A9_CAMERA_USERNAME") ?? "admin";
    var password = Environment.GetEnvironmentVariable("A9_CAMERA_PASSWORD") ?? "888888";
    var timeoutSeconds = 4;
    var readSeconds = 2;
    var maxBytes = 1024 * 1024;
    var stopAfterFirstImage = false;
    var saveRaw = false;
    var json = false;
    string? outputPath = null;
    string? outputDirectory = null;
    var paths = new List<string>();

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--host":
                if (!TryReadValue(args, ref i, out host))
                {
                    Console.Error.WriteLine("--host requires a value.");
                    return 2;
                }
                break;

            case "--port":
                if (!TryReadValue(args, ref i, out var portValue) ||
                    !int.TryParse(portValue, out port) ||
                    port is < 1 or > 65535)
                {
                    Console.Error.WriteLine("--port requires a valid TCP port.");
                    return 2;
                }
                break;

            case "--username":
                if (!TryReadValue(args, ref i, out username))
                {
                    Console.Error.WriteLine("--username requires a value.");
                    return 2;
                }
                break;

            case "--password":
                if (!TryReadValue(args, ref i, out password))
                {
                    Console.Error.WriteLine("--password requires a value.");
                    return 2;
                }
                break;

            case "--timeout":
            case "--timeout-seconds":
                if (!TryReadValue(args, ref i, out var timeoutValue) ||
                    !int.TryParse(timeoutValue, out timeoutSeconds) ||
                    timeoutSeconds < 1)
                {
                    Console.Error.WriteLine("--timeout requires an integer value >= 1.");
                    return 2;
                }
                break;

            case "--read-seconds":
                if (!TryReadValue(args, ref i, out var readValue) ||
                    !int.TryParse(readValue, out readSeconds) ||
                    readSeconds < 1)
                {
                    Console.Error.WriteLine("--read-seconds requires an integer value >= 1.");
                    return 2;
                }
                break;

            case "--max-bytes":
                if (!TryReadValue(args, ref i, out var maxBytesValue) ||
                    !int.TryParse(maxBytesValue, out maxBytes) ||
                    maxBytes < 1024)
                {
                    Console.Error.WriteLine("--max-bytes requires an integer value >= 1024.");
                    return 2;
                }
                break;

            case "--path":
                if (!TryReadValue(args, ref i, out var path))
                {
                    Console.Error.WriteLine("--path requires a CGI path.");
                    return 2;
                }
                paths.Add(path);
                break;

            case "--stop-after-first-image":
                stopAfterFirstImage = true;
                break;

            case "--save-raw":
                saveRaw = true;
                break;

            case "--output-dir":
                if (!TryReadValue(args, ref i, out outputDirectory))
                {
                    Console.Error.WriteLine("--output-dir requires a directory path.");
                    return 2;
                }
                break;

            case "--json":
                json = true;
                break;

            case "--output":
                if (!TryReadValue(args, ref i, out outputPath))
                {
                    Console.Error.WriteLine("--output requires a file path.");
                    return 2;
                }
                break;

            default:
                Console.Error.WriteLine($"Unknown vue990-http-media option: {args[i]}");
                return 2;
        }
    }

    var topology = WindowsTopology.Capture();
    var result = await new A9Vue990HttpMediaProbeClient().ProbeAsync(new A9Vue990HttpMediaProbeOptions
    {
        Host = host,
        Port = port,
        Username = string.IsNullOrWhiteSpace(username) ? "admin" : username,
        Password = string.IsNullOrWhiteSpace(password) ? "888888" : password,
        EndpointTimeout = TimeSpan.FromSeconds(timeoutSeconds),
        ReadDuration = TimeSpan.FromSeconds(readSeconds),
        MaxBytes = maxBytes,
        StopAfterFirstImage = stopAfterFirstImage,
        SaveRawSamples = saveRaw,
        OutputDirectory = outputDirectory,
        Paths = paths,
    });

    Console.WriteLine(WindowsTopology.ToReadableString(topology));
    Console.WriteLine(result.ToReadableString());

    if (json || outputPath is not null)
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var payload = JsonSerializer.Serialize(new
        {
            topology,
            result,
        }, jsonOptions);

        if (outputPath is not null)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(outputPath, payload);
            Console.WriteLine($"JSON written: {outputPath}");
        }
        else
        {
            Console.WriteLine(payload);
        }
    }

    return result.CapturedImage || result.CapturedVideo ? 0 : 1;
}

static async Task<int> RunVue990DirectCaptureCommandAsync(string[] args)
{
    var host = Environment.GetEnvironmentVariable("A9_CAMERA_IP") ?? "192.168.168.1";
    var captureImage = true;
    var captureVideo = true;
    var streamSeconds = 18;
    var maxFrames = 12;
    var json = false;
    string? outputPath = null;
    string? outputDirectory = null;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--host":
                if (!TryReadValue(args, ref i, out host))
                {
                    Console.Error.WriteLine("--host requires a value.");
                    return 2;
                }
                break;

            case "--output-dir":
                if (!TryReadValue(args, ref i, out outputDirectory))
                {
                    Console.Error.WriteLine("--output-dir requires a directory path.");
                    return 2;
                }
                break;

            case "--stream-seconds":
            case "--timeout-seconds":
                if (!TryReadValue(args, ref i, out var streamValue) ||
                    !int.TryParse(streamValue, out streamSeconds) ||
                    streamSeconds < 1)
                {
                    Console.Error.WriteLine("--stream-seconds requires an integer value >= 1.");
                    return 2;
                }
                break;

            case "--max-frames":
                if (!TryReadValue(args, ref i, out var maxFramesValue) ||
                    !int.TryParse(maxFramesValue, out maxFrames) ||
                    maxFrames < 1)
                {
                    Console.Error.WriteLine("--max-frames requires an integer value >= 1.");
                    return 2;
                }
                break;

            case "--no-image":
                captureImage = false;
                break;

            case "--no-video":
                captureVideo = false;
                break;

            case "--json":
                json = true;
                break;

            case "--output":
                if (!TryReadValue(args, ref i, out outputPath))
                {
                    Console.Error.WriteLine("--output requires a file path.");
                    return 2;
                }
                break;

            default:
                Console.Error.WriteLine($"Unknown vue990-direct-capture option: {args[i]}");
                return 2;
        }
    }

    outputDirectory ??= Path.Combine(
        Environment.CurrentDirectory,
        ".my",
        "plan",
        "m38-a9-camera",
        "captures",
        $"vue990-direct-capture-{DateTimeOffset.Now:yyyy-MM-dd-HHmmss}");

    var topology = WindowsTopology.Capture();
    var result = await new A9Vue990DirectCaptureClient().CaptureAsync(
        new A9Vue990DirectCaptureOptions
        {
            Host = host,
            OutputDirectory = outputDirectory,
            CaptureImage = captureImage,
            CaptureVideo = captureVideo,
            StreamSeconds = streamSeconds,
            MaxFrames = maxFrames,
        },
        Console.WriteLine);

    Console.WriteLine();
    Console.WriteLine(WindowsTopology.ToReadableString(topology));
    Console.WriteLine(result.ToReadableString());

    if (json || outputPath is not null)
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var payload = JsonSerializer.Serialize(new
        {
            topology,
            result,
        }, jsonOptions);

        if (outputPath is not null)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(outputPath, payload);
            Console.WriteLine($"JSON written: {outputPath}");
        }
        else
        {
            Console.WriteLine(payload);
        }
    }

    return result.Success ? 0 : 1;
}

static async Task<int> RunVue990PpcsTransportCommandAsync(string[] args)
{
    var host = Environment.GetEnvironmentVariable("A9_CAMERA_IP") ?? "192.168.168.1";
    var port = 81;
    var username = Environment.GetEnvironmentVariable("A9_CAMERA_USERNAME") ?? "admin";
    var password = Environment.GetEnvironmentVariable("A9_CAMERA_PASSWORD") ?? "888888";
    var statusTimeoutSeconds = 5;
    var timeoutMs = 1200;
    var readMs = 750;
    var maxBytes = 4096;
    var probeRelays = false;
    var json = false;
    string? outputPath = null;
    var tcpPorts = new List<int>();
    var udpPorts = new List<int>();
    var relayTcpPorts = new List<int>();
    var relayUdpPorts = new List<int>();

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--host":
                if (!TryReadValue(args, ref i, out host))
                {
                    Console.Error.WriteLine("--host requires a value.");
                    return 2;
                }
                break;

            case "--port":
            case "--status-port":
                if (!TryReadValue(args, ref i, out var portValue) ||
                    !int.TryParse(portValue, out port) ||
                    port is < 1 or > 65535)
                {
                    Console.Error.WriteLine("--port requires a valid TCP port.");
                    return 2;
                }
                break;

            case "--username":
                if (!TryReadValue(args, ref i, out username))
                {
                    Console.Error.WriteLine("--username requires a value.");
                    return 2;
                }
                break;

            case "--password":
                if (!TryReadValue(args, ref i, out password))
                {
                    Console.Error.WriteLine("--password requires a value.");
                    return 2;
                }
                break;

            case "--status-timeout":
            case "--status-timeout-seconds":
                if (!TryReadValue(args, ref i, out var statusTimeoutValue) ||
                    !int.TryParse(statusTimeoutValue, out statusTimeoutSeconds) ||
                    statusTimeoutSeconds < 1)
                {
                    Console.Error.WriteLine("--status-timeout requires an integer value >= 1.");
                    return 2;
                }
                break;

            case "--timeout":
            case "--timeout-ms":
            case "--connect-timeout-ms":
                if (!TryReadValue(args, ref i, out var timeoutValue) ||
                    !int.TryParse(timeoutValue, out timeoutMs) ||
                    timeoutMs < 100)
                {
                    Console.Error.WriteLine("--timeout-ms requires an integer value >= 100.");
                    return 2;
                }
                break;

            case "--read-ms":
                if (!TryReadValue(args, ref i, out var readValue) ||
                    !int.TryParse(readValue, out readMs) ||
                    readMs < 100)
                {
                    Console.Error.WriteLine("--read-ms requires an integer value >= 100.");
                    return 2;
                }
                break;

            case "--max-bytes":
                if (!TryReadValue(args, ref i, out var maxBytesValue) ||
                    !int.TryParse(maxBytesValue, out maxBytes) ||
                    maxBytes < 256)
                {
                    Console.Error.WriteLine("--max-bytes requires an integer value >= 256.");
                    return 2;
                }
                break;

            case "--probe-relays":
                probeRelays = true;
                break;

            case "--tcp-port":
                if (!TryReadValue(args, ref i, out var tcpPortValue) ||
                    !int.TryParse(tcpPortValue, out var tcpPort) ||
                    tcpPort is < 1 or > 65535)
                {
                    Console.Error.WriteLine("--tcp-port requires a valid TCP port.");
                    return 2;
                }
                tcpPorts.Add(tcpPort);
                break;

            case "--relay-tcp-port":
                if (!TryReadValue(args, ref i, out var relayTcpPortValue) ||
                    !int.TryParse(relayTcpPortValue, out var relayTcpPort) ||
                    relayTcpPort is < 1 or > 65535)
                {
                    Console.Error.WriteLine("--relay-tcp-port requires a valid TCP port.");
                    return 2;
                }
                relayTcpPorts.Add(relayTcpPort);
                break;

            case "--udp-port":
                if (!TryReadValue(args, ref i, out var udpPortValue) ||
                    !int.TryParse(udpPortValue, out var udpPort) ||
                    udpPort is < 1 or > 65535)
                {
                    Console.Error.WriteLine("--udp-port requires a valid UDP port.");
                    return 2;
                }
                udpPorts.Add(udpPort);
                break;

            case "--relay-udp-port":
                if (!TryReadValue(args, ref i, out var relayUdpPortValue) ||
                    !int.TryParse(relayUdpPortValue, out var relayUdpPort) ||
                    relayUdpPort is < 1 or > 65535)
                {
                    Console.Error.WriteLine("--relay-udp-port requires a valid UDP port.");
                    return 2;
                }
                relayUdpPorts.Add(relayUdpPort);
                break;

            case "--json":
                json = true;
                break;

            case "--output":
                if (!TryReadValue(args, ref i, out outputPath))
                {
                    Console.Error.WriteLine("--output requires a file path.");
                    return 2;
                }
                break;

            default:
                Console.Error.WriteLine($"Unknown vue990-ppcs-transport option: {args[i]}");
                return 2;
        }
    }

    var defaultOptions = new A9Vue990PpcsTransportProbeOptions();
    var options = new A9Vue990PpcsTransportProbeOptions
    {
        Host = host,
        StatusPort = port,
        Username = string.IsNullOrWhiteSpace(username) ? "admin" : username,
        Password = string.IsNullOrWhiteSpace(password) ? "888888" : password,
        StatusTimeout = TimeSpan.FromSeconds(statusTimeoutSeconds),
        ConnectTimeout = TimeSpan.FromMilliseconds(timeoutMs),
        ReadTimeout = TimeSpan.FromMilliseconds(readMs),
        MaxBytes = maxBytes,
        TcpPorts = tcpPorts.Count == 0 ? defaultOptions.TcpPorts : tcpPorts,
        UdpPorts = udpPorts.Count == 0 ? defaultOptions.UdpPorts : udpPorts,
        ProbeDecodedRelayHosts = probeRelays,
        RelayTcpPorts = relayTcpPorts.Count == 0 ? defaultOptions.RelayTcpPorts : relayTcpPorts,
        RelayUdpPorts = relayUdpPorts.Count == 0 ? defaultOptions.RelayUdpPorts : relayUdpPorts,
    };

    var topology = WindowsTopology.Capture();
    var result = await new A9Vue990PpcsTransportProbeClient().ProbeAsync(options);
    Console.WriteLine(WindowsTopology.ToReadableString(topology));
    Console.WriteLine(result.ToReadableString());

    if (json || outputPath is not null)
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var payload = JsonSerializer.Serialize(new
        {
            topology,
            result,
        }, jsonOptions);

        if (outputPath is not null)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(outputPath, payload);
            Console.WriteLine($"JSON written: {outputPath}");
        }
        else
        {
            Console.WriteLine(payload);
        }
    }

    return result.Success ? 0 : 1;
}

static async Task<int> RunVue990RelayHelloCommandAsync(string[] args)
{
    var host = Environment.GetEnvironmentVariable("A9_CAMERA_IP") ?? "192.168.168.1";
    var port = 81;
    var username = Environment.GetEnvironmentVariable("A9_CAMERA_USERNAME") ?? "admin";
    var password = Environment.GetEnvironmentVariable("A9_CAMERA_PASSWORD") ?? "888888";
    string? clientId = null;
    string? vuid = null;
    string? serverParameter = null;
    var statusTimeoutSeconds = 5;
    var timeoutMs = 1200;
    var readMs = 1200;
    var relayPort = 65527;
    var maxBytes = 4096;
    var maxCandidates = int.MaxValue;
    var json = false;
    string? outputPath = null;
    string? responseOutputDirectory = null;
    var relayHosts = new List<string>();
    var extraPayloads = new List<A9Vue990RelayHelloExtraPayload>();

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--host":
                if (!TryReadValue(args, ref i, out host))
                {
                    Console.Error.WriteLine("--host requires a value.");
                    return 2;
                }
                break;

            case "--port":
            case "--status-port":
                if (!TryReadValue(args, ref i, out var portValue) ||
                    !int.TryParse(portValue, out port) ||
                    port is < 1 or > 65535)
                {
                    Console.Error.WriteLine("--port requires a valid TCP port.");
                    return 2;
                }
                break;

            case "--username":
                if (!TryReadValue(args, ref i, out username))
                {
                    Console.Error.WriteLine("--username requires a value.");
                    return 2;
                }
                break;

            case "--password":
                if (!TryReadValue(args, ref i, out password))
                {
                    Console.Error.WriteLine("--password requires a value.");
                    return 2;
                }
                break;

            case "--client-id":
                if (!TryReadValue(args, ref i, out clientId))
                {
                    Console.Error.WriteLine("--client-id requires a value.");
                    return 2;
                }
                break;

            case "--vuid":
                if (!TryReadValue(args, ref i, out vuid))
                {
                    Console.Error.WriteLine("--vuid requires a value.");
                    return 2;
                }
                break;

            case "--server":
                if (!TryReadValue(args, ref i, out serverParameter))
                {
                    Console.Error.WriteLine("--server requires a DAS value.");
                    return 2;
                }
                break;

            case "--status-timeout":
            case "--status-timeout-seconds":
                if (!TryReadValue(args, ref i, out var statusTimeoutValue) ||
                    !int.TryParse(statusTimeoutValue, out statusTimeoutSeconds) ||
                    statusTimeoutSeconds < 1)
                {
                    Console.Error.WriteLine("--status-timeout requires an integer value >= 1.");
                    return 2;
                }
                break;

            case "--timeout":
            case "--timeout-ms":
            case "--connect-timeout-ms":
                if (!TryReadValue(args, ref i, out var timeoutValue) ||
                    !int.TryParse(timeoutValue, out timeoutMs) ||
                    timeoutMs < 100)
                {
                    Console.Error.WriteLine("--timeout-ms requires an integer value >= 100.");
                    return 2;
                }
                break;

            case "--read-ms":
                if (!TryReadValue(args, ref i, out var readValue) ||
                    !int.TryParse(readValue, out readMs) ||
                    readMs < 100)
                {
                    Console.Error.WriteLine("--read-ms requires an integer value >= 100.");
                    return 2;
                }
                break;

            case "--relay-port":
                if (!TryReadValue(args, ref i, out var relayPortValue) ||
                    !int.TryParse(relayPortValue, out relayPort) ||
                    relayPort is < 1 or > 65535)
                {
                    Console.Error.WriteLine("--relay-port requires a valid TCP port.");
                    return 2;
                }
                break;

            case "--relay-host":
                if (!TryReadValue(args, ref i, out var relayHost))
                {
                    Console.Error.WriteLine("--relay-host requires a value.");
                    return 2;
                }
                relayHosts.Add(relayHost);
                break;

            case "--payload-hex":
                if (!TryReadValue(args, ref i, out var payloadHex) ||
                    !TryParseHex(payloadHex, out var payloadBytes))
                {
                    Console.Error.WriteLine("--payload-hex requires an even-length hex value.");
                    return 2;
                }
                extraPayloads.Add(new A9Vue990RelayHelloExtraPayload
                {
                    Name = $"extra-{extraPayloads.Count + 1}",
                    Bytes = payloadBytes,
                });
                break;

            case "--max-bytes":
            case "--max-response-bytes":
                if (!TryReadValue(args, ref i, out var maxBytesValue) ||
                    !int.TryParse(maxBytesValue, out maxBytes) ||
                    maxBytes < 256)
                {
                    Console.Error.WriteLine("--max-bytes requires an integer value >= 256.");
                    return 2;
                }
                break;

            case "--max-candidates":
                if (!TryReadValue(args, ref i, out var maxCandidatesValue) ||
                    !int.TryParse(maxCandidatesValue, out maxCandidates) ||
                    maxCandidates < 1)
                {
                    Console.Error.WriteLine("--max-candidates requires an integer value >= 1.");
                    return 2;
                }
                break;

            case "--response-output-dir":
                if (!TryReadValue(args, ref i, out responseOutputDirectory))
                {
                    Console.Error.WriteLine("--response-output-dir requires a directory path.");
                    return 2;
                }
                break;

            case "--json":
                json = true;
                break;

            case "--output":
                if (!TryReadValue(args, ref i, out outputPath))
                {
                    Console.Error.WriteLine("--output requires a file path.");
                    return 2;
                }
                break;

            default:
                Console.Error.WriteLine($"Unknown vue990-relay-hello option: {args[i]}");
                return 2;
        }
    }

    var options = new A9Vue990RelayHelloProbeOptions
    {
        Host = host,
        StatusPort = port,
        Username = string.IsNullOrWhiteSpace(username) ? "admin" : username,
        Password = string.IsNullOrWhiteSpace(password) ? "888888" : password,
        ClientId = clientId,
        Vuid = vuid,
        ServerParameter = serverParameter,
        StatusTimeout = TimeSpan.FromSeconds(statusTimeoutSeconds),
        ConnectTimeout = TimeSpan.FromMilliseconds(timeoutMs),
        ReadTimeout = TimeSpan.FromMilliseconds(readMs),
        RelayPort = relayPort,
        MaxResponseBytes = maxBytes,
        MaxCandidates = maxCandidates,
        ResponseOutputDirectory = responseOutputDirectory,
        RelayHosts = relayHosts,
        ExtraPayloads = extraPayloads,
    };

    var topology = WindowsTopology.Capture();
    var result = await new A9Vue990RelayHelloProbeClient().ProbeAsync(options);
    Console.WriteLine(WindowsTopology.ToReadableString(topology));
    Console.WriteLine(result.ToReadableString());

    if (json || outputPath is not null)
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var payload = JsonSerializer.Serialize(new
        {
            topology,
            result,
        }, jsonOptions);

        if (outputPath is not null)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(outputPath, payload);
            Console.WriteLine($"JSON written: {outputPath}");
        }
        else
        {
            Console.WriteLine(payload);
        }
    }

    return result.Success ? 0 : 1;
}

static async Task<int> RunVue990AndroidCaptureCommandAsync(string[] args, bool channelOracleDefault = false)
{
    var adb = Environment.GetEnvironmentVariable("A9_ADB_PATH") ?? "adb";
    var packageName = Environment.GetEnvironmentVariable("A9_PHONE_PROBE_PACKAGE") ?? "com.bodycam.a9phoneprobe";
    var host = Environment.GetEnvironmentVariable("A9_CAMERA_IP") ?? "192.168.168.1";
    var requiredSubnet = Environment.GetEnvironmentVariable("A9_PHONE_WIFI_SUBNET") ?? "192.168.168.";
    var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd-HHmmss");
    var outputDirectory = Path.Combine(
        Environment.CurrentDirectory,
        ".my",
        "plan",
        "m38-a9-camera",
        "captures",
        channelOracleDefault
            ? $"phase-40-native-channel-oracle-{stamp}"
            : $"phase-27-android-csharp-orchestrated-{stamp}");
    string? apkPath = Environment.GetEnvironmentVariable("A9_PHONE_PROBE_APK");
    string? outputPath = null;
    var installApk = true;
    var captureImage = !channelOracleDefault;
    var captureVideo = !channelOracleDefault;
    var fakeRelay = false;
    var channelOracle = channelOracleDefault;
    var managedLiveCgi = false;
    string? managedLiveCgiMode = null;
    string? serverOverride = null;
    var requireSubnet = true;
    var adbTimeoutSeconds = 10;
    var reportTimeoutSeconds = 130;
    var width = 640;
    var height = 480;
    var fps = 2;
    var json = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--adb":
                if (!TryReadValue(args, ref i, out adb))
                {
                    Console.Error.WriteLine("--adb requires a value.");
                    return 2;
                }
                break;

            case "--package":
            case "--package-name":
                if (!TryReadValue(args, ref i, out packageName))
                {
                    Console.Error.WriteLine("--package requires a value.");
                    return 2;
                }
                break;

            case "--apk":
            case "--apk-path":
                if (!TryReadValue(args, ref i, out apkPath))
                {
                    Console.Error.WriteLine("--apk requires a value.");
                    return 2;
                }
                break;

            case "--host":
                if (!TryReadValue(args, ref i, out host))
                {
                    Console.Error.WriteLine("--host requires a value.");
                    return 2;
                }
                break;

            case "--required-subnet":
                if (!TryReadValue(args, ref i, out requiredSubnet))
                {
                    Console.Error.WriteLine("--required-subnet requires a value.");
                    return 2;
                }
                break;

            case "--allow-any-wifi":
                requireSubnet = false;
                break;

            case "--skip-install":
                installApk = false;
                break;

            case "--no-image":
                captureImage = false;
                break;

            case "--no-video":
                captureVideo = false;
                break;

            case "--fake-relay":
                fakeRelay = true;
                break;

            case "--channel-oracle":
            case "--native-channel-oracle":
                channelOracle = true;
                break;

            case "--managed-live-cgi":
                managedLiveCgi = true;
                break;

            case "--managed-live-cgi-mode":
                if (!TryReadValue(args, ref i, out managedLiveCgiMode))
                {
                    Console.Error.WriteLine("--managed-live-cgi-mode requires a mode value.");
                    return 2;
                }

                managedLiveCgi = true;
                break;

            case "--server-override":
                if (!TryReadValue(args, ref i, out serverOverride))
                {
                    Console.Error.WriteLine("--server-override requires a DAS value.");
                    return 2;
                }
                break;

            case "--output-dir":
                if (!TryReadValue(args, ref i, out outputDirectory))
                {
                    Console.Error.WriteLine("--output-dir requires a directory path.");
                    return 2;
                }
                break;

            case "--output":
                if (!TryReadValue(args, ref i, out outputPath))
                {
                    Console.Error.WriteLine("--output requires a file path.");
                    return 2;
                }
                break;

            case "--json":
                json = true;
                break;

            case "--adb-timeout":
            case "--adb-timeout-seconds":
                if (!TryReadValue(args, ref i, out var adbTimeoutValue) ||
                    !int.TryParse(adbTimeoutValue, out adbTimeoutSeconds) ||
                    adbTimeoutSeconds < 1)
                {
                    Console.Error.WriteLine("--adb-timeout requires an integer value >= 1.");
                    return 2;
                }
                break;

            case "--timeout":
            case "--timeout-seconds":
            case "--report-timeout":
            case "--report-timeout-seconds":
                if (!TryReadValue(args, ref i, out var reportTimeoutValue) ||
                    !int.TryParse(reportTimeoutValue, out reportTimeoutSeconds) ||
                    reportTimeoutSeconds < 10)
                {
                    Console.Error.WriteLine("--timeout-seconds requires an integer value >= 10.");
                    return 2;
                }
                break;

            case "--width":
                if (!TryReadValue(args, ref i, out var widthValue) ||
                    !int.TryParse(widthValue, out width) ||
                    width < 1)
                {
                    Console.Error.WriteLine("--width requires an integer value >= 1.");
                    return 2;
                }
                break;

            case "--height":
                if (!TryReadValue(args, ref i, out var heightValue) ||
                    !int.TryParse(heightValue, out height) ||
                    height < 1)
                {
                    Console.Error.WriteLine("--height requires an integer value >= 1.");
                    return 2;
                }
                break;

            case "--fps":
                if (!TryReadValue(args, ref i, out var fpsValue) ||
                    !int.TryParse(fpsValue, out fps) ||
                    fps < 1)
                {
                    Console.Error.WriteLine("--fps requires an integer value >= 1.");
                    return 2;
                }
                break;

            default:
                Console.Error.WriteLine($"Unknown vue990-android-capture option: {args[i]}");
                return 2;
        }
    }

    if (!captureImage && !captureVideo && !fakeRelay && !channelOracle)
    {
        Console.Error.WriteLine("At least one of image capture, video capture, --fake-relay, or --channel-oracle must be enabled.");
        return 2;
    }

    if (!string.IsNullOrWhiteSpace(managedLiveCgiMode))
    {
        var allowedModes = new[]
        {
            "d1-get-slash",
            "d1-get-noslash",
            "raw-cgi",
            "raw-cgi-null",
            "raw-get-slash",
            "raw-get-noslash",
            "command-cgi-split",
            "command-cgi-combined",
        };
        managedLiveCgiMode = managedLiveCgiMode.Trim().ToLowerInvariant();
        if (!allowedModes.Contains(managedLiveCgiMode, StringComparer.Ordinal))
        {
            Console.Error.WriteLine($"Unknown managed live CGI mode: {managedLiveCgiMode}");
            return 2;
        }
    }

    var result = await new A9AndroidPhoneCaptureClient().CaptureAsync(new A9AndroidPhoneCaptureOptions
    {
        AdbPath = adb,
        PackageName = string.IsNullOrWhiteSpace(packageName) ? "com.bodycam.a9phoneprobe" : packageName,
        CameraHost = string.IsNullOrWhiteSpace(host) ? "192.168.168.1" : host,
        RequiredPhoneSubnet = string.IsNullOrWhiteSpace(requiredSubnet) ? "192.168.168." : requiredSubnet,
        RequirePhoneSubnet = requireSubnet,
        ApkPath = apkPath,
        InstallApk = installApk,
        CaptureImage = captureImage,
        CaptureVideo = captureVideo,
        FakeRelay = fakeRelay,
        ChannelOracle = channelOracle,
        ManagedLiveCgi = managedLiveCgi,
        ManagedLiveCgiMode = managedLiveCgiMode,
        ServerParameterOverride = serverOverride,
        OutputDirectory = outputDirectory,
        AdbTimeout = TimeSpan.FromSeconds(adbTimeoutSeconds),
        ReportTimeout = TimeSpan.FromSeconds(reportTimeoutSeconds),
        VideoWidth = width,
        VideoHeight = height,
        FramesPerSecond = fps,
    });

    Console.WriteLine(result.ToReadableString());

    if (json || outputPath is not null)
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var payload = JsonSerializer.Serialize(result, jsonOptions);

        if (outputPath is not null)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(outputPath, payload);
            Console.WriteLine($"JSON written: {outputPath}");
        }
        else
        {
            Console.WriteLine(payload);
        }
    }

    return result.Success ? 0 : 1;
}

static async Task<int> RunVue990AndroidManagedDirectCommandAsync(string[] args)
{
    var adb = Environment.GetEnvironmentVariable("A9_ADB_PATH") ?? "adb";
    var packageName = Environment.GetEnvironmentVariable("A9_PHONE_PROBE_PACKAGE") ?? "com.bodycam.a9phoneprobe";
    var host = Environment.GetEnvironmentVariable("A9_CAMERA_IP") ?? "192.168.168.1";
    var requiredSubnet = Environment.GetEnvironmentVariable("A9_PHONE_WIFI_SUBNET") ?? "192.168.168.";
    var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd-HHmmss");
    var outputDirectory = Path.Combine(
        Environment.CurrentDirectory,
        ".my",
        "plan",
        "m38-a9-camera",
        "captures",
        $"phase-33-android-managed-direct-{stamp}");
    string? apkPath = Environment.GetEnvironmentVariable("A9_PHONE_PROBE_APK");
    string? outputPath = null;
    var installApk = true;
    var captureImage = true;
    var captureVideo = true;
    var managedLanHoleOnly = false;
    var requireSubnet = true;
    var adbTimeoutSeconds = 10;
    var reportTimeoutSeconds = 90;
    var json = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--adb":
                if (!TryReadValue(args, ref i, out adb))
                {
                    Console.Error.WriteLine("--adb requires a value.");
                    return 2;
                }
                break;

            case "--package":
            case "--package-name":
                if (!TryReadValue(args, ref i, out packageName))
                {
                    Console.Error.WriteLine("--package requires a value.");
                    return 2;
                }
                break;

            case "--apk":
            case "--apk-path":
                if (!TryReadValue(args, ref i, out apkPath))
                {
                    Console.Error.WriteLine("--apk requires a value.");
                    return 2;
                }
                break;

            case "--host":
                if (!TryReadValue(args, ref i, out host))
                {
                    Console.Error.WriteLine("--host requires a value.");
                    return 2;
                }
                break;

            case "--required-subnet":
                if (!TryReadValue(args, ref i, out requiredSubnet))
                {
                    Console.Error.WriteLine("--required-subnet requires a value.");
                    return 2;
                }
                break;

            case "--allow-any-wifi":
                requireSubnet = false;
                break;

            case "--skip-install":
                installApk = false;
                break;

            case "--no-image":
                captureImage = false;
                break;

            case "--no-video":
                captureVideo = false;
                break;

            case "--managed-lan-hole":
            case "--managed-lan-hole-only":
                managedLanHoleOnly = true;
                captureImage = false;
                captureVideo = false;
                break;

            case "--output-dir":
                if (!TryReadValue(args, ref i, out outputDirectory))
                {
                    Console.Error.WriteLine("--output-dir requires a directory path.");
                    return 2;
                }
                break;

            case "--output":
                if (!TryReadValue(args, ref i, out outputPath))
                {
                    Console.Error.WriteLine("--output requires a file path.");
                    return 2;
                }
                break;

            case "--json":
                json = true;
                break;

            case "--adb-timeout":
            case "--adb-timeout-seconds":
                if (!TryReadValue(args, ref i, out var adbTimeoutValue) ||
                    !int.TryParse(adbTimeoutValue, out adbTimeoutSeconds) ||
                    adbTimeoutSeconds < 1)
                {
                    Console.Error.WriteLine("--adb-timeout requires an integer value >= 1.");
                    return 2;
                }
                break;

            case "--timeout":
            case "--timeout-seconds":
            case "--report-timeout":
            case "--report-timeout-seconds":
                if (!TryReadValue(args, ref i, out var reportTimeoutValue) ||
                    !int.TryParse(reportTimeoutValue, out reportTimeoutSeconds) ||
                    reportTimeoutSeconds < 10)
                {
                    Console.Error.WriteLine("--timeout-seconds requires an integer value >= 10.");
                    return 2;
                }
                break;

            default:
                Console.Error.WriteLine($"Unknown vue990-android-managed-direct option: {args[i]}");
                return 2;
        }
    }

    var result = await new A9AndroidManagedDirectClient().ProbeAsync(new A9AndroidManagedDirectOptions
    {
        AdbPath = adb,
        PackageName = string.IsNullOrWhiteSpace(packageName) ? "com.bodycam.a9phoneprobe" : packageName,
        CameraHost = string.IsNullOrWhiteSpace(host) ? "192.168.168.1" : host,
        RequiredPhoneSubnet = string.IsNullOrWhiteSpace(requiredSubnet) ? "192.168.168." : requiredSubnet,
        RequirePhoneSubnet = requireSubnet,
        ApkPath = apkPath,
        InstallApk = installApk,
        CaptureImage = captureImage,
        CaptureVideo = captureVideo,
        ManagedLanHoleOnly = managedLanHoleOnly,
        OutputDirectory = outputDirectory,
        AdbTimeout = TimeSpan.FromSeconds(adbTimeoutSeconds),
        ReportTimeout = TimeSpan.FromSeconds(reportTimeoutSeconds),
    });

    Console.WriteLine(result.ToReadableString());

    if (json || outputPath is not null)
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var payload = JsonSerializer.Serialize(result, jsonOptions);

        if (outputPath is not null)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(outputPath, payload);
            Console.WriteLine($"JSON written: {outputPath}");
        }
        else
        {
            Console.WriteLine(payload);
        }
    }

    return result.Success ? 0 : 1;
}

static (A9ProbeOptions Options, bool Json, string? OutputPath, string? Error) ParseProbeOptions(string[] args)
{
    string? host = null;
    var hosts = new List<string>();
    var protocol = A9ProbeProtocol.Auto;
    var timeoutMs = 1200;
    var firstFrame = false;
    var json = false;
    string? outputPath = null;
    var username = Environment.GetEnvironmentVariable("A9_CAMERA_USERNAME") ?? "admin";
    var password = Environment.GetEnvironmentVariable("A9_CAMERA_PASSWORD") ?? "admin";

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        switch (arg)
        {
            case "--host":
                if (!TryReadValue(args, ref i, out host))
                    return (new A9ProbeOptions(), false, null, "--host requires a value.");
                break;

            case "--protocol":
                if (!TryReadValue(args, ref i, out var protocolValue))
                    return (new A9ProbeOptions(), false, null, "--protocol requires a value.");

                if (!TryParseProtocol(protocolValue, out protocol))
                    return (new A9ProbeOptions(), false, null, $"Unknown protocol: {protocolValue}");
                break;

            case "--timeout":
            case "--timeout-ms":
                if (!TryReadValue(args, ref i, out var timeoutValue) ||
                    !int.TryParse(timeoutValue, out timeoutMs) ||
                    timeoutMs < 100)
                {
                    return (new A9ProbeOptions(), false, null, "--timeout requires an integer value >= 100.");
                }
                break;

            case "--first-frame":
                firstFrame = true;
                break;

            case "--json":
                json = true;
                break;

            case "--output":
                if (!TryReadValue(args, ref i, out outputPath))
                    return (new A9ProbeOptions(), false, null, "--output requires a file path.");
                break;

            case "--username":
                if (!TryReadValue(args, ref i, out username))
                    return (new A9ProbeOptions(), false, null, "--username requires a value.");
                break;

            case "--password":
                if (!TryReadValue(args, ref i, out password))
                    return (new A9ProbeOptions(), false, null, "--password requires a value.");
                break;

            default:
                if (arg.StartsWith("--", StringComparison.Ordinal))
                    return (new A9ProbeOptions(), false, null, $"Unknown option: {arg}");

                hosts.Add(arg);
                break;
        }
    }

    return (new A9ProbeOptions
    {
        Host = host,
        Hosts = hosts,
        Protocol = protocol,
        TimeoutMs = timeoutMs,
        FirstFrame = firstFrame,
        Username = string.IsNullOrWhiteSpace(username) ? "admin" : username,
        Password = string.IsNullOrWhiteSpace(password) ? "admin" : password,
    }, json, outputPath, null);
}

static bool TryReadValue(string[] args, ref int index, out string value)
{
    value = string.Empty;
    if (index + 1 >= args.Length)
        return false;

    value = args[++index];
    return !string.IsNullOrWhiteSpace(value);
}

static bool TryBuildRewrittenDasServer(
    A9Vue990DasServerParameter das,
    string replacementRelayToken,
    out string server,
    out string error)
{
    server = string.Empty;
    error = string.Empty;

    var replacement = replacementRelayToken.Trim();
    if (replacement.Length == 0)
    {
        error = "Replacement relay token is empty.";
        return false;
    }

    if (replacement.Contains(',', StringComparison.Ordinal))
    {
        error = "Replacement relay token must not contain commas.";
        return false;
    }

    if (replacement.Any(value => value < 33 || value > 126))
    {
        error = "Replacement relay token must be printable ASCII without whitespace.";
        return false;
    }

    if (!das.HasDecodedPayload || das.DecodedPayload.PlainBytes.Length == 0)
    {
        error = "DAS payload was not decoded; cannot replace relay hosts.";
        return false;
    }

    var plain = das.DecodedPayload.PlainBytes;
    var replacementBytes = Encoding.ASCII.GetBytes(replacement);
    var start = 0;
    for (var i = 0; i <= plain.Length; i++)
    {
        if (i != plain.Length && plain[i] != (byte)',')
            continue;

        var length = i - start;
        if (LooksLikeRelayHostToken(plain.AsSpan(start, length)))
        {
            var rewritten = new byte[plain.Length - length + replacementBytes.Length];
            Buffer.BlockCopy(plain, 0, rewritten, 0, start);
            Buffer.BlockCopy(replacementBytes, 0, rewritten, start, replacementBytes.Length);
            Buffer.BlockCopy(
                plain,
                i,
                rewritten,
                start + replacementBytes.Length,
                plain.Length - i);

            server = A9Vue990DasServerParameter.EncodeDecodedPayload(rewritten);
            return true;
        }

        start = i + 1;
    }

    error = "No decoded relay-host token was found in the DAS plaintext.";
    return false;
}

static bool LooksLikeRelayHostToken(ReadOnlySpan<byte> token)
{
    if (token.IsEmpty)
        return false;

    var text = Encoding.ASCII.GetString(token);
    var parts = text.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length < 2)
        return false;

    var addressLikeParts = parts.Count(part =>
        part.Count(value => value == '.') == 3 &&
        part.All(value => value is >= '0' and <= '9' or '.'));
    return addressLikeParts >= 2;
}

static bool TryParseHex(string value, out byte[] bytes)
{
    bytes = [];
    var normalized = value
        .Replace(" ", string.Empty, StringComparison.Ordinal)
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .Replace(":", string.Empty, StringComparison.Ordinal);

    if (normalized.Length == 0 || normalized.Length % 2 != 0)
        return false;

    try
    {
        bytes = Convert.FromHexString(normalized);
        return true;
    }
    catch (FormatException)
    {
        return false;
    }
}

static bool TryParseProtocol(string value, out A9ProbeProtocol protocol)
{
    protocol = value.Trim().ToLowerInvariant() switch
    {
        "auto" => A9ProbeProtocol.Auto,
        "rtsp" => A9ProbeProtocol.Rtsp,
        "http" or "http-mjpeg" or "mjpeg" => A9ProbeProtocol.HttpMjpeg,
        "v720" or "v720-naxclow" or "naxclow" => A9ProbeProtocol.V720Naxclow,
        "pppp" or "pppp-udp" or "udp" or "pppp-udp-32108" => A9ProbeProtocol.PpppUdpMjpeg,
        "pppp-udp-20190" or "udp-20190" => A9ProbeProtocol.PpppUdp20190,
        _ => A9ProbeProtocol.None,
    };

    return protocol != A9ProbeProtocol.None;
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        BodyCam A9 probe

        Usage:
          dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- probe [options]
          dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- wifi-ap [options]
          dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- vue990-status [options]
          dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- vue990-das [options]
          dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- vue990-ppcs-transport [options]
          dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- vue990-relay-hello [options]
          dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- vue990-android-capture [options]
          dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- vue990-android-channel-oracle [options]
          dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- vue990-android-managed-direct [options]
          dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- vue990-http-media [options]
          dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- vue990-direct-capture [options]
          dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- mjpeg-avi [options]

        Options:
          --host <ip-or-host>       Probe a known camera host first.
          --protocol <name>         auto, rtsp, http-mjpeg, v720-naxclow, pppp-udp-32108, pppp-udp-20190.
          --timeout <ms>           Per-probe timeout. Default: 1200.
          --first-frame            Capture one frame when the selected protocol supports it.
          --json                   Print JSON after the readable summary.
          --output <path>          Write JSON to a file.
          --username <value>       Camera username. Default: A9_CAMERA_USERNAME or admin.
          --password <value>       Camera password. Default: A9_CAMERA_PASSWORD or admin.

        Wi-Fi AP options:
          --ssid <value>           SSID to scan for. Default: @MC-0025644.
          --connect                Connect using an existing saved Windows Wi-Fi profile.
          --connect-timeout <sec>  Seconds to wait for --connect. Default: 30.
          --no-protocol-probe      Do not run A9 protocol probes when already connected.

        Vue990 status options:
          --host <ip-or-host>       Camera host. Default: A9_CAMERA_IP or 192.168.168.1.
          --port <port>            Camera HTTP port. Default: 81.
          --username <value>       Camera username. Default: A9_CAMERA_USERNAME or admin.
          --password <value>       Camera password. Default: A9_CAMERA_PASSWORD or 888888.
          --timeout <seconds>      HTTP timeout in seconds. Default: 5.
          --json                   Print JSON after the readable summary.
          --output <path>          Write JSON to a file.

        Vue990 DAS options:
          --server <DAS-value>      Analyze this DAS value instead of fetching status.
          --replace-relays <hosts>  Re-encrypt DAS after replacing decoded relay host token.
          --server-only             Print only the resulting DAS value.
          --host <ip-or-host>       Camera host. Default: A9_CAMERA_IP or 192.168.168.1.
          --port <port>            Camera HTTP port. Default: 81.
          --username <value>       Camera username. Default: A9_CAMERA_USERNAME or admin.
          --password <value>       Camera password. Default: A9_CAMERA_PASSWORD or 888888.
          --timeout <seconds>      HTTP timeout in seconds. Default: 5.
          --json                   Print JSON after the readable summary.
          --output <path>          Write JSON to a file.

        Vue990 PPCS transport options:
          --host <ip-or-host>       Camera host. Default: A9_CAMERA_IP or 192.168.168.1.
          --status-port <port>      Camera HTTP status port. Default: 81.
          --username <value>       Camera username. Default: A9_CAMERA_USERNAME or admin.
          --password <value>       Camera password. Default: A9_CAMERA_PASSWORD or 888888.
          --timeout-ms <ms>         TCP connect timeout. Default: 1200.
          --read-ms <ms>            TCP/UDP read window. Default: 750.
          --max-bytes <bytes>       Max bytes recorded per response. Default: 4096.
          --tcp-port <port>         Override TCP candidates; repeatable.
          --udp-port <port>         Override UDP candidates; repeatable.
          --probe-relays            Also probe relay hosts decoded from DAS.
          --relay-tcp-port <port>   Override decoded-relay TCP candidates; repeatable.
          --relay-udp-port <port>   Override decoded-relay UDP candidates; repeatable.
          --json                   Print JSON after the readable summary.
          --output <path>          Write JSON to a file.

        Vue990 relay hello options:
          --host <ip-or-host>       Camera host for status/DAS. Default: A9_CAMERA_IP or 192.168.168.1.
          --status-port <port>      Camera HTTP status port. Default: 81.
          --server <DAS-value>      Use this DAS value instead of fetching status.
          --client-id <value>       Client/device id for --server mode.
          --vuid <value>            Real device/VUID for --server mode.
          --relay-port <port>       Relay TCP port. Default: 65527.
          --relay-host <host>       Override decoded relay hosts; repeatable.
          --payload-hex <hex>       Add an extra candidate payload; repeatable.
          --timeout-ms <ms>         TCP connect timeout. Default: 1200.
          --read-ms <ms>            TCP read window. Default: 1200.
          --max-candidates <n>      Limit candidate payloads per relay.
          --response-output-dir <p> Save any relay response bytes as .bin files.
          --json                   Print JSON after the readable summary.
          --output <path>          Write JSON to a file.

        Vue990 Android C# capture options:
          --adb <path>              ADB executable. Default: A9_ADB_PATH or adb.
          --package <name>          Android probe package. Default: com.bodycam.a9phoneprobe.
          --apk <path>              Android probe APK to install.
          --skip-install            Use the installed probe app as-is.
          --host <ip-or-host>       Camera host. Default: A9_CAMERA_IP or 192.168.168.1.
          --required-subnet <value> Phone Wi-Fi subnet prefix. Default: 192.168.168.
          --allow-any-wifi          Do not enforce phone Wi-Fi subnet prefix.
          --no-image                Skip still-image capture.
          --no-video                Skip frame/video capture.
          --server-override <DAS>   Pass this DAS value to the Android native PPCS stack.
          --fake-relay              Start a phone-local TCP relay recorder on port 65527.
          --channel-oracle          Dump bounded native channel bytes after live CGI.
          --managed-live-cgi        Use C# CGI frame bytes through native JNIApi.write.
          --managed-live-cgi-mode <mode>
                                    d1-get-slash, d1-get-noslash, raw-cgi,
                                    raw-cgi-null, raw-get-slash, raw-get-noslash,
                                    command-cgi-split, command-cgi-combined.
          --output-dir <path>       Directory for pulled JPEGs, AVI, report, and logcat.
          --timeout-seconds <sec>   Wait for phone probe report. Default: 130.
          --width <pixels>          AVI frame width. Default: 640.
          --height <pixels>         AVI frame height. Default: 480.
          --fps <value>             AVI frames per second. Default: 2.
          --json                   Print JSON after the readable summary.
          --output <path>          Write JSON result to a file.

        Vue990 Android managed-direct options:
          --adb <path>              ADB executable. Default: A9_ADB_PATH or adb.
          --package <name>          Android probe package. Default: com.bodycam.a9phoneprobe.
          --apk <path>              Android probe APK to install.
          --skip-install            Use the installed probe app as-is.
          --host <ip-or-host>       Camera host. Default: A9_CAMERA_IP or 192.168.168.1.
          --required-subnet <value> Phone Wi-Fi subnet prefix. Default: 192.168.168.
          --allow-any-wifi          Do not enforce phone Wi-Fi subnet prefix.
          --no-image                Skip still-image artifact save.
          --no-video                Skip MJPEG AVI artifact save.
          --managed-lan-hole        Run only the C# LAN-hole/session opener probe.
          --output-dir <path>       Directory for report, logcat, and pulled artifacts.
          --timeout-seconds <sec>   Wait for phone probe report. Default: 90.
          --json                   Print JSON after the readable summary.
          --output <path>          Write JSON result to a file.

        Vue990 HTTP media options:
          --host <ip-or-host>       Camera host. Default: A9_CAMERA_IP or 192.168.168.1.
          --port <port>            Camera HTTP port. Default: 81.
          --username <value>       Camera username. Default: A9_CAMERA_USERNAME or admin.
          --password <value>       Camera password. Default: A9_CAMERA_PASSWORD or 888888.
          --timeout <seconds>      Per-endpoint header timeout. Default: 4.
          --read-seconds <seconds> Seconds to read streaming bodies. Default: 2.
          --max-bytes <bytes>      Max bytes read per endpoint. Default: 1048576.
          --path <cgi-path>        Probe a specific path; repeatable.
          --stop-after-first-image Stop after extracting a JPEG frame.
          --save-raw               Save non-status raw samples under --output-dir.
          --output-dir <path>      Save JPEG/AVI artifacts when found.
          --json                   Print JSON after the readable summary.
          --output <path>          Write JSON to a file.

        Vue990 direct C# capture options:
          --host <ip-or-host>       Camera host. Default: A9_CAMERA_IP or 192.168.168.1.
          --output-dir <path>       Save direct packets, JPEGs, AVI, and report artifacts.
          --stream-seconds <sec>    Direct receive window. Default: 18.
          --max-frames <n>          Stop after this many JPEG frames. Default: 12.
          --no-image                Skip still-image artifact save.
          --no-video                Skip MJPEG AVI artifact save.
          --json                   Print JSON after the readable summary.
          --output <path>          Write JSON result to a file.

        MJPEG AVI options:
          --input-dir <path>       Directory containing JPEG frame files.
          --pattern <glob>         Frame file glob. Default: frame-*.jpg.
          --output <path>          AVI output path.
          --width <pixels>         Frame width. Default: 640.
          --height <pixels>        Frame height. Default: 480.
          --fps <value>            Frames per second. Default: 2.
        """);
}
