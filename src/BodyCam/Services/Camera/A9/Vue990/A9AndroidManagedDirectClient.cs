using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace BodyCam.Services.Camera.A9.Vue990;

public sealed class A9AndroidManagedDirectClient
{
    private const string DefaultPackageName = "com.bodycam.a9phoneprobe";

    public async Task<A9AndroidManagedDirectResult> ProbeAsync(
        A9AndroidManagedDirectOptions options,
        CancellationToken ct = default)
    {
        var timestamp = DateTimeOffset.Now;
        var stamp = timestamp.ToString("yyyy-MM-dd-HHmmss");
        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        var modeName = options.ManagedLanHoleOnly ? "managed-lan-hole" : "managed-direct";
        Directory.CreateDirectory(outputDirectory);

        var result = new A9AndroidManagedDirectResult
        {
            Timestamp = timestamp,
            AdbPath = options.AdbPath,
            PackageName = options.PackageName,
            CameraHost = options.CameraHost,
            OutputDirectory = outputDirectory,
            ManagedLanHoleOnly = options.ManagedLanHoleOnly,
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
                await RunAdbTextAsync(options.AdbPath, TimeSpan.FromSeconds(90), ct, "install", "-r", "-g", apk)
                    .ConfigureAwait(false);
                await TryGrantAndroidRuntimePermissionsAsync(options.AdbPath, options.PackageName, options.AdbTimeout, ct)
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
                activity,
                "--ez",
                "autorun",
                "true",
                "--ez",
                options.ManagedLanHoleOnly ? "managed_lan_hole" : "managed_direct",
                "true",
                "--es",
                "host",
                options.CameraHost,
            };
            if (!options.ManagedLanHoleOnly)
            {
                startArgs.Add("--ez");
                startArgs.Add("capture_image");
                startArgs.Add(options.CaptureImage ? "true" : "false");
                startArgs.Add("--ez");
                startArgs.Add("capture_video");
                startArgs.Add(options.CaptureVideo ? "true" : "false");
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

            result.LocalReportPath = Path.Combine(outputDirectory, $"a9-android-{modeName}-{stamp}.txt");
            result.LocalLogcatPath = Path.Combine(outputDirectory, $"a9-android-{modeName}-logcat-{stamp}.txt");
            await File.WriteAllTextAsync(result.LocalReportPath, result.Report, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(result.LocalLogcatPath, result.FilteredLogcat, ct).ConfigureAwait(false);

            var reportComplete = result.Report.Contains("Managed-direct C# probe complete", StringComparison.Ordinal) ||
                                 result.Report.Contains("managed-direct summary:", StringComparison.Ordinal) ||
                                 result.Report.Contains("Managed LAN-hole C# probe complete", StringComparison.Ordinal) ||
                                 result.Report.Contains("managed-lan-hole summary:", StringComparison.Ordinal);
            if (result.Report.Contains("Fatal:", StringComparison.Ordinal) ||
                result.FilteredLogcat.Contains("FATAL EXCEPTION", StringComparison.Ordinal))
            {
                return Fail(result, "Android managed-direct probe reported a fatal error.");
            }

            await PullArtifactsAsync(result, options, ct).ConfigureAwait(false);

            result.Success = reportComplete;
            result.CapturedImage = result.Artifacts.Any(artifact =>
                artifact.LocalPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));
            result.CapturedVideo = result.Artifacts.Any(artifact =>
                artifact.LocalPath.EndsWith(".avi", StringComparison.OrdinalIgnoreCase));
            if (!reportComplete)
                result.Error = $"Timed out waiting for {modeName} completion report.";

            return result;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or OperationCanceledException)
        {
            return Fail(result, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task PullArtifactsAsync(
        A9AndroidManagedDirectResult result,
        A9AndroidManagedDirectOptions options,
        CancellationToken ct)
    {
        var deviceFiles = ExtractArtifactPaths(result.Report);
        foreach (var devicePath in deviceFiles.Distinct(StringComparer.Ordinal))
        {
            var localPath = Path.Combine(result.OutputDirectory, Path.GetFileName(devicePath));
            var pull = await TryRunAdbBinaryAsync(
                    options.AdbPath,
                    options.AdbTimeout,
                    ct,
                    "exec-out",
                    "run-as",
                    options.PackageName,
                    "cat",
                    devicePath)
                .ConfigureAwait(false);

            if (pull.ExitCode != 0 || pull.StdOut.Length == 0)
                continue;

            await File.WriteAllBytesAsync(localPath, pull.StdOut, ct).ConfigureAwait(false);
            result.Artifacts.Add(A9AndroidManagedDirectArtifact.FromBytes(localPath, devicePath, pull.StdOut));
        }
    }

    private static IReadOnlyList<string> ExtractArtifactPaths(string report)
    {
        var paths = new List<string>();
        foreach (var token in report.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var value = token;
            var equals = value.IndexOf('=', StringComparison.Ordinal);
            if (equals >= 0)
                value = value[(equals + 1)..];

            value = value.TrimEnd(',', ';');
            if (!value.StartsWith("/data/", StringComparison.Ordinal) &&
                !value.StartsWith("/sdcard/", StringComparison.Ordinal))
            {
                continue;
            }

            if (value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(value);
            }
        }

        return paths;
    }

    private static A9AndroidManagedDirectResult Fail(A9AndroidManagedDirectResult result, string error)
    {
        result.Success = false;
        result.Error = error;
        return result;
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

            if (result.ExitCode == 0)
            {
                report = result.StdOut;
                if (report.Contains("Managed-direct C# probe complete", StringComparison.Ordinal) ||
                    report.Contains("managed-direct summary:", StringComparison.Ordinal) ||
                    report.Contains("Managed LAN-hole C# probe complete", StringComparison.Ordinal) ||
                    report.Contains("managed-lan-hole summary:", StringComparison.Ordinal) ||
                    report.Contains("Fatal:", StringComparison.Ordinal))
                {
                    return report;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }

        return report;
    }

    private static bool HasAdbDevice(string output)
    {
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Contains('\t') && line.Contains("device", StringComparison.Ordinal));
    }

    private static string? ResolveApkPath(string? configured)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured))
            candidates.Add(configured);

        candidates.Add(Path.Combine(
            Environment.CurrentDirectory,
            "tools",
            "BodyCam.A9PhoneProbe",
            "bin",
            "Debug",
            "net10.0-android",
            $"{DefaultPackageName}-Signed.apk"));
        candidates.Add(Path.Combine(
            Environment.CurrentDirectory,
            "tools",
            "BodyCam.A9PhoneProbe",
            "bin",
            "Debug",
            "net10.0-android",
            "BodyCam.A9PhoneProbe-Signed.apk"));

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
    }

    private static string? ResolveActivity(string output)
    {
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .LastOrDefault(line => line.Contains('/'));
    }

    private static string FilterLogcat(string value)
    {
        var keep = new[] { "A9PhoneProbe", "Managed direct", "Managed LAN-hole", "AndroidRuntime" };
        var sb = new StringBuilder();
        foreach (var line in value.Split('\n'))
        {
            if (keep.Any(k => line.Contains(k, StringComparison.OrdinalIgnoreCase)))
                sb.AppendLine(line.TrimEnd());
        }

        return sb.ToString();
    }

    private static async Task TryGrantAndroidRuntimePermissionsAsync(
        string adbPath,
        string packageName,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var permissions = new[]
        {
            "android.permission.NEARBY_WIFI_DEVICES",
            "android.permission.ACCESS_FINE_LOCATION",
        };

        foreach (var permission in permissions)
        {
            await TryRunAdbTextAsync(
                    adbPath,
                    timeout,
                    ct,
                    "shell",
                    "pm",
                    "grant",
                    packageName,
                    permission)
                .ConfigureAwait(false);
        }
    }

    private static Task<AdbResult> TryRunAdbTextAsync(
        string adbPath,
        TimeSpan timeout,
        CancellationToken ct,
        params string[] args)
    {
        return RunAdbTextAsync(adbPath, timeout, ct, throwOnFailure: false, args);
    }

    private static Task<AdbBinaryResult> TryRunAdbBinaryAsync(
        string adbPath,
        TimeSpan timeout,
        CancellationToken ct,
        params string[] args)
    {
        return RunAdbBinaryAsync(adbPath, timeout, ct, throwOnFailure: false, args);
    }

    private static Task<AdbResult> RunAdbTextAsync(
        string adbPath,
        TimeSpan timeout,
        CancellationToken ct,
        params string[] args)
    {
        return RunAdbTextAsync(adbPath, timeout, ct, throwOnFailure: true, args);
    }

    private static async Task<AdbResult> RunAdbTextAsync(
        string adbPath,
        TimeSpan timeout,
        CancellationToken ct,
        bool throwOnFailure,
        params string[] args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = adbPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.Latin1,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        var result = new AdbResult(process.ExitCode, stdout, stderr);
        if (throwOnFailure && result.ExitCode != 0)
            throw new InvalidOperationException($"adb {string.Join(' ', args)} failed: {result.StdErr}");

        return result;
    }

    private sealed record AdbResult(int ExitCode, string StdOut, string StdErr);

    private static async Task<AdbBinaryResult> RunAdbBinaryAsync(
        string adbPath,
        TimeSpan timeout,
        CancellationToken ct,
        bool throwOnFailure,
        params string[] args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = adbPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);

        using var memory = new MemoryStream();
        var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(memory, timeoutSource.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        var result = new AdbBinaryResult(process.ExitCode, memory.ToArray(), stderr);
        if (throwOnFailure && result.ExitCode != 0)
            throw new InvalidOperationException($"adb {string.Join(' ', args)} failed: {result.StdErr}");

        return result;
    }

    private sealed record AdbBinaryResult(int ExitCode, byte[] StdOut, string StdErr);
}

public sealed class A9AndroidManagedDirectOptions
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

    public bool ManagedLanHoleOnly { get; init; }

    public string OutputDirectory { get; init; } =
        Path.Combine(Environment.CurrentDirectory, ".my", "plan", "m38-a9-camera", "captures", "phase-33-android-managed-direct");

    public TimeSpan AdbTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ReportTimeout { get; init; } = TimeSpan.FromSeconds(90);
}

public sealed class A9AndroidManagedDirectResult
{
    public DateTimeOffset Timestamp { get; init; }

    public required string AdbPath { get; init; }

    public required string PackageName { get; init; }

    public required string CameraHost { get; init; }

    public required string OutputDirectory { get; init; }

    public bool ManagedLanHoleOnly { get; init; }

    public bool Success { get; set; }

    public bool CapturedImage { get; set; }

    public bool CapturedVideo { get; set; }

    public string? Error { get; set; }

    public string? ApkPath { get; set; }

    public string? Activity { get; set; }

    public string? AdbDevices { get; set; }

    public string? PhoneWifi { get; set; }

    public string Report { get; set; } = string.Empty;

    public string FilteredLogcat { get; set; } = string.Empty;

    public string? LocalReportPath { get; set; }

    public string? LocalLogcatPath { get; set; }

    public List<A9AndroidManagedDirectArtifact> Artifacts { get; } = [];

    public string ToReadableString()
    {
        var sb = new StringBuilder();
        sb.AppendLine(ManagedLanHoleOnly
            ? "A9 Android managed LAN-hole C# probe"
            : "A9 Android managed-direct C# probe");
        sb.AppendLine($"Timestamp: {Timestamp:O}");
        sb.AppendLine($"Success: {Success}");
        sb.AppendLine($"Captured image: {CapturedImage}");
        sb.AppendLine($"Captured video: {CapturedVideo}");
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

        sb.AppendLine($"Artifacts: {Artifacts.Count}");
        foreach (var artifact in Artifacts)
            sb.AppendLine($"- {artifact.LocalPath} bytes={artifact.Bytes} sha256={artifact.Sha256}");

        return sb.ToString();
    }
}

public sealed record A9AndroidManagedDirectArtifact(
    string LocalPath,
    string DevicePath,
    long Bytes,
    string Sha256)
{
    public static A9AndroidManagedDirectArtifact FromBytes(string localPath, string devicePath, byte[] bytes)
    {
        return new A9AndroidManagedDirectArtifact(
            Path.GetFullPath(localPath),
            devicePath,
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
