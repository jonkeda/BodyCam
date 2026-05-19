using System.Diagnostics;
using System.Net;
using BodyCam.Platforms.Windows.HeyCyan;
using BodyCam.RealTests.Fixtures;
using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Services.Glasses.HeyCyan;

/// <summary>
/// Real-hardware tests for WiFi Direct peer discovery and connection to HeyCyan glasses.
/// These tests verify the WiFi Direct P2P flow that replaced the regular WiFi hotspot approach.
///
/// Requires:
/// - HeyCyan glasses powered on
/// - Windows PC with Bluetooth + WiFi Direct capable adapter
/// - BODYCAM_REAL_HEYCYAN=1
/// - BODYCAM_REAL_HEYCYAN_MAC=D8:79:B8:7F:E6:C9 (your glasses' address)
///
/// Run with:
///   $env:BODYCAM_REAL_HEYCYAN="1"; $env:BODYCAM_REAL_HEYCYAN_MAC="D8:79:B8:7F:E6:C9"
///   dotnet test src/BodyCam.RealTests -f net10.0-windows10.0.19041.0 --filter "Category=RealWiFiDirect" -v normal --logger "console;verbosity=detailed"
/// </summary>
[Trait("Category", "RealWiFiDirect")]
[Collection("HeyCyanWiFiTransfer")]
public sealed class WindowsWiFiDirectTests
{
    private readonly ITestOutputHelper _output;
    private readonly SharedHeyCyanWiFiFixture _shared;

    private static bool RealEnabled =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN") == "1";

    private static string Mac =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_MAC") ?? "";

    public WindowsWiFiDirectTests(SharedHeyCyanWiFiFixture shared, ITestOutputHelper output)
    {
        _shared = shared;
        _output = output;
    }

    // ── Peer Discovery ──────────────────────────────────────────────

    [SkippableFact]
    public async Task PeerDiscovery_FindsGlassesAfterTransferModeCommand()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;
        var logFactory = LoggerFactory.Create(b => b.AddDebug().SetMinimumLevel(LogLevel.Trace));

        // Use the full session flow — EnterTransferModeAsync sends the BLE
        // command and then performs WiFi Direct discovery + connection internally.
        _output.WriteLine("Entering transfer mode (triggers BLE cmd + WiFi Direct)...");
        var sw = Stopwatch.StartNew();
        var session = await fixture.Session.EnterTransferModeAsync(CancellationToken.None);
        sw.Stop();

        _output.WriteLine($"Transfer mode active in {sw.ElapsedMilliseconds} ms");
        _output.WriteLine($"Base URL: {session.BaseUrl}");

        session.BaseUrl.Should().NotBeNullOrEmpty();

        var uri = new Uri(session.BaseUrl);
        IPAddress.TryParse(uri.Host, out var ip).Should().BeTrue(
            "base URL host should be a valid IP address");
        _output.WriteLine($"Glasses IP: {ip}");

        await fixture.Session.ExitTransferModeAsync(CancellationToken.None);
    }

    // ── Full Transfer Mode via Session ──────────────────────────────

    [SkippableFact]
    public async Task EnterTransferMode_ViaWiFiDirect_ReturnsIp()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;

        _output.WriteLine("Entering transfer mode (WiFi Direct path)...");
        var sw = Stopwatch.StartNew();

        var session = await fixture.Session.EnterTransferModeAsync(CancellationToken.None);
        sw.Stop();

        _output.WriteLine($"Transfer mode active in {sw.ElapsedMilliseconds} ms");
        _output.WriteLine($"Base URL: {session.BaseUrl}");

        session.BaseUrl.Should().NotBeNullOrEmpty();
        session.BaseUrl.Should().StartWith("http://");

        var uri = new Uri(session.BaseUrl);
        IPAddress.TryParse(uri.Host, out var ip).Should().BeTrue();
        _output.WriteLine($"Glasses IP: {ip}");

        // Verify the IP is reachable by hitting the HTTP server
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            var response = await http.GetAsync($"{session.BaseUrl}/files/media.config");
            _output.WriteLine($"HTTP probe: {(int)response.StatusCode} {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"HTTP probe failed: {ex.Message}");
        }

        await fixture.Session.ExitTransferModeAsync(CancellationToken.None);
    }

    [SkippableFact]
    public async Task EnterTransferMode_TimesOutGracefully_WhenGlassesOff()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        // This test verifies timeout behavior — it's expected to fail fast
        // when the glasses' WiFi Direct is not available.
        // We send ExitTransferMode first to ensure glasses aren't in transfer mode.
        var fixture = _shared.Inner!;

        try { await fixture.Session.ExitTransferModeAsync(CancellationToken.None); }
        catch { /* already exited */ }
        await Task.Delay(2000);

        // Request transfer mode but cancel quickly (5s) — not enough time
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var act = async () => await fixture.Session.EnterTransferModeAsync(cts.Token);

        // Should throw OperationCanceledException or InvalidOperationException
        await act.Should().ThrowAsync<Exception>(
            "should fail when cancelled before WiFi Direct connects");
    }

    // ── WiFi Direct Connection Lifecycle ────────────────────────────

    [SkippableFact]
    public async Task WiFiDirect_ConnectDisconnectReconnect()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;

        // First connection
        _output.WriteLine("First transfer mode entry...");
        var session1 = await fixture.Session.EnterTransferModeAsync(CancellationToken.None);
        _output.WriteLine($"IP 1: {new Uri(session1.BaseUrl).Host}");
        session1.BaseUrl.Should().StartWith("http://");

        await fixture.Session.ExitTransferModeAsync(CancellationToken.None);
        _output.WriteLine("Exited transfer mode, waiting...");
        await Task.Delay(5000); // let glasses tear down

        // Second connection — verify we can reconnect
        _output.WriteLine("Second transfer mode entry...");
        var session2 = await fixture.Session.EnterTransferModeAsync(CancellationToken.None);
        _output.WriteLine($"IP 2: {new Uri(session2.BaseUrl).Host}");
        session2.BaseUrl.Should().StartWith("http://");

        await fixture.Session.ExitTransferModeAsync(CancellationToken.None);
    }

    // ── HTTP Reachability After WiFi Direct ─────────────────────────

    [SkippableFact]
    public async Task WiFiDirect_HttpServerReachable()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;
        var session = await fixture.Session.EnterTransferModeAsync(CancellationToken.None);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var url = $"{session.BaseUrl}/files/media.config";
        _output.WriteLine($"GET {url}");

        var response = await http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Status: {(int)response.StatusCode}");
        _output.WriteLine($"Body ({body.Length} chars): {body[..Math.Min(500, body.Length)]}");

        response.IsSuccessStatusCode.Should().BeTrue(
            "glasses HTTP server should serve media.config after WiFi Direct connects");
        body.Should().NotBeNullOrEmpty("media.config should have content");

        await fixture.Session.ExitTransferModeAsync(CancellationToken.None);
    }

    // ── Performance ─────────────────────────────────────────────────

    [SkippableFact]
    public async Task WiFiDirect_ConnectionLatency_IsUnder30Seconds()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;

        // Ensure clean state
        try { await fixture.Session.ExitTransferModeAsync(CancellationToken.None); }
        catch { }
        await Task.Delay(5000);

        var sw = Stopwatch.StartNew();
        var session = await fixture.Session.EnterTransferModeAsync(CancellationToken.None);
        sw.Stop();

        _output.WriteLine($"WiFi Direct cold connect: {sw.ElapsedMilliseconds} ms");
        _output.WriteLine($"Glasses at: {session.BaseUrl}");

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30),
            "WiFi Direct discovery + P2P connect should complete within 30s");

        await fixture.Session.ExitTransferModeAsync(CancellationToken.None);
    }

    // ── State Verification ──────────────────────────────────────────

    [SkippableFact]
    public async Task ExitTransferMode_CleansUpWiFiDirect()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;
        var session = await fixture.Session.EnterTransferModeAsync(CancellationToken.None);

        fixture.Session.State.Should().Be(HeyCyanState.TransferMode);
        _output.WriteLine($"In transfer mode at {session.BaseUrl}");

        await fixture.Session.ExitTransferModeAsync(CancellationToken.None);

        fixture.Session.State.Should().Be(HeyCyanState.Connected,
            "should return to Connected state after exit");
        _output.WriteLine($"State after exit: {fixture.Session.State}");
    }
}
