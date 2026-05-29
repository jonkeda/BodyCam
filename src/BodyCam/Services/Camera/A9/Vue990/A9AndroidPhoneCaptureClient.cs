using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace BodyCam.Services.Camera.A9.Vue990;

public sealed class A9AndroidPhoneCaptureClient
{
    private const string DefaultPackageName = "com.bodycam.a9phoneprobe";

    public async Task<A9AndroidPhoneCaptureResult> CaptureAsync(
        A9AndroidPhoneCaptureOptions options,
        CancellationToken ct = default)
    {
        var timestamp = DateTimeOffset.Now;
        var stamp = timestamp.ToString("yyyy-MM-dd-HHmmss");
        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var result = new A9AndroidPhoneCaptureResult
        {
            Timestamp = timestamp,
            AdbPath = options.AdbPath,
            PackageName = options.PackageName,
            CameraHost = options.CameraHost,
            OutputDirectory = outputDirectory,
        };

        try
        {
            var devices = await RunAdbTextAsync(options.AdbPath, options.AdbTimeout, ct, "devices")
                .ConfigureAwait(false);
            result.AdbDevices = devices.StdOut;
            if (!HasAdbDevice(devices.StdOut))
                return Fail(result, "No authorized Android device is connected over ADB.");

            var wifi = await RunAdbTextAsync(
                options.AdbPath,
                options.AdbTimeout,
                ct,
                "shell",
                "ip",
                "-4",
                "addr",
                "show",
                "wlan0").ConfigureAwait(false);
            result.PhoneWifi = wifi.StdOut;
            if (options.RequirePhoneSubnet &&
                !wifi.StdOut.Contains(options.RequiredPhoneSubnet, StringComparison.Ordinal))
            {
                return Fail(
                    result,
                    $"Phone wlan0 is not on expected camera subnet prefix {options.RequiredPhoneSubnet}.");
            }

            if (options.InstallApk)
            {
                var apk = ResolveApkPath(options.ApkPath);
                if (apk is null)
                    return Fail(result, "A9 phone probe APK not found; build tools/BodyCam.A9PhoneProbe first.");

                result.ApkPath = apk;
                await RunAdbTextAsync(options.AdbPath, TimeSpan.FromSeconds(90), ct, "install", "-r", apk)
                    .ConfigureAwait(false);
            }

            var resolve = await RunAdbTextAsync(
                options.AdbPath,
                options.AdbTimeout,
                ct,
                "shell",
                "cmd",
                "package",
                "resolve-activity",
                "--brief",
                options.PackageName).ConfigureAwait(false);
            var activity = ResolveActivity(resolve.StdOut);
            if (string.IsNullOrWhiteSpace(activity))
                return Fail(result, $"Could not resolve launcher activity for {options.PackageName}.");

            result.Activity = activity;
            await RunAdbTextAsync(options.AdbPath, options.AdbTimeout, ct, "shell", "am", "force-stop", options.PackageName)
                .ConfigureAwait(false);
            await TryRunAdbTextAsync(
                options.AdbPath,
                TimeSpan.FromSeconds(5),
                ct,
                "shell",
                "run-as",
                options.PackageName,
                "rm",
                "files/latest-a9-phone-probe.txt").ConfigureAwait(false);
            await RunAdbTextAsync(options.AdbPath, options.AdbTimeout, ct, "logcat", "-c")
                .ConfigureAwait(false);

            var startArgs = new List<string>
            {
                "shell",
                "am",
                "start",
                "-n",
                $"{options.PackageName}/{activity}",
                "--ez",
                "autorun",
                "true",
                "--ez",
                "ppcs",
                "true",
            };

            if (options.CaptureImage)
            {
                startArgs.Add("--ez");
                startArgs.Add("capture_image");
                startArgs.Add("true");
            }

            if (options.CaptureVideo)
            {
                startArgs.Add("--ez");
                startArgs.Add("capture_video");
                startArgs.Add("true");
            }

            if (options.FakeRelay)
            {
                startArgs.Add("--ez");
                startArgs.Add("fake_relay");
                startArgs.Add("true");
            }

            if (options.ChannelOracle)
            {
                startArgs.Add("--ez");
                startArgs.Add("native_channel_oracle");
                startArgs.Add("true");
            }

            if (options.ManagedLiveCgi || !string.IsNullOrWhiteSpace(options.ManagedLiveCgiMode))
            {
                startArgs.Add("--ez");
                startArgs.Add("managed_live_cgi");
                startArgs.Add("true");
            }

            if (!string.IsNullOrWhiteSpace(options.ManagedLiveCgiMode))
            {
                startArgs.Add("--es");
                startArgs.Add("managed_live_cgi_mode");
                startArgs.Add(options.ManagedLiveCgiMode);
            }

            startArgs.Add("--es");
            startArgs.Add("host");
            startArgs.Add(options.CameraHost);

            if (!string.IsNullOrWhiteSpace(options.ServerParameterOverride))
            {
                startArgs.Add("--es");
                startArgs.Add("server_override");
                startArgs.Add(options.ServerParameterOverride);
            }

            await RunAdbTextAsync(options.AdbPath, options.AdbTimeout, ct, startArgs.ToArray())
                .ConfigureAwait(false);

            result.Report = await WaitForReportAsync(
                options.AdbPath,
                options.PackageName,
                options.ReportTimeout,
                ct).ConfigureAwait(false);

            var logcat = await RunAdbTextAsync(options.AdbPath, options.AdbTimeout, ct, "logcat", "-d", "-v", "time")
                .ConfigureAwait(false);
            result.FilteredLogcat = FilterLogcat(logcat.StdOut);

            result.LocalReportPath = Path.Combine(outputDirectory, $"a9-android-csharp-capture-{stamp}.txt");
            result.LocalLogcatPath = Path.Combine(outputDirectory, $"a9-android-csharp-capture-logcat-{stamp}.txt");
            await File.WriteAllTextAsync(result.LocalReportPath, result.Report, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(result.LocalLogcatPath, result.FilteredLogcat, ct).ConfigureAwait(false);

            if (!result.Report.Contains("JNIApi.destroy=done", StringComparison.Ordinal))
                return Fail(result, "Android C# probe did not complete cleanly.");
            if (result.Report.Contains("Fatal:", StringComparison.Ordinal) ||
                result.FilteredLogcat.Contains("FATAL EXCEPTION", StringComparison.Ordinal) ||
                result.FilteredLogcat.Contains("AndroidRuntime", StringComparison.Ordinal))
            {
                return Fail(result, "Android C# probe reported a fatal error.");
            }

            if (options.CaptureImage)
                await PullStillAsync(result, options, stamp, ct).ConfigureAwait(false);

            if (options.CaptureVideo)
                await PullFramesAndBuildAviAsync(result, options, stamp, ct).ConfigureAwait(false);

            if (options.ChannelOracle)
                await PullChannelOracleArtifactsAsync(result, options, ct).ConfigureAwait(false);

            result.Success = options.ChannelOracle
                ? result.ChannelOracleArtifacts.Count > 0 && result.StillImage is not null && result.Video is not null
                : options.FakeRelay
                ? result.Report.Contains("fake relay: connections=", StringComparison.Ordinal) &&
                  !result.Report.Contains("fake relay: connections=0", StringComparison.Ordinal)
                : result.StillImage is not null || result.Video is not null;
            if (!result.Success)
            {
                result.Error = options.ChannelOracle
                    ? "Probe completed, but no native channel JPEG/video artifact was extracted."
                    : options.FakeRelay
                    ? "Probe completed, but the fake relay report was not found."
                    : "Probe completed, but no image or video artifact path was found in the report.";
            }

            return result;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            return Fail(result, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task PullStillAsync(
        A9AndroidPhoneCaptureResult result,
        A9AndroidPhoneCaptureOptions options,
        string stamp,
        CancellationToken ct)
    {
        var devicePath = ExtractCapturePath(result.Report);
        if (string.IsNullOrWhiteSpace(devicePath))
            return;

        var bytes = await RunAdbBytesAsync(
            options.AdbPath,
            TimeSpan.FromSeconds(30),
            ct,
            "exec-out",
            "run-as",
            options.PackageName,
            "cat",
            devicePath).ConfigureAwait(false);

        var fileName = Path.GetFileName(devicePath.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = $"a9-capture-{stamp}.jpg";

        var localPath = Path.Combine(result.OutputDirectory, fileName);
        await File.WriteAllBytesAsync(localPath, bytes, ct).ConfigureAwait(false);
        result.StillImage = A9AndroidPhoneCaptureArtifact.FromBytes(localPath, devicePath, bytes);
    }

    private static async Task PullFramesAndBuildAviAsync(
        A9AndroidPhoneCaptureResult result,
        A9AndroidPhoneCaptureOptions options,
        string stamp,
        CancellationToken ct)
    {
        var devicePaths = ExtractFramePaths(result.Report);
        if (devicePaths.Count == 0)
            return;

        var frameDir = Path.Combine(result.OutputDirectory, $"a9-video-{stamp}-frames");
        Directory.CreateDirectory(frameDir);

        var frames = new List<byte[]>(devicePaths.Count);
        for (var i = 0; i < devicePaths.Count; i++)
        {
            var bytes = await RunAdbBytesAsync(
                options.AdbPath,
                TimeSpan.FromSeconds(30),
                ct,
                "exec-out",
                "run-as",
                options.PackageName,
                "cat",
                devicePaths[i]).ConfigureAwait(false);

            if (bytes.Length < 2 || bytes[0] != 0xff || bytes[1] != 0xd8)
                throw new InvalidOperationException($"Frame {i} was not a JPEG.");

            var localFrame = Path.Combine(frameDir, $"frame-{i:000}.jpg");
            await File.WriteAllBytesAsync(localFrame, bytes, ct).ConfigureAwait(false);
            frames.Add(bytes);
            result.VideoFrames.Add(A9AndroidPhoneCaptureArtifact.FromBytes(localFrame, devicePaths[i], bytes));
        }

        var aviPath = Path.Combine(result.OutputDirectory, $"a9-video-{stamp}-mjpeg.avi");
        A9MjpegAviWriter.Write(
            aviPath,
            frames,
            options.VideoWidth,
            options.VideoHeight,
            options.FramesPerSecond);

        var aviBytes = await File.ReadAllBytesAsync(aviPath, ct).ConfigureAwait(false);
        result.Video = A9AndroidPhoneCaptureArtifact.FromBytes(aviPath, null, aviBytes);
    }

    private static async Task PullChannelOracleArtifactsAsync(
        A9AndroidPhoneCaptureResult result,
        A9AndroidPhoneCaptureOptions options,
        CancellationToken ct)
    {
        var devicePaths = ExtractChannelOraclePaths(result.Report);
        if (devicePaths.Count == 0)
            return;

        var oracleDir = Path.Combine(result.OutputDirectory, "native-channel-oracle");
        var frameDir = Path.Combine(result.OutputDirectory, "native-channel-oracle-frames");
        Directory.CreateDirectory(oracleDir);
        Directory.CreateDirectory(frameDir);

        var frames = new List<byte[]>();
        for (var i = 0; i < devicePaths.Count; i++)
        {
            var bytes = await RunAdbBytesAsync(
                options.AdbPath,
                TimeSpan.FromSeconds(30),
                ct,
                "exec-out",
                "run-as",
                options.PackageName,
                "cat",
                devicePaths[i]).ConfigureAwait(false);

            var fileName = Path.GetFileName(devicePaths[i].Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"native-channel-oracle-{i:00}.bin";

            var localPath = Path.Combine(oracleDir, fileName);
            await File.WriteAllBytesAsync(localPath, bytes, ct).ConfigureAwait(false);
            result.ChannelOracleArtifacts.Add(A9AndroidPhoneCaptureArtifact.FromBytes(localPath, devicePaths[i], bytes));

            foreach (var frame in A9Vue990ChannelMediaExtractor.ExtractJpegFrames(bytes))
            {
                var framePath = Path.Combine(frameDir, $"channel-frame-{frames.Count:000}.jpg");
                await File.WriteAllBytesAsync(framePath, frame.Bytes, ct).ConfigureAwait(false);
                frames.Add(frame.Bytes);

                var artifact = A9AndroidPhoneCaptureArtifact.FromBytes(
                    framePath,
                    $"{devicePaths[i]}#jpeg-offset-{frame.Offset}",
                    frame.Bytes);
                result.VideoFrames.Add(artifact);
                result.StillImage ??= artifact;
            }
        }

        if (frames.Count == 0)
            return;

        var aviPath = Path.Combine(result.OutputDirectory, "native-channel-oracle-mjpeg.avi");
        A9MjpegAviWriter.Write(
            aviPath,
            frames,
            options.VideoWidth,
            options.VideoHeight,
            options.FramesPerSecond);

        var aviBytes = await File.ReadAllBytesAsync(aviPath, ct).ConfigureAwait(false);
        result.Video = A9AndroidPhoneCaptureArtifact.FromBytes(aviPath, null, aviBytes);
    }

    private static A9AndroidPhoneCaptureResult Fail(A9AndroidPhoneCaptureResult result, string error)
    {
        result.Success = false;
        result.Error = error;
        return result;
    }

    private static string? ResolveApkPath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? Path.GetFullPath(configured) : null;

        var root = FindRepoRoot();
        if (root is null)
            return null;

        var apk = Path.Combine(
            root,
            "tools",
            "BodyCam.A9PhoneProbe",
            "bin",
            "Debug",
            "net10.0-android",
            "com.bodycam.a9phoneprobe-Signed.apk");

        return File.Exists(apk) ? apk : null;
    }

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "tools", "BodyCam.A9PhoneProbe")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }

    private static bool HasAdbDevice(string adbDevicesOutput)
    {
        return adbDevicesOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => line.EndsWith("\tdevice", StringComparison.Ordinal));
    }

    private static string? ResolveActivity(string resolveActivityOutput)
    {
        return resolveActivityOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => line.Contains('/', StringComparison.Ordinal))?
            .Split('/', 2)[1];
    }

    private static async Task<string> WaitForReportAsync(
        string adb,
        string packageName,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var report = string.Empty;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await TryRunAdbTextAsync(
                adb,
                TimeSpan.FromSeconds(5),
                ct,
                "shell",
                "run-as",
                packageName,
                "cat",
                "files/latest-a9-phone-probe.txt").ConfigureAwait(false);

            if (result?.ExitCode == 0)
            {
                report = result.StdOut;
                if (report.Contains("JNIApi.destroy=done", StringComparison.Ordinal) ||
                    report.Contains("Fatal:", StringComparison.Ordinal))
                {
                    return report;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }

        return report;
    }

    private static string? ExtractCapturePath(string report)
    {
        const string marker = "captureImage exists=true path=";
        foreach (var line in report.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var start = line.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                continue;

            start += marker.Length;
            var end = line.IndexOf(" bytes=", start, StringComparison.Ordinal);
            return end > start ? line[start..end] : null;
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractFramePaths(string report)
    {
        var paths = new List<string>();
        const string pathMarker = " exists=true path=";

        foreach (var line in report.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.Contains("captureVideo frame[", StringComparison.Ordinal))
                continue;

            var start = line.IndexOf(pathMarker, StringComparison.Ordinal);
            if (start < 0)
                continue;

            start += pathMarker.Length;
            var end = line.IndexOf(" bytes=", start, StringComparison.Ordinal);
            if (end > start)
                paths.Add(line[start..end]);
        }

        return paths.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> ExtractChannelOraclePaths(string report)
    {
        var paths = new List<string>();
        const string marker = "nativeChannelOracle saved[";
        const string pathMarker = " path=";

        foreach (var line in report.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.Contains(marker, StringComparison.Ordinal))
                continue;

            var start = line.IndexOf(pathMarker, StringComparison.Ordinal);
            if (start < 0)
                continue;

            start += pathMarker.Length;
            var end = line.IndexOf(" bytes=", start, StringComparison.Ordinal);
            if (end > start)
                paths.Add(line[start..end]);
        }

        return paths.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string FilterLogcat(string logcat)
    {
        string[] needles =
        [
            "A9PhoneProbe",
            "A9PPCS",
            "PpcsProbeBridge",
            "OKSMART",
            "HLP2P",
            "hl_p2p",
            "_p2p",
            "kaven",
            "das,",
            "app_source_live",
            "app_player",
            "P2PClient",
            "JNIApi",
            "FATAL EXCEPTION",
            "AndroidRuntime",
        ];

        var lines = logcat
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => needles.Any(needle => line.Contains(needle, StringComparison.OrdinalIgnoreCase)));

        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<AdbTextResult?> TryRunAdbTextAsync(
        string adb,
        TimeSpan timeout,
        CancellationToken ct,
        params string[] arguments)
    {
        try
        {
            return await RunAdbTextAsync(adb, timeout, ct, arguments).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<AdbTextResult> RunAdbTextAsync(
        string adb,
        TimeSpan timeout,
        CancellationToken ct,
        params string[] arguments)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);

        var startInfo = new ProcessStartInfo(adb)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException($"Could not start {adb}.");

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);

            var result = new AdbTextResult(
                process.ExitCode,
                await stdoutTask.ConfigureAwait(false),
                await stderrTask.ConfigureAwait(false));

            if (result.ExitCode != 0)
                throw new InvalidOperationException($"{adb} {string.Join(' ', arguments)} failed: {result.StdErr}");

            return result;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static async Task<byte[]> RunAdbBytesAsync(
        string adb,
        TimeSpan timeout,
        CancellationToken ct,
        params string[] arguments)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);

        var startInfo = new ProcessStartInfo(adb)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException($"Could not start {adb}.");

        try
        {
            using var stdout = new MemoryStream();
            var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(stdout, timeoutSource.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            await stdoutTask.ConfigureAwait(false);

            var stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{adb} {string.Join(' ', arguments)} failed: {stderr}");

            return stdout.ToArray();
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed record AdbTextResult(int ExitCode, string StdOut, string StdErr);
}

public sealed class A9AndroidPhoneCaptureOptions
{
    public string AdbPath { get; init; } = "adb";

    public string PackageName { get; init; } = "com.bodycam.a9phoneprobe";

    public string CameraHost { get; init; } = "192.168.168.1";

    public string RequiredPhoneSubnet { get; init; } = "192.168.168.";

    public bool RequirePhoneSubnet { get; init; } = true;

    public string? ApkPath { get; init; }

    public bool InstallApk { get; init; } = true;

    public bool CaptureImage { get; init; } = true;

    public bool CaptureVideo { get; init; } = true;

    public string? ServerParameterOverride { get; init; }

    public bool FakeRelay { get; init; }

    public bool ChannelOracle { get; init; }

    public bool ManagedLiveCgi { get; init; }

    public string? ManagedLiveCgiMode { get; init; }

    public string OutputDirectory { get; init; } =
        Path.Combine(Environment.CurrentDirectory, ".my", "plan", "m38-a9-camera", "captures", "phase-27-android-csharp-orchestrated");

    public TimeSpan AdbTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ReportTimeout { get; init; } = TimeSpan.FromSeconds(130);

    public int VideoWidth { get; init; } = 640;

    public int VideoHeight { get; init; } = 480;

    public int FramesPerSecond { get; init; } = 2;
}

public sealed class A9AndroidPhoneCaptureResult
{
    public DateTimeOffset Timestamp { get; init; }

    public required string AdbPath { get; init; }

    public required string PackageName { get; init; }

    public required string CameraHost { get; init; }

    public required string OutputDirectory { get; init; }

    public bool Success { get; set; }

    public string? Error { get; set; }

    public string? ApkPath { get; set; }

    public string? Activity { get; set; }

    public string? AdbDevices { get; set; }

    public string? PhoneWifi { get; set; }

    public string Report { get; set; } = string.Empty;

    public string FilteredLogcat { get; set; } = string.Empty;

    public string? LocalReportPath { get; set; }

    public string? LocalLogcatPath { get; set; }

    public A9AndroidPhoneCaptureArtifact? StillImage { get; set; }

    public List<A9AndroidPhoneCaptureArtifact> VideoFrames { get; } = [];

    public A9AndroidPhoneCaptureArtifact? Video { get; set; }

    public List<A9AndroidPhoneCaptureArtifact> ChannelOracleArtifacts { get; } = [];

    public string ToReadableString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("A9 Android C# capture");
        sb.AppendLine($"Timestamp: {Timestamp:O}");
        sb.AppendLine($"Success: {Success}");
        sb.AppendLine($"Camera host: {CameraHost}");
        sb.AppendLine($"Package: {PackageName}");
        sb.AppendLine($"Output: {OutputDirectory}");

        if (!string.IsNullOrWhiteSpace(Error))
            sb.AppendLine($"Error: {Error}");
        if (!string.IsNullOrWhiteSpace(ApkPath))
            sb.AppendLine($"APK: {ApkPath}");
        if (!string.IsNullOrWhiteSpace(Activity))
            sb.AppendLine($"Activity: {Activity}");
        if (!string.IsNullOrWhiteSpace(LocalReportPath))
            sb.AppendLine($"Report: {LocalReportPath}");
        if (!string.IsNullOrWhiteSpace(LocalLogcatPath))
            sb.AppendLine($"Logcat: {LocalLogcatPath}");

        if (StillImage is not null)
        {
            sb.AppendLine(
                $"Still: {StillImage.LocalPath} bytes={StillImage.Bytes} sha256={StillImage.Sha256}");
        }

        sb.AppendLine($"Frames: {VideoFrames.Count}");
        foreach (var frame in VideoFrames)
            sb.AppendLine($"- {frame.LocalPath} bytes={frame.Bytes} sha256={frame.Sha256}");

        if (Video is not null)
        {
            sb.AppendLine(
                $"Video: {Video.LocalPath} bytes={Video.Bytes} sha256={Video.Sha256}");
        }

        sb.AppendLine($"Channel oracle artifacts: {ChannelOracleArtifacts.Count}");
        foreach (var artifact in ChannelOracleArtifacts)
            sb.AppendLine($"- {artifact.LocalPath} bytes={artifact.Bytes} sha256={artifact.Sha256}");

        return sb.ToString();
    }
}

public sealed record A9AndroidPhoneCaptureArtifact(
    string LocalPath,
    string? DevicePath,
    long Bytes,
    string Sha256)
{
    public static A9AndroidPhoneCaptureArtifact FromBytes(string localPath, string? devicePath, byte[] bytes)
    {
        return new A9AndroidPhoneCaptureArtifact(
            Path.GetFullPath(localPath),
            devicePath,
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
