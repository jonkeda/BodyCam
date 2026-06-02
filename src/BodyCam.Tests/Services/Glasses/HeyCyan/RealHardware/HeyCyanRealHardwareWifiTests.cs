using FluentAssertions;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan.RealHardware;

public sealed class HeyCyanRealHardwareWifiTests
{
    private const string AndroidPackage = "com.companyname.bodycam";
    private const string ProbeAction = "com.companyname.bodycam.HEYCYAN_PROBE";

    [HeyCyanRealHardwareFact(Timeout = 240_000)]
    public async Task Android_probe_downloads_fresh_photo_and_video_through_csharp_wifi_transfer()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var run = RealHardwareRun.Create();
        ProbeCompletion? completion = null;

        try
        {
            await run.CaptureCommandAsync("adb-devices", TimeSpan.FromSeconds(10), cts.Token, "devices", "-l");

            var state = await run.Adb.RunAsync(TimeSpan.FromSeconds(10), cts.Token, "get-state");
            state.EnsureSuccess();
            state.Stdout.Trim().Should().Be("device", "an unlocked Android phone must be connected over adb");

            var packagePath = await run.Adb.RunAsync(
                TimeSpan.FromSeconds(10),
                cts.Token,
                "shell",
                "pm",
                "path",
                AndroidPackage);
            packagePath.EnsureSuccess();
            packagePath.Stdout.Should().Contain(AndroidPackage, "the BodyCam Android app must be installed first");

            await run.Adb.RunAsync(TimeSpan.FromSeconds(10), cts.Token, "logcat", "-c");
            await run.Adb.RunAsync(TimeSpan.FromSeconds(10), cts.Token, "shell", "svc", "power", "stayon", "true");

            await StartProbeAsync(run.Adb, cts.Token);
            completion = await WaitForProbeCompletionAsync(run.Adb, TimeSpan.FromSeconds(180), cts.Token);

            await run.PullProbeOutputAsync(completion.OutputDirectory, cts.Token);
            var result = await run.ReadProbeResultAsync(cts.Token);

            AssertProbeResult(result);
            await run.WriteSummaryAsync(result, completion, cts.Token);
        }
        finally
        {
            using var diagnosticsCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await run.TryCaptureDiagnosticsAsync(completion?.OutputDirectory, diagnosticsCts.Token);
            await run.Adb.TryRunAsync(TimeSpan.FromSeconds(10), CancellationToken.None, "shell", "svc", "power", "stayon", "false");
        }
    }

    private static async Task StartProbeAsync(AdbRunner adb, CancellationToken ct)
    {
        var launchActivity = await ResolveLaunchActivityAsync(adb, ct);
        var args = new List<string>
        {
            "shell",
            "am",
            "start",
            "-W",
            "-a",
            ProbeAction,
            "-n",
            launchActivity,
            "-p",
            AndroidPackage,
            "--ez",
            "capturePhotoBeforeTransfer",
            "true",
            "--ez",
            "recordVideoBeforeTransfer",
            "true",
            "--ei",
            "videoSeconds",
            ReadIntEnv("BODYCAM_REAL_HEYCYAN_VIDEO_SECONDS", 4).ToString(),
            "--ei",
            "scanTimeoutSeconds",
            ReadIntEnv("BODYCAM_REAL_HEYCYAN_SCAN_SECONDS", 8).ToString(),
            "--ei",
            "holdOnFailureSeconds",
            ReadIntEnv("BODYCAM_REAL_HEYCYAN_HOLD_ON_FAILURE_SECONDS", 20).ToString()
        };

        AddStringExtra(args, "address", Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_MAC"));
        AddStringExtra(args, "name", Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_NAME"));

        var started = await adb.RunAsync(TimeSpan.FromSeconds(30), ct, args.ToArray());
        started.EnsureSuccess();
    }

    private static async Task<string> ResolveLaunchActivityAsync(AdbRunner adb, CancellationToken ct)
    {
        var result = await adb.RunAsync(
            TimeSpan.FromSeconds(10),
            ct,
            "shell",
            "cmd",
            "package",
            "resolve-activity",
            "--brief",
            "-a",
            "android.intent.action.MAIN",
            "-c",
            "android.intent.category.LAUNCHER",
            AndroidPackage);

        var component = result.Stdout
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .LastOrDefault(line =>
                line.Contains('/', StringComparison.Ordinal)
                && line.StartsWith($"{AndroidPackage}/", StringComparison.Ordinal));

        if (component is null)
            throw new InvalidOperationException($"Could not resolve launch activity for {AndroidPackage}:{Environment.NewLine}{result.Stdout}");

        return component;
    }

    private static async Task<ProbeCompletion> WaitForProbeCompletionAsync(
        AdbRunner adb,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        string lastLog = string.Empty;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var log = await adb.TryRunAsync(
                TimeSpan.FromSeconds(10),
                ct,
                "logcat",
                "-d",
                "-v",
                "brief",
                "-s",
                "BodyCamHeyCyanProbe:I",
                "*:S");

            if (log.ExitCode == 0)
            {
                lastLog = log.Stdout;
                var completion = ProbeCompletion.TryParse(lastLog);
                if (completion is not null)
                    return completion;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        throw new TimeoutException(
            $"HeyCyan Android probe did not finish within {timeout}. Last log tail:{Environment.NewLine}{Tail(lastLog, 80)}");
    }

    private static void AssertProbeResult(ProbeResult result)
    {
        result.Success.Should().BeTrue("the Android probe should complete the full photo/video C# transfer path");
        result.DeviceName.Should().NotBeNullOrWhiteSpace();
        result.DeviceAddress.Should().NotBeNullOrWhiteSpace();
        result.MediaEntries.Should().NotBeEmpty("media.config should contain at least the freshly created media");

        var expectedMac = Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_MAC");
        if (!string.IsNullOrWhiteSpace(expectedMac))
            result.DeviceAddress.Should().Be(expectedMac);

        var expectedName = Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_NAME");
        if (!string.IsNullOrWhiteSpace(expectedName))
            result.DeviceName.Should().Contain(expectedName);

        var expectedModel = Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_MODEL");
        if (!string.IsNullOrWhiteSpace(expectedModel))
            $"{result.DeviceName} {result.Version}".Should().Contain(expectedModel);

        var photo = result.Files.FirstOrDefault(f => string.Equals(f.Kind, "Photo", StringComparison.OrdinalIgnoreCase));
        photo.Should().NotBeNull("the probe records a fresh photo before transfer");
        photo!.Length.Should().BeGreaterThan(0);
        photo.IsJpeg.Should().BeTrue("downloaded photo should start with FF D8");

        var video = result.Files.FirstOrDefault(f => string.Equals(f.Kind, "Video", StringComparison.OrdinalIgnoreCase));
        video.Should().NotBeNull("the probe records a fresh video before transfer");
        video!.Length.Should().BeGreaterThan(0);
        video.IsMp4.Should().BeTrue("downloaded video should contain an ftyp MP4 signature");

        result.Steps.Should().Contain(s => s.Contains("Listing glasses media", StringComparison.OrdinalIgnoreCase));
        result.Steps.Should().Contain(s => s.Contains("Downloaded", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddStringExtra(List<string> args, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        args.Add("--es");
        args.Add(key);
        args.Add(value);
    }

    private static int ReadIntEnv(string name, int fallback)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? value
            : fallback;
    }

    private static string Tail(string text, int maxLines)
    {
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);
        return string.Join(Environment.NewLine, lines.TakeLast(maxLines));
    }

    private sealed record ProbeCompletion(bool Success, string OutputDirectory, string Log)
    {
        private static readonly Regex FinishedRegex = new(
            @"Probe finished success=(?<success>true|false) output=(?<path>\S+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static ProbeCompletion? TryParse(string log)
        {
            var match = FinishedRegex.Matches(log).Cast<Match>().LastOrDefault(m => m.Success);
            if (match is null)
                return null;

            return new ProbeCompletion(
                bool.Parse(match.Groups["success"].Value),
                match.Groups["path"].Value,
                log);
        }
    }

    private sealed class RealHardwareRun
    {
        private readonly string _artifactDirectory;

        private RealHardwareRun(string artifactDirectory, AdbRunner adb)
        {
            _artifactDirectory = artifactDirectory;
            ADB = adb;
        }

        public AdbRunner ADB { get; }
        public AdbRunner Adb => ADB;

        public static RealHardwareRun Create()
        {
            var root = FindRepoRoot();
            var baseDirectory = Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_ARTIFACT_DIR");
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                baseDirectory = Path.Combine(
                    root,
                    ".my",
                    "plan",
                    "m46-heycyan-csharp-wifi-retry",
                    "captures",
                    "phase-5-real-hardware-test-harness");
            }

            var artifactDirectory = Path.Combine(baseDirectory, DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(artifactDirectory);

            var adb = new AdbRunner(
                Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_ADB") ?? "adb",
                Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_ADB_SERIAL"));

            return new RealHardwareRun(artifactDirectory, adb);
        }

        public async Task<CommandResult> CaptureCommandAsync(
            string name,
            TimeSpan timeout,
            CancellationToken ct,
            params string[] args)
        {
            var result = await Adb.TryRunAsync(timeout, ct, args);
            await WriteCommandResultAsync(name, result, ct);
            return result;
        }

        public async Task PullProbeOutputAsync(string remoteDirectory, CancellationToken ct)
        {
            var localDirectory = Path.Combine(_artifactDirectory, "probe-output");
            var result = await Adb.TryRunAsync(
                TimeSpan.FromSeconds(45),
                ct,
                "pull",
                remoteDirectory,
                localDirectory);

            await WriteCommandResultAsync("adb-pull-probe-output", result, ct);
            result.EnsureSuccess();
        }

        public async Task<ProbeResult> ReadProbeResultAsync(CancellationToken ct)
        {
            var path = Directory
                .EnumerateFiles(_artifactDirectory, "probe-result.json", SearchOption.AllDirectories)
                .FirstOrDefault();

            path.Should().NotBeNull("the Android probe should write probe-result.json before finishing");

            var json = await File.ReadAllTextAsync(path!, ct);
            var result = JsonSerializer.Deserialize<ProbeResult>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            result.Should().NotBeNull("probe-result.json should deserialize");
            return result!;
        }

        public async Task CaptureDiagnosticsAsync(string? remoteOutputDirectory, CancellationToken ct)
        {
            await CaptureCommandAsync("adb-logcat-full", TimeSpan.FromSeconds(20), ct, "logcat", "-d", "-v", "time");
            await CaptureCommandAsync(
                "adb-logcat-heycyan",
                TimeSpan.FromSeconds(20),
                ct,
                "logcat",
                "-d",
                "-v",
                "time",
                "-s",
                "BodyCamHeyCyanProbe:V",
                "WiFiP2pHttpClient:V",
                "HeyCyanMediaTransfer:V",
                "AndroidHeyCyanGlassesSession:V",
                "*:S");
            await CaptureCommandAsync("adb-shell-ip-addr", TimeSpan.FromSeconds(10), ct, "shell", "ip", "addr");
            await CaptureCommandAsync("adb-shell-ip-route", TimeSpan.FromSeconds(10), ct, "shell", "ip", "route");
            await CaptureCommandAsync("adb-shell-dumpsys-wifi-p2p", TimeSpan.FromSeconds(20), ct, "shell", "dumpsys", "wifi", "p2p");
            await CaptureCommandAsync("adb-shell-dumpsys-connectivity", TimeSpan.FromSeconds(20), ct, "shell", "dumpsys", "connectivity");

            if (!string.IsNullOrWhiteSpace(remoteOutputDirectory))
                await File.WriteAllTextAsync(Path.Combine(_artifactDirectory, "remote-output-directory.txt"), remoteOutputDirectory, ct);
        }

        public async Task TryCaptureDiagnosticsAsync(string? remoteOutputDirectory, CancellationToken ct)
        {
            try
            {
                await CaptureDiagnosticsAsync(remoteOutputDirectory, ct);
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(_artifactDirectory, "diagnostics-capture-error.txt"),
                    ex.ToString(),
                    CancellationToken.None);
            }
        }

        public async Task WriteSummaryAsync(ProbeResult result, ProbeCompletion completion, CancellationToken ct)
        {
            var lines = new List<string>
            {
                "# HeyCyan Real Hardware Run",
                "",
                $"- Success: `{result.Success}`",
                $"- Device: `{result.DeviceName}` / `{result.DeviceAddress}`",
                $"- Version: `{result.Version}`",
                $"- Battery: `{result.Battery}`",
                $"- Remote output: `{completion.OutputDirectory}`",
                ""
            };

            lines.Add("## Files");
            foreach (var file in result.Files)
                lines.Add($"- `{file.Kind}` `{file.Name}` `{file.Length}` bytes `{file.FirstBytesHex}`");

            lines.Add("");
            lines.Add("## Steps");
            lines.AddRange(result.Steps.Select(step => $"- {step}"));

            await File.WriteAllLinesAsync(Path.Combine(_artifactDirectory, "run-summary.md"), lines, ct);
        }

        private async Task WriteCommandResultAsync(string name, CommandResult result, CancellationToken ct)
        {
            var path = Path.Combine(_artifactDirectory, $"{name}.txt");
            await File.WriteAllTextAsync(path, result.ToReport(), ct);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            return Directory.GetCurrentDirectory();
        }
    }

    private sealed class AdbRunner
    {
        private readonly string _adbPath;
        private readonly string? _serial;

        public AdbRunner(string adbPath, string? serial)
        {
            _adbPath = adbPath;
            _serial = string.IsNullOrWhiteSpace(serial) ? null : serial;
        }

        public async Task<CommandResult> RunAsync(TimeSpan timeout, CancellationToken ct, params string[] args)
        {
            var result = await TryRunAsync(timeout, ct, args);
            result.EnsureSuccess();
            return result;
        }

        public async Task<CommandResult> TryRunAsync(TimeSpan timeout, CancellationToken ct, params string[] args)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            var psi = new ProcessStartInfo
            {
                FileName = _adbPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (_serial is not null)
            {
                psi.ArgumentList.Add("-s");
                psi.ArgumentList.Add(_serial);
            }

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = new Process { StartInfo = psi };
            var startedAt = DateTimeOffset.Now;

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TryKill(process);
                throw new TimeoutException($"Command timed out after {timeout}: {BuildCommandLine(args)}");
            }

            return new CommandResult(
                BuildCommandLine(args),
                process.ExitCode,
                await stdoutTask,
                await stderrTask,
                startedAt,
                DateTimeOffset.Now);
        }

        private string BuildCommandLine(IReadOnlyList<string> args)
        {
            var allArgs = _serial is null
                ? args
                : new[] { "-s", _serial }.Concat(args).ToArray();

            return $"{_adbPath} {string.Join(" ", allArgs.Select(QuoteIfNeeded))}";
        }

        private static string QuoteIfNeeded(string value) =>
            value.Contains(' ') ? $"\"{value}\"" : value;

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    private sealed record CommandResult(
        string CommandLine,
        int ExitCode,
        string Stdout,
        string Stderr,
        DateTimeOffset StartedAt,
        DateTimeOffset FinishedAt)
    {
        public void EnsureSuccess()
        {
            if (ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{CommandLine} exited with {ExitCode}.{Environment.NewLine}{Tail(Stderr + Stdout, 80)}");
            }
        }

        public string ToReport() =>
            $"""
            Command: {CommandLine}
            ExitCode: {ExitCode}
            StartedAt: {StartedAt:O}
            FinishedAt: {FinishedAt:O}

            STDOUT
            ------
            {Stdout}

            STDERR
            ------
            {Stderr}
            """;
    }

    private sealed class ProbeResult
    {
        public bool Success { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset FinishedAt { get; set; }
        public string? OutputDirectory { get; set; }
        public string? DeviceName { get; set; }
        public string? DeviceAddress { get; set; }
        public string? Version { get; set; }
        public string? Battery { get; set; }
        public string? Error { get; set; }
        public List<string> ScanResults { get; set; } = [];
        public List<string> MediaEntries { get; set; } = [];
        public List<ProbeMediaFile> Files { get; set; } = [];
        public List<string> Steps { get; set; } = [];
    }

    private sealed class ProbeMediaFile
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
        public string? Path { get; set; }
        public string? AliasPath { get; set; }
        public long Length { get; set; }
        public string? FirstBytesHex { get; set; }
        public bool IsJpeg { get; set; }
        public bool IsMp4 { get; set; }
    }
}
