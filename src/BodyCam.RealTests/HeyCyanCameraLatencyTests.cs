using BodyCam.RealTests.Fixtures;
using FluentAssertions;
using System.Diagnostics;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests;

/// <summary>
/// Real-hardware latency benchmarks for HeyCyan camera capture pipeline.
/// Requires BODYCAM_REAL_HEYCYAN=1 and BODYCAM_REAL_HEYCYAN_MAC=XX:XX:XX:XX:XX:XX env vars.
/// </summary>
public class HeyCyanCameraLatencyTests
{
    private readonly ITestOutputHelper _output;

    private static bool RealEnabled =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN") == "1";

    public HeyCyanCameraLatencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact, Trait("Category", "RealHardware")]
    public async Task CaptureFrameAsync_ColdLatency_IsUnderSixSeconds()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set to 1");

        await using var fixture = await HeyCyanRealFixture.ConnectAsync();
        
        // Force COLD path by exiting transfer mode and waiting for group teardown
        await fixture.Transfer.ExitAsync(default);
        _output.WriteLine("Waiting 15s for P2P group teardown...");
        await Task.Delay(TimeSpan.FromSeconds(15));

        var sw = Stopwatch.StartNew();
        var jpg = await fixture.Camera.CaptureFrameAsync(default);
        sw.Stop();

        jpg.Should().NotBeNull();
        jpg.Should().StartWith(new byte[] { 0xFF, 0xD8 }, "should be valid JPEG (SOI marker)");
        
        _output.WriteLine($"Cold capture latency: {sw.ElapsedMilliseconds} ms");
        
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(6),
            "cold capture must complete within 6s per M33 latency contract");
    }

    [SkippableFact, Trait("Category", "RealHardware")]
    public async Task CaptureFrameAsync_WarmLatency_IsUnderTwoSeconds()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set to 1");

        await using var fixture = await HeyCyanRealFixture.ConnectAsync();

        // Prime the warm session with one capture
        _output.WriteLine("Priming warm session with first capture...");
        var prime = await fixture.Camera.CaptureFrameAsync(default);
        prime.Should().NotBeNull();
        prime.Should().StartWith(new byte[] { 0xFF, 0xD8 });

        // Measure the next capture (should be warm)
        _output.WriteLine("Measuring warm capture latency...");
        var sw = Stopwatch.StartNew();
        var jpg = await fixture.Camera.CaptureFrameAsync(default);
        sw.Stop();

        jpg.Should().NotBeNull();
        jpg.Should().StartWith(new byte[] { 0xFF, 0xD8 });
        
        _output.WriteLine($"Warm capture latency: {sw.ElapsedMilliseconds} ms");
        
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "warm capture must complete within 2s per M33 latency contract");
    }

    [SkippableFact, Trait("Category", "RealHardware")]
    public async Task CaptureFrameAsync_LatencyDistribution_RecordedToCsv()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set to 1");

        const int coldSamples = 10;
        const int warmSamples = 10;
        var results = new List<(int iteration, string mode, long ms, int jpgBytes)>();

        await using var fixture = await HeyCyanRealFixture.ConnectAsync();

        // Get firmware version for the report
        var version = await fixture.Session.GetVersionAsync(default);
        _output.WriteLine($"Glasses firmware: {version.Firmware}, hardware: {version.Hardware}");

        // Cold captures
        _output.WriteLine($"Running {coldSamples} cold captures...");
        for (int i = 0; i < coldSamples; i++)
        {
            // Force cold path
            await fixture.Transfer.ExitAsync(default);
            await Task.Delay(TimeSpan.FromSeconds(15));

            var sw = Stopwatch.StartNew();
            var jpg = await fixture.Camera.CaptureFrameAsync(default);
            sw.Stop();

            jpg.Should().NotBeNull();
            jpg.Should().StartWith(new byte[] { 0xFF, 0xD8 });

            results.Add((i + 1, "cold", sw.ElapsedMilliseconds, jpg!.Length));
            _output.WriteLine($"  Cold #{i + 1}: {sw.ElapsedMilliseconds} ms, {jpg.Length} bytes");
        }

        // Warm captures
        _output.WriteLine($"Running {warmSamples} warm captures...");
        
        // Prime warm session
        await fixture.Camera.CaptureFrameAsync(default);

        for (int i = 0; i < warmSamples; i++)
        {
            var sw = Stopwatch.StartNew();
            var jpg = await fixture.Camera.CaptureFrameAsync(default);
            sw.Stop();

            jpg.Should().NotBeNull();
            jpg.Should().StartWith(new byte[] { 0xFF, 0xD8 });

            results.Add((i + 1, "warm", sw.ElapsedMilliseconds, jpg!.Length));
            _output.WriteLine($"  Warm #{i + 1}: {sw.ElapsedMilliseconds} ms, {jpg.Length} bytes");

            // Small delay to stay within warm window
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        // Compute percentiles
        var coldLatencies = results.Where(r => r.mode == "cold").Select(r => r.ms).OrderBy(x => x).ToList();
        var warmLatencies = results.Where(r => r.mode == "warm").Select(r => r.ms).OrderBy(x => x).ToList();

        var coldP50 = Percentile(coldLatencies, 0.5);
        var coldP95 = Percentile(coldLatencies, 0.95);
        var warmP50 = Percentile(warmLatencies, 0.5);
        var warmP95 = Percentile(warmLatencies, 0.95);

        _output.WriteLine("");
        _output.WriteLine("=== Latency Percentiles ===");
        _output.WriteLine($"Cold p50: {coldP50} ms, p95: {coldP95} ms");
        _output.WriteLine($"Warm p50: {warmP50} ms, p95: {warmP95} ms");

        // Write CSV
        var csvPath = Path.Combine(
            Path.GetDirectoryName(typeof(HeyCyanCameraLatencyTests).Assembly.Location)!,
            "..", "..", "..", "..", "..", // Navigate up from bin/Debug/net10.0-*
            "TestResults",
            "heycyan-latency.csv");

        Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);

        var csv = new StringBuilder();
        csv.AppendLine($"# HeyCyan Latency Benchmark - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        csv.AppendLine($"# Firmware: {version.Firmware}, Hardware: {version.Hardware}");
        csv.AppendLine($"# Cold p50: {coldP50} ms, p95: {coldP95} ms");
        csv.AppendLine($"# Warm p50: {warmP50} ms, p95: {warmP95} ms");
        csv.AppendLine("iteration,mode,ms,jpg_bytes");

        foreach (var (iteration, mode, ms, jpgBytes) in results)
        {
            csv.AppendLine($"{iteration},{mode},{ms},{jpgBytes}");
        }

        File.WriteAllText(csvPath, csv.ToString());
        _output.WriteLine($"Wrote results to {csvPath}");

        // Assert percentile thresholds
        coldP95.Should().BeLessThanOrEqualTo(6000, "cold p95 must be <= 6000 ms");
        warmP95.Should().BeLessThanOrEqualTo(2000, "warm p95 must be <= 2000 ms");
    }

    private static long Percentile(List<long> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        var index = (int)Math.Ceiling(sorted.Count * p) - 1;
        return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
    }
}
