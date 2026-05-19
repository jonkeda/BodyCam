using System.Net.NetworkInformation;
using BodyCam.Platforms.Windows.HeyCyan;
using BodyCam.RealTests.Fixtures;
using BodyCam.Services.Glasses.HeyCyan;
using Windows.Devices.WiFi;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Services.Glasses.HeyCyan;

/// <summary>
/// Diagnostic: enter transfer mode, then scan WiFi with WinRT API to see
/// if the glasses AP appears, and try connecting with the WinRT WiFi adapter.
/// </summary>
[Trait("Category", "RealWiFiDiag")]
public sealed class WiFiScanDiagnosticTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private WindowsHeyCyanRealFixture? _fixture;

    private static bool RealEnabled =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN") == "1";

    public WiFiScanDiagnosticTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        if (!RealEnabled) return;
        _fixture = await WindowsHeyCyanRealFixture.CreateAsync();
        var mac = Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_MAC") ?? "D8:79:B8:7F:E6:C9";
        await _fixture.ConnectByAddressAsync(mac, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_fixture is not null) await _fixture.DisposeAsync();
    }

    [SkippableFact]
    public async Task EnterTransferMode_ScanForGlassesAP()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(120)).Token;

        // Step 1: Enter transfer mode via the public API (will fail at WiFi but still sends BLE)
        _output.WriteLine("Step 1: Calling EnterTransferModeAsync (expect WiFi failure)...");
        try
        {
            var transferSession = await _fixture!.Session.EnterTransferModeAsync(ct);
            _output.WriteLine($"  *** TRANSFER MODE SUCCEEDED! BaseUrl={transferSession.BaseUrl} ***");
            // If it somehow worked, exit and return
            await _fixture.Session.ExitTransferModeAsync(ct);
            return;
        }
        catch (Exception ex)
        {
            _output.WriteLine($"  EnterTransferModeAsync failed (expected): {ex.GetType().Name}: {ex.Message}");
        }

        // At this point the BLE command has been sent and the glasses AP should be up.
        // Wait a bit more then scan with WinRT.
        _output.WriteLine("\n  Waiting 10s for AP to stabilize...");
        await Task.Delay(10_000, ct);

        // Step 2: Get WinRT WiFi adapter
        var access = await WiFiAdapter.RequestAccessAsync();
        _output.WriteLine($"\nStep 2: WiFi access status: {access}");
        if (access != WiFiAccessStatus.Allowed)
        {
            _output.WriteLine("  ACCESS DENIED - cannot scan");
            await TryExitTransferMode(ct);
            return;
        }

        var adapters = await WiFiAdapter.FindAllAdaptersAsync();
        if (adapters.Count == 0) { _output.WriteLine("  No adapters!"); await TryExitTransferMode(ct); return; }
        _output.WriteLine($"  Found {adapters.Count} adapter(s)");
        var adapter = adapters[0];

        // Step 3: Scan multiple times
        _output.WriteLine("\nStep 3: Scanning for WiFi networks (4 passes, 8s apart)...");
        for (int pass = 1; pass <= 4; pass++)
        {
            _output.WriteLine($"\n  --- Scan pass {pass} ---");
            await adapter.ScanAsync();
            var networks = adapter.NetworkReport.AvailableNetworks;

            _output.WriteLine($"  Found {networks.Count} networks total");

            // Show any that could be the glasses (including empty-SSID hidden networks)
            var candidates = networks.Where(n =>
                n.Ssid.Contains("M01", StringComparison.OrdinalIgnoreCase) ||
                n.Ssid.Contains("Pro_", StringComparison.OrdinalIgnoreCase) ||
                n.Ssid.Contains("D879", StringComparison.OrdinalIgnoreCase) ||
                n.Ssid.Contains("E6C9", StringComparison.OrdinalIgnoreCase) ||
                n.Ssid.Contains("DIRECT", StringComparison.OrdinalIgnoreCase) ||
                n.Ssid.Contains("HeyCyan", StringComparison.OrdinalIgnoreCase) ||
                n.Ssid.Contains("Glass", StringComparison.OrdinalIgnoreCase) ||
                n.Ssid.Contains("QC_", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(n.Ssid) // Hidden networks show as empty SSID
            ).ToList();

            if (candidates.Count > 0)
            {
                foreach (var c in candidates)
                {
                    _output.WriteLine($"  *** CANDIDATE: SSID=\"{(string.IsNullOrEmpty(c.Ssid) ? "<hidden>" : c.Ssid)}\" " +
                        $"signal={c.NetworkRssiInDecibelMilliwatts}dBm " +
                        $"band={c.ChannelCenterFrequencyInKilohertz / 1000}MHz " +
                        $"auth={c.SecuritySettings.NetworkAuthenticationType} " +
                        $"bssid={c.Bssid}");
                }
            }
            else
            {
                _output.WriteLine("  No candidates found");
                // Also list top-5 strongest to see what's visible
                var top5 = networks.OrderByDescending(n => n.NetworkRssiInDecibelMilliwatts).Take(5);
                foreach (var n in top5)
                    _output.WriteLine($"    (top) SSID=\"{n.Ssid}\" {n.NetworkRssiInDecibelMilliwatts}dBm bssid={n.Bssid}");
            }

            if (pass < 4) await Task.Delay(8_000, ct);
        }

        // Step 4: Also dump network interface state
        _output.WriteLine("\nStep 4: Network interfaces with IP addresses:");
        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (iface.OperationalStatus != OperationalStatus.Up) continue;
            if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var ips = iface.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => $"{a.Address}/{a.IPv4Mask}");

            _output.WriteLine($"  {iface.Name} ({iface.NetworkInterfaceType}): {string.Join(", ", ips)}");
        }

        // Step 5: Exit transfer mode
        await TryExitTransferMode(ct);
    }

    private async Task TryExitTransferMode(CancellationToken ct)
    {
        _output.WriteLine("\nExiting transfer mode...");
        try { await _fixture!.Session.ExitTransferModeAsync(ct); }
        catch (Exception ex) { _output.WriteLine($"  ExitTransferMode failed: {ex.Message}"); }
    }
}
