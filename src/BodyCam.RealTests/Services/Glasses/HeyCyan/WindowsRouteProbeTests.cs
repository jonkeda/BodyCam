using System.Diagnostics;
using System.Text.Json;
using BodyCam.RealTests.Fixtures;
using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Services.Glasses.HeyCyan;

[Trait("Category", "RealWindowsRouteProbe")]
[Collection("HeyCyanWiFiTransfer")]
public sealed class WindowsRouteProbeTests
{
    private readonly ITestOutputHelper _output;
    private readonly SharedHeyCyanWiFiFixture _shared;

    private static bool RealEnabled =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN") == "1";

    public WindowsRouteProbeTests(SharedHeyCyanWiFiFixture shared, ITestOutputHelper output)
    {
        _shared = shared;
        _output = output;
    }

    [SkippableFact]
    public async Task Windows_route_probe_writes_transfer_artifacts()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");
        Skip.If(_shared.Inner!.Transfer is null, "Transfer not initialized");

        var fixture = _shared.Inner!;
        var runDirectory = CreateRunDirectory();
        var report = new WindowsRouteProbeReport
        {
            StartedAt = DateTimeOffset.UtcNow,
            DeviceMac = Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_MAC"),
            CaptureFreshMedia = ReadBoolEnv("BODYCAM_REAL_HEYCYAN_WINDOWS_CAPTURE_FRESH", defaultValue: true),
            ArtifactDirectory = runDirectory
        };

        Exception? failure = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));

        try
        {
            if (report.CaptureFreshMedia)
                await CaptureFreshMediaAsync(fixture, report, cts.Token).ConfigureAwait(false);

            var sw = Stopwatch.StartNew();
            var entries = await fixture.Transfer!.ListAsync(cts.Token).ConfigureAwait(false);
            sw.Stop();

            report.TransferSucceeded = true;
            report.ListDurationMs = sw.ElapsedMilliseconds;
            report.MediaEntries = entries
                .Select(entry => new MediaEntryProbe(
                    entry.Name,
                    entry.Size,
                    entry.Timestamp,
                    entry.Kind.ToString()))
                .ToArray();

            var photo = entries
                .Where(entry => entry.Kind == HeyCyanMediaKind.Photo)
                .OrderByDescending(entry => entry.Timestamp)
                .ThenByDescending(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (photo is not null)
                report.Photo = await DownloadArtifactAsync(fixture.Transfer, photo, runDirectory, cts.Token)
                    .ConfigureAwait(false);

            var video = entries
                .Where(entry => entry.Kind == HeyCyanMediaKind.Video)
                .OrderByDescending(entry => entry.Timestamp)
                .ThenByDescending(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (video is not null)
                report.Video = await DownloadArtifactAsync(fixture.Transfer, video, runDirectory, cts.Token)
                    .ConfigureAwait(false);

            if (report.Photo is not null)
                report.Photo.LooksValid.Should().BeTrue("downloaded photo should look like a JPEG");
            if (report.Video is not null)
                report.Video.LooksValid.Should().BeTrue("downloaded video should look like an MP4");
        }
        catch (Exception ex)
        {
            failure = ex;
            report.TransferSucceeded = false;
            report.Error = ex.ToString();
        }
        finally
        {
            report.CompletedAt = DateTimeOffset.UtcNow;
            report.LastTransferCandidateIps = fixture.Session.DiagnosticLastTransferEndpointCandidates
                .Select(ip => ip.ToString())
                .ToArray();
            report.ValidatedTransferIp = fixture.Session.DiagnosticLastValidatedTransferIp?.ToString();
            report.WiFiDirect = BuildWiFiDirectReport(fixture);

            await WriteReportAsync(runDirectory, report, CancellationToken.None).ConfigureAwait(false);
            _output.WriteLine($"Windows route probe artifacts: {runDirectory}");

            try { await fixture.Transfer!.ExitAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { _output.WriteLine($"Cleanup ExitAsync failed: {ex.Message}"); }
        }

        if (failure is not null)
            throw new InvalidOperationException(
                $"Windows route probe failed; artifacts were written to {runDirectory}",
                failure);

        report.TransferSucceeded.Should().BeTrue();
        report.ValidatedTransferIp.Should().NotBeNullOrWhiteSpace();
    }

    private async Task CaptureFreshMediaAsync(
        WindowsHeyCyanRealFixture fixture,
        WindowsRouteProbeReport report,
        CancellationToken ct)
    {
        var videoSeconds = ReadIntEnv("BODYCAM_REAL_HEYCYAN_WINDOWS_VIDEO_SECONDS", defaultValue: 4);

        _output.WriteLine("Preparing fresh Windows probe media...");
        await fixture.Transfer!.ExitAsync(ct).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);

        var photoSw = Stopwatch.StartNew();
        await fixture.Session.TakePhotoAsync(ct).ConfigureAwait(false);
        photoSw.Stop();
        report.PhotoCaptureCommandMs = photoSw.ElapsedMilliseconds;
        await Task.Delay(TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);

        var videoSw = Stopwatch.StartNew();
        await fixture.Session.StartVideoAsync(ct).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(videoSeconds), ct).ConfigureAwait(false);
        await fixture.Session.StopVideoAsync(ct).ConfigureAwait(false);
        videoSw.Stop();
        report.VideoCaptureCommandMs = videoSw.ElapsedMilliseconds;
        await Task.Delay(TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
    }

    private static async Task<DownloadedArtifactProbe> DownloadArtifactAsync(
        IHeyCyanMediaTransfer transfer,
        HeyCyanMediaEntry entry,
        string runDirectory,
        CancellationToken ct)
    {
        var safeName = Path.GetFileName(entry.Name);
        var outputPath = Path.Combine(runDirectory, safeName);

        var sw = Stopwatch.StartNew();
        await using (var input = await transfer.OpenAsync(entry.Name, ct).ConfigureAwait(false))
        await using (var output = File.Create(outputPath))
        {
            await input.CopyToAsync(output, ct).ConfigureAwait(false);
        }
        sw.Stop();

        var fileInfo = new FileInfo(outputPath);
        var header = await ReadHeaderHexAsync(outputPath, ct).ConfigureAwait(false);
        var looksValid = entry.Kind switch
        {
            HeyCyanMediaKind.Photo => header.StartsWith("FFD8", StringComparison.OrdinalIgnoreCase),
            HeyCyanMediaKind.Video => header.Length >= 16
                && header.Substring(8, 8).Equals("66747970", StringComparison.OrdinalIgnoreCase),
            _ => true
        };

        return new DownloadedArtifactProbe(
            entry.Name,
            outputPath,
            fileInfo.Length,
            sw.ElapsedMilliseconds,
            header,
            looksValid);
    }

    private static WindowsWiFiDirectProbe BuildWiFiDirectReport(WindowsHeyCyanRealFixture fixture)
    {
        var manager = fixture.WifiDirectManager;
        if (manager is null)
            return new WindowsWiFiDirectProbe(null, null, null, [], []);

        return new WindowsWiFiDirectProbe(
            manager.RemoteIp,
            manager.MatchedPeerName,
            manager.MatchedPeerId,
            manager.ConnectionEndpointPairs
                .Select(pair => new EndpointPairProbe(
                    pair.LocalHost,
                    pair.LocalService,
                    pair.RemoteHost,
                    pair.RemoteService))
                .ToArray(),
            manager.DiscoveryEvents.ToArray());
    }

    private static async Task<string> ReadHeaderHexAsync(string path, CancellationToken ct)
    {
        var header = new byte[16];
        await using var stream = File.OpenRead(path);
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), ct).ConfigureAwait(false);
        return Convert.ToHexString(header.AsSpan(0, read));
    }

    private static async Task WriteReportAsync(
        string runDirectory,
        WindowsRouteProbeReport report,
        CancellationToken ct)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        var path = Path.Combine(runDirectory, "windows-route-probe-result.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, options), ct)
            .ConfigureAwait(false);
    }

    private static string CreateRunDirectory()
    {
        var root = Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_WINDOWS_ARTIFACT_DIR");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                ".my", "plan", "m46-heycyan-csharp-wifi-retry", "captures",
                "phase-7c-windows-route-probe"));
        }

        var runDirectory = Path.Combine(root, DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(runDirectory);
        return runDirectory;
    }

    private static bool ReadBoolEnv(string name, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadIntEnv(string name, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : defaultValue;
    }

    private sealed class WindowsRouteProbeReport
    {
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string? DeviceMac { get; set; }
        public bool CaptureFreshMedia { get; set; }
        public string ArtifactDirectory { get; set; } = "";
        public bool TransferSucceeded { get; set; }
        public long? PhotoCaptureCommandMs { get; set; }
        public long? VideoCaptureCommandMs { get; set; }
        public long? ListDurationMs { get; set; }
        public IReadOnlyList<string> LastTransferCandidateIps { get; set; } = [];
        public string? ValidatedTransferIp { get; set; }
        public WindowsWiFiDirectProbe? WiFiDirect { get; set; }
        public IReadOnlyList<MediaEntryProbe> MediaEntries { get; set; } = [];
        public DownloadedArtifactProbe? Photo { get; set; }
        public DownloadedArtifactProbe? Video { get; set; }
        public string? Error { get; set; }
    }

    private sealed record WindowsWiFiDirectProbe(
        string? RemoteIp,
        string? MatchedPeerName,
        string? MatchedPeerId,
        IReadOnlyList<EndpointPairProbe> EndpointPairs,
        IReadOnlyList<string> DiscoveryEvents);

    private sealed record EndpointPairProbe(
        string? LocalHost,
        string? LocalService,
        string? RemoteHost,
        string? RemoteService);

    private sealed record MediaEntryProbe(
        string Name,
        long Size,
        DateTimeOffset Timestamp,
        string Kind);

    private sealed record DownloadedArtifactProbe(
        string Name,
        string Path,
        long Size,
        long DurationMs,
        string HeaderHex,
        bool LooksValid);
}
