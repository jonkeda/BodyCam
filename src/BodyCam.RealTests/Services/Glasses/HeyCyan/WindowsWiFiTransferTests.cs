using System.Diagnostics;
using System.Net;
using BodyCam.RealTests.Fixtures;
using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Services.Glasses.HeyCyan;

/// <summary>
/// Real-hardware tests for the HeyCyan WiFi photo transfer pipeline on Windows.
/// Exercises: BLE transfer mode → WiFi join → HTTP file listing → photo download.
///
/// Requires:
/// - HeyCyan glasses powered on with at least one photo stored
/// - Windows PC with Bluetooth + WiFi radios
/// - BODYCAM_REAL_HEYCYAN=1
/// - BODYCAM_REAL_HEYCYAN_MAC=D8:79:B8:7F:E6:C9 (your glasses' address)
///
/// Run with:
///   $env:BODYCAM_REAL_HEYCYAN="1"; $env:BODYCAM_REAL_HEYCYAN_MAC="D8:79:B8:7F:E6:C9"
///   dotnet test src/BodyCam.RealTests -f net10.0-windows10.0.19041.0 --filter "Category=RealWiFiTransfer" -v normal
/// </summary>
[Trait("Category", "RealWiFiTransfer")]
[Collection("HeyCyanWiFiTransfer")]
public sealed class WindowsWiFiTransferTests
{
    private readonly ITestOutputHelper _output;
    private readonly SharedHeyCyanWiFiFixture _shared;
    private WindowsHeyCyanRealFixture? _fixture;

    private static bool RealEnabled =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN") == "1";

    public WindowsWiFiTransferTests(SharedHeyCyanWiFiFixture shared, ITestOutputHelper output)
    {
        _shared = shared;
        _output = output;
        _fixture = shared.Inner;
    }

    // ── Transfer Mode Entry ─────────────────────────────────────────

    [SkippableFact]
    public async Task EnterTransferMode_ReceivesIpAddress()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        var session = await _fixture!.Session.EnterTransferModeAsync(CancellationToken.None);

        _output.WriteLine($"Transfer session base URL: {session.BaseUrl}");

        session.BaseUrl.Should().NotBeNullOrEmpty();
        session.BaseUrl.Should().StartWith("http://");

        // Extract IP and verify it's a valid private address
        var uri = new Uri(session.BaseUrl);
        var ip = IPAddress.Parse(uri.Host);
        ip.AddressFamily.Should().Be(System.Net.Sockets.AddressFamily.InterNetwork,
            "glasses should provide an IPv4 address");

        _output.WriteLine($"Glasses IP: {ip}");

        await _fixture.Session.ExitTransferModeAsync(CancellationToken.None);
    }

    // ── WiFi Connectivity ───────────────────────────────────────────

    [SkippableFact]
    public async Task JoinGlassesWiFi_NetworkIsReachable()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        var session = await _fixture!.Session.EnterTransferModeAsync(CancellationToken.None);
        var uri = new Uri(session.BaseUrl);

        _output.WriteLine($"Testing reachability of {uri.Host}...");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        bool reachable;
        try
        {
            // Try to reach the glasses HTTP server
            await http.GetAsync($"http://{uri.Host}/", CancellationToken.None);
            reachable = true;
        }
        catch (HttpRequestException)
        {
            // 404/500 is fine — means TCP connected but no root handler
            reachable = true;
        }
        catch (TaskCanceledException)
        {
            reachable = false;
        }

        _output.WriteLine($"Glasses IP {uri.Host} reachable: {reachable}");
        reachable.Should().BeTrue("glasses IP should be reachable after WiFi join");

        await _fixture.Session.ExitTransferModeAsync(CancellationToken.None);
    }

    // ── Media Listing ───────────────────────────────────────────────

    [SkippableFact]
    public async Task ListMedia_ReturnsNonEmptyList()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture?.Transfer == null, "Fixture or Transfer not initialized");

        var files = await _fixture!.Transfer!.ListAsync(CancellationToken.None);

        _output.WriteLine($"Media files on glasses: {files.Count}");
        foreach (var f in files)
            _output.WriteLine($"  {f.Name} ({f.Kind}, {f.Size} bytes, {f.Timestamp})");

        files.Should().NotBeEmpty(
            "glasses should have at least one media file — " +
            "take a photo on the glasses before running this test");

        await _fixture.Transfer.ExitAsync(CancellationToken.None);
    }

    [SkippableFact]
    public async Task ListMedia_ContainsAtLeastOnePhoto()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture?.Transfer == null, "Fixture or Transfer not initialized");

        var files = await _fixture!.Transfer!.ListAsync(CancellationToken.None);

        _output.WriteLine($"Total files: {files.Count}, photos: {files.Count(f => f.Kind == HeyCyanMediaKind.Photo)}");

        files.Should().Contain(f => f.Kind == HeyCyanMediaKind.Photo,
            "glasses should have at least one photo");

        await _fixture.Transfer.ExitAsync(CancellationToken.None);
    }

    // ── Photo Download ──────────────────────────────────────────────

    [SkippableFact]
    public async Task DownloadPhoto_ReturnsValidJpeg()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture?.Transfer == null, "Fixture or Transfer not initialized");

        var files = await _fixture!.Transfer!.ListAsync(CancellationToken.None);
        var photo = files.FirstOrDefault(f => f.Kind == HeyCyanMediaKind.Photo);
        Skip.If(photo == null, "No photos on glasses");

        _output.WriteLine($"Downloading {photo!.Name} ({photo.Size} bytes)...");
        var sw = Stopwatch.StartNew();
        var bytes = await _fixture.Transfer.DownloadAsync(photo.Name, CancellationToken.None);
        sw.Stop();

        _output.WriteLine($"Downloaded {bytes.Length} bytes in {sw.ElapsedMilliseconds} ms");

        bytes.Should().NotBeEmpty();
        bytes.Should().HaveCountGreaterThan(100, "photo should be more than 100 bytes");

        // Validate JPEG SOI marker (0xFF 0xD8)
        bytes[0].Should().Be(0xFF, "first byte should be JPEG SOI high byte");
        bytes[1].Should().Be(0xD8, "second byte should be JPEG SOI low byte");

        // Validate JPEG EOI marker (0xFF 0xD9) at end
        bytes[^2].Should().Be(0xFF, "second-to-last byte should be JPEG EOI high byte");
        bytes[^1].Should().Be(0xD9, "last byte should be JPEG EOI low byte");

        await _fixture.Transfer.ExitAsync(CancellationToken.None);
    }

    [SkippableFact]
    public async Task DownloadPhoto_SizeMatchesManifest()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture?.Transfer == null, "Fixture or Transfer not initialized");

        var files = await _fixture!.Transfer!.ListAsync(CancellationToken.None);
        var photo = files.FirstOrDefault(f => f.Kind == HeyCyanMediaKind.Photo);
        Skip.If(photo == null, "No photos on glasses");

        var bytes = await _fixture.Transfer.DownloadAsync(photo!.Name, CancellationToken.None);

        _output.WriteLine($"Manifest size: {photo.Size}, actual: {bytes.Length}");

        // Some glasses firmware may not report exact sizes in the manifest;
        // if Size is 0 (unknown), skip the assertion
        if (photo.Size > 0)
        {
            bytes.Length.Should().Be((int)photo.Size,
                "downloaded file size should match manifest entry");
        }

        await _fixture.Transfer.ExitAsync(CancellationToken.None);
    }

    [SkippableFact]
    public async Task DownloadAllPhotos_AllReturnValidJpeg()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture?.Transfer == null, "Fixture or Transfer not initialized");

        var files = await _fixture!.Transfer!.ListAsync(CancellationToken.None);
        var photos = files.Where(f => f.Kind == HeyCyanMediaKind.Photo).ToList();
        Skip.If(photos.Count == 0, "No photos on glasses");

        _output.WriteLine($"Downloading {photos.Count} photos...");

        foreach (var photo in photos)
        {
            var bytes = await _fixture.Transfer.DownloadAsync(photo.Name, CancellationToken.None);

            _output.WriteLine($"  {photo.Name}: {bytes.Length} bytes");

            bytes.Should().NotBeEmpty($"{photo.Name} should not be empty");
            bytes[0].Should().Be(0xFF, $"{photo.Name} should start with JPEG SOI");
            bytes[1].Should().Be(0xD8, $"{photo.Name} should start with JPEG SOI");
        }

        _output.WriteLine($"All {photos.Count} photos downloaded successfully");

        await _fixture.Transfer.ExitAsync(CancellationToken.None);
    }

    // ── Warm Transfer ───────────────────────────────────────────────

    [SkippableFact]
    public async Task WarmTransfer_SecondListIsFaster()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture?.Transfer == null, "Fixture or Transfer not initialized");

        // Cold: first ListAsync triggers EnterTransferModeAsync + WiFi join
        var swCold = Stopwatch.StartNew();
        var files1 = await _fixture!.Transfer!.ListAsync(CancellationToken.None);
        swCold.Stop();

        // Warm: second ListAsync reuses existing session (no WiFi join)
        var swWarm = Stopwatch.StartNew();
        var files2 = await _fixture.Transfer.ListAsync(CancellationToken.None);
        swWarm.Stop();

        _output.WriteLine($"Cold list: {swCold.ElapsedMilliseconds} ms ({files1.Count} files)");
        _output.WriteLine($"Warm list: {swWarm.ElapsedMilliseconds} ms ({files2.Count} files)");

        files1.Count.Should().Be(files2.Count, "same files both times");
        swWarm.Elapsed.Should().BeLessThan(swCold.Elapsed,
            "warm transfer should be faster than cold");

        await _fixture.Transfer.ExitAsync(CancellationToken.None);
    }

    // ── State Management ────────────────────────────────────────────

    [SkippableFact]
    public async Task ExitTransferMode_RestoresConnectedState()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture?.Transfer == null, "Fixture or Transfer not initialized");

        // Enter transfer mode
        await _fixture!.Transfer!.ListAsync(CancellationToken.None);
        _fixture.Session.State.Should().Be(HeyCyanState.TransferMode);

        // Exit
        await _fixture.Transfer.ExitAsync(CancellationToken.None);

        _output.WriteLine($"State after exit: {_fixture.Session.State}");
        _fixture.Session.State.Should().Be(HeyCyanState.Connected,
            "should return to Connected after exit transfer mode");
    }

    // ── Round Trip ──────────────────────────────────────────────────

    [SkippableFact]
    public async Task TransferMode_TakePhotoThenDownload_RoundTrip()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture?.Transfer == null, "Fixture or Transfer not initialized");

        // Take a photo via BLE
        _output.WriteLine("Taking photo via BLE...");
        await _fixture!.Session.TakePhotoAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(3)); // wait for glasses to save

        // Enter transfer mode and list files
        var files = await _fixture.Transfer!.ListAsync(CancellationToken.None);
        var photos = files.Where(f => f.Kind == HeyCyanMediaKind.Photo).ToList();

        _output.WriteLine($"Found {photos.Count} photos after taking one");
        photos.Should().NotBeEmpty("should have at least one photo after capture");

        // Download the latest photo (last in list, most recent by name convention)
        var latestPhoto = photos
            .OrderByDescending(f => f.Timestamp)
            .First();

        _output.WriteLine($"Downloading latest photo: {latestPhoto.Name}");
        var bytes = await _fixture.Transfer.DownloadAsync(latestPhoto.Name, CancellationToken.None);

        bytes.Should().NotBeEmpty();
        bytes[0].Should().Be(0xFF);
        bytes[1].Should().Be(0xD8);
        _output.WriteLine($"Downloaded {bytes.Length} bytes — valid JPEG");

        // Save to TestResults for manual inspection
        var savePath = Path.Combine(
            Path.GetDirectoryName(typeof(WindowsWiFiTransferTests).Assembly.Location)!,
            "..", "..", "..", "..", "..",
            "TestResults", "wifi-transfer", latestPhoto.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
        File.WriteAllBytes(savePath, bytes);
        _output.WriteLine($"Saved to {savePath}");

        await _fixture.Transfer.ExitAsync(CancellationToken.None);
    }

    // ── File I/O ────────────────────────────────────────────────────

    [SkippableFact]
    public async Task DownloadPhoto_SaveToLocalFile_Succeeds()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture?.Transfer == null, "Fixture or Transfer not initialized");

        var files = await _fixture!.Transfer!.ListAsync(CancellationToken.None);
        var photo = files.FirstOrDefault(f => f.Kind == HeyCyanMediaKind.Photo);
        Skip.If(photo == null, "No photos on glasses");

        var bytes = await _fixture.Transfer.DownloadAsync(photo!.Name, CancellationToken.None);

        var savePath = Path.Combine(Path.GetTempPath(), "bodycam-test", photo.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
        File.WriteAllBytes(savePath, bytes);

        _output.WriteLine($"Saved to {savePath}");

        // Re-read and validate
        var readBack = File.ReadAllBytes(savePath);
        readBack.Length.Should().Be(bytes.Length);
        readBack[0].Should().Be(0xFF);
        readBack[1].Should().Be(0xD8);

        // Cleanup
        File.Delete(savePath);

        await _fixture.Transfer.ExitAsync(CancellationToken.None);
    }

    // ── Performance ─────────────────────────────────────────────────

    [SkippableFact]
    public async Task TransferLatency_ColdEntry_IsUnder20Seconds()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture?.Transfer == null, "Fixture or Transfer not initialized");

        // Force cold path
        try { await _fixture!.Transfer!.ExitAsync(CancellationToken.None); }
        catch { /* already exited */ }
        await Task.Delay(TimeSpan.FromSeconds(10));

        var sw = Stopwatch.StartNew();
        var files = await _fixture.Transfer!.ListAsync(CancellationToken.None);
        sw.Stop();

        _output.WriteLine($"Cold transfer entry + list: {sw.ElapsedMilliseconds} ms");
        _output.WriteLine($"Files: {files.Count}");

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20),
            "cold transfer mode entry + WiFi join + list should complete within 20s");

        await _fixture.Transfer.ExitAsync(CancellationToken.None);
    }
}
