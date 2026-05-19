using System.Diagnostics;
using BodyCam.RealTests.Fixtures;
using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Services.Glasses.HeyCyan;

/// <summary>
/// End-to-end test: take photo on glasses → enter transfer mode → download → save to disk.
/// Validates the full capture pipeline including the RCA-810 channel fix.
///
/// Run with:
///   $env:BODYCAM_REAL_HEYCYAN="1"; $env:BODYCAM_REAL_HEYCYAN_MAC="D8:79:B8:7F:E6:C9"
///   dotnet test src/BodyCam.RealTests -f net10.0-windows10.0.19041.0 --filter "FullyQualifiedName~CaptureAndDownloadTests" -v normal --logger "console;verbosity=detailed"
/// </summary>
[Trait("Category", "RealWiFiTransfer")]
[Collection("HeyCyanWiFiTransfer")]
public sealed class CaptureAndDownloadTests
{
    private readonly ITestOutputHelper _output;
    private readonly SharedHeyCyanWiFiFixture _shared;
    private WindowsHeyCyanRealFixture? _fixture;

    private static bool RealEnabled =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN") == "1";

    public CaptureAndDownloadTests(SharedHeyCyanWiFiFixture shared, ITestOutputHelper output)
    {
        _shared = shared;
        _output = output;
        _fixture = shared.Inner;
    }

    [SkippableFact]
    public async Task CapturePhoto_Download_SaveToDisk()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized (glasses not connected)");
        Skip.If(_fixture!.Transfer == null, "Transfer not initialized");

        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(180)).Token;

        // 1. Exit transfer mode so the glasses can take a photo
        //    (glasses can't capture while in transfer/WiFi mode)
        _output.WriteLine("Step 1: Exiting transfer mode to allow photo capture...");
        await _fixture.Transfer!.ExitAsync(ct);
        await Task.Delay(2000, ct);

        // 2. Take a photo via BLE — enter photo mode first, then trigger capture
        _output.WriteLine("Step 2a: Entering photo mode on glasses...");
        await _fixture.Session.TakePhotoAsync(ct); // StartPhotoMode: activates camera
        _output.WriteLine("  StartPhotoMode sent, waiting 3s for camera init...");
        await Task.Delay(3000, ct);

        _output.WriteLine("Step 2b: Triggering capture (StartPhotoMode again as shutter)...");
        var sw = Stopwatch.StartNew();
        await _fixture.Session.TakePhotoAsync(ct); // Second call acts as shutter trigger
        _output.WriteLine($"  Second StartPhotoMode completed in {sw.ElapsedMilliseconds} ms");

        // Also try AI photo trigger as fallback
        _output.WriteLine("Step 2c: Also sending TakeAiPhoto as fallback...");
        await _fixture.Session.TakeAiPhotoAsync(ct);
        _output.WriteLine("  TakeAiPhoto sent");

        // 3. Wait for glasses to write the photo (firmware needs time)
        _output.WriteLine("Step 3: Waiting 5s for glasses firmware to write photo...");
        await Task.Delay(5000, ct);

        // 4. Re-enter transfer mode and list media — also dump raw media.config
        _output.WriteLine("Step 4: Re-entering transfer mode and listing media...");
        sw.Restart();
        var afterFiles = await _fixture.Transfer.ListAsync(ct);
        sw.Stop();

        // Also fetch media.config raw for diagnostics
        using var diagHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            var rawConfig = await diagHttp.GetStringAsync("http://192.168.1.1/files/media.config", ct);
            _output.WriteLine($"  Raw media.config ({rawConfig.Length} chars): [{rawConfig.Replace("\n", "\\n").Replace("\r", "\\r")}]");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"  media.config fetch error: {ex.Message}");
        }

        var afterPhotos = afterFiles.Where(f => f.Kind == HeyCyanMediaKind.Photo).ToList();
        _output.WriteLine($"  Photos found: {afterPhotos.Count} ({sw.ElapsedMilliseconds} ms to list)");
        _output.WriteLine($"  All entries: {string.Join(", ", afterFiles.Select(f => $"{f.Name}({f.Kind})"))}");

        afterPhotos.Count.Should().BeGreaterThan(0,
            "at least one photo should exist after TakePhotoAsync");

        // 5. Find the newest photo (last in the list or newest by name)
        var newestPhoto = afterPhotos
            .OrderByDescending(p => p.Timestamp)
            .ThenByDescending(p => p.Name)
            .First();
        _output.WriteLine($"  Newest photo: {newestPhoto.Name} ({newestPhoto.Size} bytes, {newestPhoto.Timestamp})");

        // 6. Download the photo
        _output.WriteLine($"Step 6: Downloading {newestPhoto.Name}...");
        sw.Restart();
        var jpegBytes = await _fixture.Transfer.DownloadAsync(newestPhoto.Name, ct);
        sw.Stop();
        _output.WriteLine($"  Downloaded {jpegBytes.Length} bytes in {sw.ElapsedMilliseconds} ms");

        // 8. Validate JPEG
        jpegBytes.Should().HaveCountGreaterThan(1000, "photo should be at least 1KB");
        jpegBytes[0].Should().Be(0xFF, "JPEG SOI high byte");
        jpegBytes[1].Should().Be(0xD8, "JPEG SOI low byte");
        jpegBytes[^2].Should().Be(0xFF, "JPEG EOI high byte");
        jpegBytes[^1].Should().Be(0xD9, "JPEG EOI low byte");

        // 9. Save to disk
        var outputDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "TestResults"));
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, "rca-811-capture.jpg");
        await File.WriteAllBytesAsync(outputPath, jpegBytes, ct);

        _output.WriteLine($"");
        _output.WriteLine($"═══════════════════════════════════════════════════");
        _output.WriteLine($"  PHOTO SAVED: {outputPath}");
        _output.WriteLine($"  Size: {jpegBytes.Length:N0} bytes");
        _output.WriteLine($"═══════════════════════════════════════════════════");

        File.Exists(outputPath).Should().BeTrue();

        // 10. Cleanup
        await _fixture.Transfer.ExitAsync(ct);
    }

    [SkippableFact]
    public async Task DownloadExistingPhoto_SaveToDisk()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized (glasses not connected)");
        Skip.If(_fixture!.Transfer == null, "Transfer not initialized");

        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(180)).Token;

        // 1. List media
        _output.WriteLine("Listing media on glasses...");
        var files = await _fixture.Transfer!.ListAsync(ct);
        var photo = files.FirstOrDefault(f => f.Kind == HeyCyanMediaKind.Photo);
        Skip.If(photo == null, "No photos on glasses — take one first");

        _output.WriteLine($"Found {files.Count(f => f.Kind == HeyCyanMediaKind.Photo)} photos");
        _output.WriteLine($"Downloading: {photo!.Name} ({photo.Size} bytes)");

        // 2. Download
        var sw = Stopwatch.StartNew();
        var jpegBytes = await _fixture.Transfer.DownloadAsync(photo.Name, ct);
        sw.Stop();
        _output.WriteLine($"Downloaded {jpegBytes.Length} bytes in {sw.ElapsedMilliseconds} ms");

        // 3. Validate
        jpegBytes[0].Should().Be(0xFF, "JPEG SOI");
        jpegBytes[1].Should().Be(0xD8, "JPEG SOI");

        // 4. Save
        var outputDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "TestResults"));
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, "rca-811-existing-photo.jpg");
        await File.WriteAllBytesAsync(outputPath, jpegBytes, ct);

        _output.WriteLine($"");
        _output.WriteLine($"═══════════════════════════════════════════════════");
        _output.WriteLine($"  PHOTO SAVED: {outputPath}");
        _output.WriteLine($"  Size: {jpegBytes.Length:N0} bytes");
        _output.WriteLine($"═══════════════════════════════════════════════════");

        await _fixture.Transfer.ExitAsync(ct);
    }
}
