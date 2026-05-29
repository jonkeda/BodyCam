#if WINDOWS
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using BodyCam.Services.Camera.A9.Vue990;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.A9;

public class A9PhonePpcsPlayerRealTests
{
    private const string DefaultPackageName = "com.bodycam.a9phoneprobe";

    private readonly ITestOutputHelper _output;

    public A9PhonePpcsPlayerRealTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact, Trait("Category", "RealHardware")]
    public async Task A9PhonePpcsPlayer_OpensLiveStreamAndReportsMetadata()
    {
        Skip.IfNot(A9RealTestSettings.Enabled, "A9_E2E not set to 1");
        Skip.IfNot(
            Environment.GetEnvironmentVariable("A9_PHONE_PPCS_E2E") == "1",
            "A9_PHONE_PPCS_E2E not set to 1");

        var run = await RunPhonePpcsProbeAsync(captureImage: false, TimeSpan.FromSeconds(85));
        SaveArtifacts(run.Report, run.FilteredLogcat, "a9-phone-ppcs-realtest");

        _output.WriteLine(run.Report);
        _output.WriteLine(run.FilteredLogcat);

        AssertPpcsMetadata(run.Report);
        AssertNoFatalLogcat(run.FilteredLogcat);
    }

    [SkippableFact, Trait("Category", "RealHardware")]
    public async Task A9PhonePpcsPlayer_CapturesStillImage()
    {
        Skip.IfNot(A9RealTestSettings.Enabled, "A9_E2E not set to 1");
        Skip.IfNot(
            Environment.GetEnvironmentVariable("A9_PHONE_CAPTURE_E2E") == "1",
            "A9_PHONE_CAPTURE_E2E not set to 1");

        var run = await RunPhonePpcsProbeAsync(captureImage: true, TimeSpan.FromSeconds(95));
        SaveArtifacts(run.Report, run.FilteredLogcat, "a9-phone-capture-realtest");

        _output.WriteLine(run.Report);
        _output.WriteLine(run.FilteredLogcat);

        AssertPpcsMetadata(run.Report);
        AssertNoFatalLogcat(run.FilteredLogcat);

        run.Report.Should().Contain("AppPlayerApi.screenshot path=");
        run.Report.Should().MatchRegex(@"captureImage exists=true path=.* bytes=\d+ dimensions=640x480 sha256=[0-9A-F]{64}");

        var devicePath = ExtractCapturePath(run.Report);
        devicePath.Should().NotBeNullOrWhiteSpace();

        var localPath = await PullCaptureAsync(run.Adb, run.PackageName, devicePath!);
        _output.WriteLine($"Pulled capture: {localPath}");
        new FileInfo(localPath).Length.Should().BeGreaterThan(1024);
    }

    [SkippableFact, Trait("Category", "RealHardware")]
    public async Task A9PhonePpcsPlayer_CapturesShortVideoArtifact()
    {
        Skip.IfNot(A9RealTestSettings.Enabled, "A9_E2E not set to 1");
        Skip.IfNot(
            Environment.GetEnvironmentVariable("A9_PHONE_VIDEO_E2E") == "1",
            "A9_PHONE_VIDEO_E2E not set to 1");

        var run = await RunPhonePpcsProbeAsync(
            captureImage: false,
            reportTimeout: TimeSpan.FromSeconds(115),
            captureVideo: true);
        SaveArtifacts(run.Report, run.FilteredLogcat, "a9-phone-video-realtest");

        _output.WriteLine(run.Report);
        _output.WriteLine(run.FilteredLogcat);

        AssertPpcsMetadata(run.Report);
        AssertNoFatalLogcat(run.FilteredLogcat);

        run.Report.Should().Contain("captureVideo startDown path=");
        run.Report.Should().Contain("captureVideo startDown fallback=mjpeg-avi-screenshot-sequence");
        run.Report.Should().MatchRegex(@"captureVideo frame\[\d+\] exists=true path=.* bytes=\d+ dimensions=640x480 sha256=[0-9A-F]{64}");
        run.Report.Should().MatchRegex(@"captureVideo frameSequence manifest=.* frames=\d+ fps=2 width=640 height=480");
        run.Report.Should().Contain("captureVideo mjpegAvi skipped=assemble-on-windows-csharp");

        var framePaths = ExtractFramePaths(run.Report);
        framePaths.Should().HaveCountGreaterThanOrEqualTo(2);

        var localPath = await PullFrameSequenceAsAviAsync(run.Adb, run.PackageName, framePaths);
        _output.WriteLine($"Pulled video artifact: {localPath}");

        var localBytes = await File.ReadAllBytesAsync(localPath);
        localBytes.Length.Should().BeGreaterThan(10_000);
        Encoding.ASCII.GetString(localBytes, 0, 4).Should().Be("RIFF");
        Encoding.ASCII.GetString(localBytes, 8, 4).Should().Be("AVI ");
    }

    private async Task<PhoneProbeRun> RunPhonePpcsProbeAsync(
        bool captureImage,
        TimeSpan reportTimeout,
        bool captureVideo = false)
    {
        var adb = Environment.GetEnvironmentVariable("A9_ADB_PATH") ?? "adb";
        var packageName = Environment.GetEnvironmentVariable("A9_PHONE_PROBE_PACKAGE") ?? DefaultPackageName;
        var cameraHost = Environment.GetEnvironmentVariable("A9_CAMERA_IP") ?? "192.168.168.1";
        var requiredPhoneSubnet = Environment.GetEnvironmentVariable("A9_PHONE_WIFI_SUBNET") ?? "192.168.168.";
        var apk = FindProbeApk();

        Skip.If(string.IsNullOrWhiteSpace(apk), "A9 phone probe APK not found; build tools/BodyCam.A9PhoneProbe first.");

        var devices = await TryRunAdbAsync(adb, TimeSpan.FromSeconds(10), "devices");
        Skip.If(devices is null, $"ADB was not found: {adb}");
        Skip.If(!HasAdbDevice(devices.StdOut), "No authorized Android device is connected over ADB.");

        var wifi = await RunAdbAsync(adb, TimeSpan.FromSeconds(10), "shell", "ip", "addr", "show", "wlan0");
        _output.WriteLine(wifi.StdOut);
        Skip.If(
            !wifi.StdOut.Contains(requiredPhoneSubnet, StringComparison.Ordinal),
            $"Phone wlan0 is not on the expected camera subnet prefix {requiredPhoneSubnet}.");

        await RunAdbAsync(adb, TimeSpan.FromSeconds(90), "install", "-r", apk!);
        var resolve = await RunAdbAsync(adb, TimeSpan.FromSeconds(10), "shell", "cmd", "package", "resolve-activity", "--brief", packageName);
        var activity = ResolveActivity(resolve.StdOut);
        Skip.If(string.IsNullOrWhiteSpace(activity), $"Could not resolve launcher activity for {packageName}.");

        await RunAdbAsync(adb, TimeSpan.FromSeconds(10), "shell", "am", "force-stop", packageName);
        await TryRunAdbAsync(
            adb,
            TimeSpan.FromSeconds(5),
            "shell",
            "run-as",
            packageName,
            "rm",
            "files/latest-a9-phone-probe.txt");
        await RunAdbAsync(adb, TimeSpan.FromSeconds(10), "logcat", "-c");
        var startArgs = new List<string>
        {
            "shell",
            "am",
            "start",
            "-n",
            $"{packageName}/{activity}",
            "--ez",
            "autorun",
            "true",
            "--ez",
            "ppcs",
            "true",
        };
        if (captureImage)
        {
            startArgs.Add("--ez");
            startArgs.Add("capture_image");
            startArgs.Add("true");
        }
        if (captureVideo)
        {
            startArgs.Add("--ez");
            startArgs.Add("capture_video");
            startArgs.Add("true");
        }

        startArgs.Add("--es");
        startArgs.Add("host");
        startArgs.Add(cameraHost);

        await RunAdbAsync(
            adb,
            TimeSpan.FromSeconds(10),
            startArgs.ToArray());

        var report = await WaitForReportAsync(adb, packageName, reportTimeout);
        var logcat = await RunAdbAsync(adb, TimeSpan.FromSeconds(10), "logcat", "-d", "-v", "time");
        var filteredLogcat = FilterLogcat(logcat.StdOut);

        return new PhoneProbeRun(adb, packageName, report, filteredLogcat);
    }

    private static void AssertPpcsMetadata(string report)
    {
        report.Should().Contain("JNIApi.connect=3");
        report.Should().MatchRegex(@"JNIApi\.login=(True|true)");
        report.Should().MatchRegex(@"JNIApi\.writeCgi live channel=1 cgi=livestream\.cgi\?streamid=10&substream=0& result=(True|true)");
        report.Should().MatchRegex(@"JNIApi\.checkBuffer\[90\] channel=1 result=\[0,0,(?!0\])\d+\]");
        report.Should().MatchRegex(@"AppPlayerApi\.start=(True|true)");
        report.Should().Contain("app_player_draw_info textureId=990001 width=640 height=480");
        report.Should().Contain("JNIApi.destroy=done");
    }

    private static void AssertNoFatalLogcat(string filteredLogcat)
    {
        filteredLogcat.Should().NotContain("FATAL EXCEPTION");
        filteredLogcat.Should().NotContain("AndroidRuntime");
    }

    private static async Task<string> WaitForReportAsync(string adb, string packageName, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var report = string.Empty;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await TryRunAdbAsync(
                adb,
                TimeSpan.FromSeconds(5),
                "shell",
                "run-as",
                packageName,
                "cat",
                "files/latest-a9-phone-probe.txt");

            if (result?.ExitCode == 0)
            {
                report = result.StdOut;
                if (report.Contains("JNIApi.destroy=done", StringComparison.Ordinal) ||
                    report.Contains("Fatal:", StringComparison.Ordinal))
                {
                    return report;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        return report;
    }

    private static string? FindProbeApk()
    {
        var configured = Environment.GetEnvironmentVariable("A9_PHONE_PROBE_APK");
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? configured : null;

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

    private static void SaveArtifacts(string report, string filteredLogcat, string prefix)
    {
        var root = FindRepoRoot();
        if (root is null)
            return;

        var artifactDir = Environment.GetEnvironmentVariable("A9_PHONE_REALTEST_ARTIFACT_DIR") ??
            Path.Combine(root, ".my", "plan", "m38-a9-camera", "captures");
        Directory.CreateDirectory(artifactDir);

        var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd-HHmmss");
        File.WriteAllText(Path.Combine(artifactDir, $"{prefix}-{stamp}.txt"), report);
        File.WriteAllText(Path.Combine(artifactDir, $"{prefix}-logcat-{stamp}.txt"), filteredLogcat);
    }

    private static async Task<string> PullCaptureAsync(string adb, string packageName, string devicePath)
    {
        var root = FindRepoRoot() ?? AppContext.BaseDirectory;
        var artifactDir = Environment.GetEnvironmentVariable("A9_PHONE_CAPTURE_ARTIFACT_DIR") ??
            Path.Combine(root, ".my", "plan", "m38-a9-camera", "captures", "phase-16");
        Directory.CreateDirectory(artifactDir);

        var fileName = Path.GetFileName(devicePath.Replace('\\', '/'));
        var localPath = Path.Combine(artifactDir, fileName);
        var bytes = await RunAdbBytesAsync(
            adb,
            TimeSpan.FromSeconds(30),
            "exec-out",
            "run-as",
            packageName,
            "cat",
            devicePath);

        await File.WriteAllBytesAsync(localPath, bytes);
        return localPath;
    }

    private static async Task<string> PullFrameSequenceAsAviAsync(
        string adb,
        string packageName,
        IReadOnlyList<string> devicePaths)
    {
        var root = FindRepoRoot() ?? AppContext.BaseDirectory;
        var artifactDir = Environment.GetEnvironmentVariable("A9_PHONE_CAPTURE_ARTIFACT_DIR") ??
            Path.Combine(root, ".my", "plan", "m38-a9-camera", "captures", "phase-16");
        var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd-HHmmss");
        var frameDir = Path.Combine(artifactDir, $"a9-video-{stamp}-frames");
        Directory.CreateDirectory(frameDir);

        var frames = new List<byte[]>(devicePaths.Count);
        for (var i = 0; i < devicePaths.Count; i++)
        {
            var bytes = await RunAdbBytesAsync(
                adb,
                TimeSpan.FromSeconds(30),
                "exec-out",
                "run-as",
                packageName,
                "cat",
                devicePaths[i]);

            bytes.Length.Should().BeGreaterThan(1024);
            bytes[0].Should().Be(0xFF);
            bytes[1].Should().Be(0xD8);

            frames.Add(bytes);
            await File.WriteAllBytesAsync(Path.Combine(frameDir, $"frame-{i:000}.jpg"), bytes);
        }

        var localPath = Path.Combine(artifactDir, $"a9-video-{stamp}-mjpeg.avi");
        A9MjpegAviWriter.Write(localPath, frames, width: 640, height: 480, framesPerSecond: 2);
        return localPath;
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

    private static string? ExtractVideoPath(string report)
    {
        const string marker = "captureVideo mjpegAvi exists=true path=";
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
            if (end <= start)
                continue;

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

    private static async Task<CommandResult?> TryRunAdbAsync(
        string adb,
        TimeSpan timeout,
        params string[] arguments)
    {
        try
        {
            return await RunAdbAsync(adb, timeout, arguments);
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<CommandResult> RunAdbAsync(
        string adb,
        TimeSpan timeout,
        params string[] arguments)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
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

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(timeoutSource.Token);

        var result = new CommandResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{adb} {string.Join(' ', arguments)} failed: {result.StdErr}");

        return result;
    }

    private static async Task<byte[]> RunAdbBytesAsync(
        string adb,
        TimeSpan timeout,
        params string[] arguments)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
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

        using var stdout = new MemoryStream();
        var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(stdout, timeoutSource.Token);
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(timeoutSource.Token);
        await stdoutTask;

        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{adb} {string.Join(' ', arguments)} failed: {stderr}");

        return stdout.ToArray();
    }

    private sealed record PhoneProbeRun(string Adb, string PackageName, string Report, string FilteredLogcat);

    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
}
#endif
