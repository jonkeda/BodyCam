using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using BodyCam.Platforms.Windows.HeyCyan;
using BodyCam.RealTests.Fixtures;
using BodyCam.Services.Glasses.HeyCyan;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.WiFi;
using Windows.Devices.WiFiDirect;
using Windows.Storage.Streams;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Services.Glasses.HeyCyan;

/// <summary>
/// Diagnostic tests for WiFi Direct peer discovery on Windows.
/// Tries multiple WinRT selector strategies to determine how (if at all)
/// the HeyCyan glasses appear as a WiFi Direct peer.
///
/// Context: RCA-803 — the current <c>AssociationEndpoint</c> selector
/// finds other WiFi Direct devices (TVs) but never the glasses.
///
/// Run with:
///   $env:BODYCAM_REAL_HEYCYAN="1"; $env:BODYCAM_REAL_HEYCYAN_MAC="D8:79:B8:7F:E6:C9"
///   dotnet test src/BodyCam.RealTests -f net10.0-windows10.0.19041.0 --filter "Category=WiFiDirectDiag" -v normal --logger "console;verbosity=detailed"
/// </summary>
[Trait("Category", "WiFiDirectDiag")]
[Collection("HeyCyanWiFiTransfer")]
public sealed class WiFiDirectDiagnosticTests
{
    private readonly ITestOutputHelper _output;
    private readonly SharedHeyCyanWiFiFixture _shared;

    private static bool RealEnabled =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN") == "1";

    private static string Mac =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_MAC") ?? "";

    public WiFiDirectDiagnosticTests(SharedHeyCyanWiFiFixture shared, ITestOutputHelper output)
    {
        _shared = shared;
        _output = output;
    }

    /// <summary>
    /// Core diagnostic: start three WiFi Direct watchers with different selectors
    /// BEFORE triggering transfer mode via the session. The session's own WiFi Direct
    /// will also be running in parallel — our extra watchers give us visibility
    /// into what each selector type can see.
    ///
    /// Flow:
    ///   1. Start 3 diagnostic watchers (AssociationEndpoint, DeviceInterface, Default)
    ///   2. Call EnterTransferModeAsync (sends BLE cmd + runs built-in WiFi Direct)
    ///   3. Whether EnterTransferMode succeeds or fails, log all peers found by each watcher
    ///   4. Exit transfer mode
    /// </summary>
    [SkippableFact]
    public async Task DiagAllSelectors_LogAllPeersAfterTransferMode()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;

        // 1. Start three watchers with different selectors BEFORE the BLE command
        _output.WriteLine("=== PHASE 1: Starting 3 WiFi Direct watchers ===\n");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var task1 = ScanWithSelector("AssociationEndpoint",
            WiFiDirectDevice.GetDeviceSelector(WiFiDirectDeviceSelectorType.AssociationEndpoint),
            cts.Token);

        var task2 = ScanWithSelector("DeviceInterface",
            WiFiDirectDevice.GetDeviceSelector(WiFiDirectDeviceSelectorType.DeviceInterface),
            cts.Token);

        var task3 = ScanWithSelector("Default",
            WiFiDirectDevice.GetDeviceSelector(),
            cts.Token);

        // Also try FindAllAsync snapshots at intervals
        var snapshotTask = PeriodicFindAllAsync(cts.Token);

        // 2. Give watchers a moment to start, then trigger transfer mode
        await Task.Delay(2000);
        _output.WriteLine("\n=== PHASE 2: Calling EnterTransferModeAsync ===");
        _output.WriteLine("(This sends BLE ResetP2p + EnterTransferMode + runs built-in WiFi Direct)\n");

        HeyCyanTransferSession? session = null;
        try
        {
            session = await fixture.Session.EnterTransferModeAsync(cts.Token);
            _output.WriteLine($"\n*** EnterTransferModeAsync SUCCEEDED! BaseUrl={session.BaseUrl} ***\n");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"\n*** EnterTransferModeAsync FAILED: {ex.GetType().Name}: {ex.Message} ***");
            _output.WriteLine("(This is expected — the diagnostic watchers may have found something useful)\n");
        }

        // 3. Stop watchers and collect results
        _output.WriteLine("\n=== PHASE 3: Stopping watchers ===");
        cts.Cancel();
        await Task.WhenAll(task1, task2, task3, snapshotTask);

        // 4. Clean up
        _output.WriteLine("\n=== PHASE 4: Cleanup ===");
        if (session is not null)
        {
            try
            {
                await fixture.Session.ExitTransferModeAsync(CancellationToken.None);
                _output.WriteLine("[BLE] ExitTransferMode sent");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"[BLE] ExitTransferMode failed: {ex.Message}");
            }
        }

        _output.WriteLine("\n=== DONE ===");
    }

    /// <summary>
    /// Try connecting directly to a constructed WiFi Direct device ID
    /// based on BLE MAC address variations (+0, +1, +2).
    /// Enters transfer mode first via the session so glasses are advertising P2P.
    /// </summary>
    [SkippableFact]
    public async Task DiagDirectConnect_TryMacVariations()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(string.IsNullOrEmpty(Mac), "BODYCAM_REAL_HEYCYAN_MAC not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;

        // Enter transfer mode (may fail — that's fine, glasses will still be in P2P mode)
        _output.WriteLine("Entering transfer mode (may timeout — expected)...");
        HeyCyanTransferSession? session = null;
        try
        {
            using var enterCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            session = await fixture.Session.EnterTransferModeAsync(enterCts.Token);
            _output.WriteLine($"Transfer mode active: {session.BaseUrl}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"EnterTransferModeAsync failed (expected): {ex.Message}");
            _output.WriteLine("Glasses should still be in P2P mode — trying direct connect...\n");
        }

        // Parse BLE MAC and try variations
        var macClean = Mac.Replace(":", "").Replace("-", "").ToUpperInvariant();
        var macBytes = Convert.FromHexString(macClean);

        _output.WriteLine($"BLE MAC: {Mac} ({macClean})");
        _output.WriteLine("Trying WiFi Direct device ID variations...\n");

        var variations = new List<(string label, string mac)>
        {
            ("BLE MAC exact", macClean),
            ("BLE MAC +1", IncrementMac(macBytes, 1)),
            ("BLE MAC +2", IncrementMac(macBytes, 2)),
            ("BLE MAC -1", IncrementMac(macBytes, -1)),
            ("BLE MAC -2", IncrementMac(macBytes, -2)),
        };

        foreach (var (label, mac) in variations)
        {
            var formattedMac = FormatMac(mac);
            var deviceId = $"WiFiDirect#{formattedMac}";
            _output.WriteLine($"[{label}] Trying device ID: {deviceId}");

            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var device = await WiFiDirectDevice.FromIdAsync(deviceId)
                    .AsTask(timeoutCts.Token);

                if (device is not null)
                {
                    _output.WriteLine($"  *** CONNECTED! ConnectionStatus={device.ConnectionStatus}");
                    var endpoints = device.GetConnectionEndpointPairs();
                    foreach (var ep in endpoints)
                    {
                        _output.WriteLine($"  Endpoint: Local={ep.LocalHostName?.CanonicalName} " +
                            $"Remote={ep.RemoteHostName?.CanonicalName}");
                    }
                    device.Dispose();
                }
                else
                {
                    _output.WriteLine("  FromIdAsync returned null");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  Failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Clean up
        if (session is not null)
        {
            try { await fixture.Session.ExitTransferModeAsync(CancellationToken.None); }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// List all paired WiFi Direct devices on the system (no BLE command needed).
    /// Helps determine if glasses were ever paired via WiFi Direct.
    /// </summary>
    [SkippableFact]
    public async Task DiagListPairedWiFiDirectDevices()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");

        _output.WriteLine("=== Listing ALL paired WiFi Direct devices ===\n");

        var selector = WiFiDirectDevice.GetDeviceSelector(
            WiFiDirectDeviceSelectorType.DeviceInterface);
        _output.WriteLine($"Selector: {selector}\n");

        var devices = await DeviceInformation.FindAllAsync(selector);
        _output.WriteLine($"Found {devices.Count} paired WiFi Direct device(s):\n");

        foreach (var dev in devices)
        {
            _output.WriteLine($"  Name: '{dev.Name}'");
            _output.WriteLine($"  Id:   {dev.Id}");
            _output.WriteLine($"  Kind: {dev.Kind}");
            _output.WriteLine($"  Paired: {dev.Pairing?.IsPaired}");
            _output.WriteLine($"  Enabled: {dev.IsEnabled}");

            foreach (var prop in dev.Properties)
            {
                _output.WriteLine($"    [{prop.Key}] = {prop.Value}");
            }
            _output.WriteLine("");
        }

        if (devices.Count == 0)
            _output.WriteLine("  (none found)");
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private async Task PeriodicFindAllAsync(CancellationToken ct)
    {
        var selector = WiFiDirectDevice.GetDeviceSelector(
            WiFiDirectDeviceSelectorType.AssociationEndpoint);

        int round = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }
            catch (OperationCanceledException) { break; }

            round++;
            await FindAllAsync($"FindAll-Snapshot-{round}", selector);
        }
    }

    private async Task ScanWithSelector(string label, string selector, CancellationToken ct)
    {
        _output.WriteLine($"[{label}] Selector: {selector}");

        var peers = new List<(string name, string id, DeviceInformationKind kind)>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var watcher = DeviceInformation.CreateWatcher(
            selector,
            new[]
            {
                "System.Devices.WiFiDirect.InformationElements",
                "System.Devices.Aep.DeviceAddress",
                "System.Devices.Aep.IsConnected",
                "System.Devices.Aep.SignalStrength",
            });

        watcher.Added += (s, info) =>
        {
            lock (peers)
            {
                peers.Add((info.Name, info.Id, info.Kind));
            }

            var sb = new StringBuilder();
            sb.Append($"[{label}] PEER: '{info.Name}' (id={info.Id}, kind={info.Kind})");
            foreach (var prop in info.Properties)
            {
                if (prop.Value is not null)
                    sb.Append($"\n  [{label}]   {prop.Key} = {prop.Value}");
            }
            _output.WriteLine(sb.ToString());
            Console.Error.WriteLine(sb.ToString());
        };

        watcher.Updated += (s, update) =>
        {
            _output.WriteLine($"[{label}] UPDATED: {update.Id}");
        };

        watcher.EnumerationCompleted += (s, _) =>
        {
            _output.WriteLine($"[{label}] Enumeration completed (continuing to watch)");
        };

        watcher.Stopped += (s, _) =>
        {
            _output.WriteLine($"[{label}] Watcher stopped");
            tcs.TrySetResult();
        };

        try
        {
            watcher.Start();

            // Wait until cancellation
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { /* expected */ }
        }
        finally
        {
            try { watcher.Stop(); }
            catch { /* ignore */ }
        }

        // Wait for stopped callback
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await tcs.Task.WaitAsync(stopCts.Token); }
        catch { /* timeout waiting for stop callback */ }

        _output.WriteLine($"\n[{label}] SUMMARY: {peers.Count} peer(s) found:");
        lock (peers)
        {
            foreach (var (name, id, kind) in peers)
            {
                _output.WriteLine($"  [{label}] '{name}' (kind={kind})");
            }
        }
        _output.WriteLine("");
    }

    private async Task FindAllAsync(string label, string selector)
    {
        _output.WriteLine($"[{label}] One-shot FindAllAsync with selector: {selector}");

        try
        {
            var devices = await DeviceInformation.FindAllAsync(selector);
            _output.WriteLine($"[{label}] Found {devices.Count} device(s):");

            foreach (var dev in devices)
            {
                _output.WriteLine($"  [{label}] '{dev.Name}' id={dev.Id} kind={dev.Kind} paired={dev.Pairing?.IsPaired}");
                foreach (var prop in dev.Properties)
                {
                    if (prop.Value is not null)
                        _output.WriteLine($"    [{label}] {prop.Key} = {prop.Value}");
                }
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"[{label}] FindAllAsync failed: {ex.Message}");
        }
        _output.WriteLine("");
    }

    private static string IncrementMac(byte[] mac, int offset)
    {
        var result = (byte[])mac.Clone();
        int carry = offset;
        for (int i = result.Length - 1; i >= 0 && carry != 0; i--)
        {
            int val = result[i] + carry;
            result[i] = (byte)(val & 0xFF);
            carry = val < 0 ? -1 : (val > 255 ? 1 : 0);
        }
        return Convert.ToHexString(result);
    }

    private static string FormatMac(string hex)
    {
        // Format as XX:XX:XX:XX:XX:XX
        var sb = new StringBuilder(17);
        for (int i = 0; i < hex.Length; i += 2)
        {
            if (i > 0) sb.Append(':');
            sb.Append(hex, i, 2);
        }
        return sb.ToString();
    }

    // ── WiFi SSID Diff Test ─────────────────────────────────────────

    /// <summary>
    /// Scan WiFi SSIDs BEFORE and AFTER entering transfer mode to detect
    /// any new networks the glasses create (regular hotspot or WiFi Direct
    /// group appearing as "DIRECT-xx-..." SSID).
    /// </summary>
    [SkippableFact]
    public async Task DiagWiFiSsidDiff_DetectNewNetworksAfterTransferMode()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;

        // Get WiFi adapter
        var access = await WiFiAdapter.RequestAccessAsync();
        Skip.If(access != WiFiAccessStatus.Allowed, $"WiFi access denied: {access}");

        var adapters = await WiFiAdapter.FindAllAdaptersAsync();
        Skip.If(adapters.Count == 0, "No WiFi adapter found");
        var adapter = adapters[0];

        // 1. Baseline scan — SSIDs visible BEFORE transfer mode
        _output.WriteLine("=== BASELINE WiFi scan (before transfer mode) ===");
        await adapter.ScanAsync();
        var baselineSsids = adapter.NetworkReport.AvailableNetworks
            .Select(n => n.Ssid)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _output.WriteLine($"Baseline: {baselineSsids.Count} SSIDs");
        foreach (var ssid in baselineSsids.OrderBy(s => s))
            _output.WriteLine($"  [baseline] {ssid}");

        // 2. Enter transfer mode (will timeout — expected)
        _output.WriteLine("\n=== Entering transfer mode (30s timeout — expected to fail) ===");
        try
        {
            using var enterCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var session = await fixture.Session.EnterTransferModeAsync(enterCts.Token);
            _output.WriteLine($"*** Transfer mode succeeded! BaseUrl={session.BaseUrl} ***");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Transfer mode failed (expected): {ex.GetType().Name}");
        }

        // 3. Repeated WiFi scans looking for new SSIDs
        _output.WriteLine("\n=== Scanning for new SSIDs (6 scans, 5s apart) ===");

        var allNewSsids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i <= 6; i++)
        {
            await Task.Delay(5000);
            await adapter.ScanAsync();

            var currentSsids = adapter.NetworkReport.AvailableNetworks
                .Select(n => new { n.Ssid, n.NetworkRssiInDecibelMilliwatts, n.SecuritySettings })
                .Where(n => !string.IsNullOrEmpty(n.Ssid))
                .ToList();

            var newSsids = currentSsids
                .Where(n => !baselineSsids.Contains(n.Ssid))
                .ToList();

            _output.WriteLine($"\n  Scan {i}: {currentSsids.Count} total SSIDs, {newSsids.Count} NEW");

            foreach (var n in newSsids)
            {
                allNewSsids.Add(n.Ssid);
                _output.WriteLine($"    *** NEW: '{n.Ssid}' signal={n.NetworkRssiInDecibelMilliwatts}dBm auth={n.SecuritySettings?.NetworkAuthenticationType}");
            }

            // Also log any DIRECT- SSIDs even if they were in baseline
            foreach (var n in currentSsids.Where(n => n.Ssid.StartsWith("DIRECT-", StringComparison.OrdinalIgnoreCase)))
            {
                _output.WriteLine($"    [DIRECT-] '{n.Ssid}' signal={n.NetworkRssiInDecibelMilliwatts}dBm");
            }
        }

        // 4. Summary
        _output.WriteLine($"\n=== SUMMARY: {allNewSsids.Count} new SSID(s) appeared ===");
        foreach (var ssid in allNewSsids.OrderBy(s => s))
            _output.WriteLine($"  NEW: '{ssid}'");

        if (allNewSsids.Count == 0)
            _output.WriteLine("  (none — glasses P2P group is not visible as a regular WiFi SSID)");

        // 5. Try connecting to common glasses IPs without joining any network
        //    (brute-force like iOS GlassesWiFiHandler does)
        _output.WriteLine("\n=== Probing common glasses IPs (brute-force, 3s timeout each) ===");

        var probeIps = new[]
        {
            "192.168.43.1", "192.168.4.1", "192.168.31.1", "192.168.1.1",
            "192.168.0.1", "192.168.100.1", "192.168.123.1", "192.168.137.1",
            "10.0.0.1", "172.20.10.1", "192.168.49.1"
        };

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        foreach (var ip in probeIps)
        {
            try
            {
                var response = await http.GetAsync($"http://{ip}/files/media.config");
                _output.WriteLine($"  *** {ip}: HTTP {(int)response.StatusCode} — REACHABLE! ***");
            }
            catch (TaskCanceledException)
            {
                _output.WriteLine($"  {ip}: timeout");
            }
            catch (HttpRequestException ex)
            {
                _output.WriteLine($"  {ip}: {ex.InnerException?.GetType().Name ?? ex.GetType().Name}");
            }
        }

        _output.WriteLine("\n=== DONE ===");
    }

    /// <summary>
    /// RCA-803 Option D: Send iOS-style BLE command {0x41, 0x04} (SetDeviceMode + Transfer)
    /// to the glasses and capture ALL BLE notify responses. The iOS QCSDK uses this opcode
    /// to receive the WiFi SSID + password from the glasses.
    ///
    /// Also tries the existing Windows command {0x02, 0x01, 0x04} for comparison and logs
    /// all notify frames as hex + ASCII for manual SSID extraction.
    /// </summary>
    [SkippableFact]
    [Trait("Category", "WiFiDirectDiag")]
    public async Task DiagBleSetDeviceMode_CaptureSSIDFromNotify()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        var fixture = _shared.Inner;
        Skip.If(fixture is null, "Fixture not initialized");

        var session = fixture.Session;
        var frames = new System.Collections.Concurrent.ConcurrentBag<(DateTimeOffset Time, byte[] Data)>();

        // Subscribe to ALL raw notify frames
        void OnNotify(object? sender, byte[] data)
        {
            var copy = data.ToArray();
            frames.Add((DateTimeOffset.UtcNow, copy));
            _output.WriteLine($"[NOTIFY] ({copy.Length}B) {BitConverter.ToString(copy)}");
            // Also try to decode as ASCII (SSID/password might be embedded)
            if (copy.Length > 7)
            {
                var ascii = TryDecodeAscii(copy.AsSpan(7));
                if (!string.IsNullOrWhiteSpace(ascii))
                    _output.WriteLine($"[NOTIFY] ASCII payload [7..]: \"{ascii}\"");
            }
            if (copy.Length > 2)
            {
                var ascii = TryDecodeAscii(copy.AsSpan(2));
                if (!string.IsNullOrWhiteSpace(ascii))
                    _output.WriteLine($"[NOTIFY] ASCII payload [2..]: \"{ascii}\"");
            }
        }

        session.RawNotifyReceived += OnNotify;
        try
        {
            // ── Phase 0: Verify BLE notifications work ──
            _output.WriteLine("=== Phase 0: Verify notifications with GetBattery ===");
            _output.WriteLine($"  Session state: {session.State}");
            try
            {
                var battery = await session.GetBatteryAsync(CancellationToken.None);
                _output.WriteLine($"  Battery: {battery.Percentage}% (charging={battery.IsCharging})");
                _output.WriteLine($"  Frames captured: {frames.Count}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  GetBattery failed: {ex.GetType().Name}: {ex.Message}");
            }

            if (frames.Count == 0)
            {
                _output.WriteLine("  WARNING: Zero notify frames received from GetBattery!");
                _output.WriteLine("  Waiting 5s for any late notifications...");
                await Task.Delay(5000);
                _output.WriteLine($"  After wait: {frames.Count} frames");
            }

            // ── Phase 1: ResetP2p then iOS-style opcode {0x41, 0x04} ──
            _output.WriteLine("\n=== Phase 1: ResetP2p + iOS-style {0x41, 0x04} ===");
            var phase1Start = frames.Count;
            try
            {
                await session.SendRawDiagnosticCommandAsync(new byte[] { 0x02, 0x01, 0x0F }, CancellationToken.None);
                _output.WriteLine("  ResetP2p sent, waiting 2s...");
                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  ResetP2p failed: {ex.Message}");
            }
            try
            {
                await session.SendRawDiagnosticCommandAsync(new byte[] { 0x41, 0x04 }, CancellationToken.None);
                _output.WriteLine("  {0x41, 0x04} sent");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  Write failed: {ex.GetType().Name}: {ex.Message}");
            }
            _output.WriteLine("  Waiting 15s for notify responses...");
            await Task.Delay(TimeSpan.FromSeconds(15));
            _output.WriteLine($"  Frames after phase 1: {frames.Count - phase1Start}");

            // ── Phase 2: Standard EnterTransferMode for comparison ──
            _output.WriteLine("\n=== Phase 2: Standard {0x02, 0x01, 0x04} (EnterTransferMode) ===");
            var phase2Start = frames.Count;
            try
            {
                await session.SendRawDiagnosticCommandAsync(new byte[] { 0x02, 0x01, 0x04 }, CancellationToken.None);
                _output.WriteLine("  EnterTransferMode sent");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  Write failed: {ex.GetType().Name}: {ex.Message}");
            }
            _output.WriteLine("  Waiting 20s for notify responses...");
            await Task.Delay(TimeSpan.FromSeconds(20));
            _output.WriteLine($"  Frames after phase 2: {frames.Count - phase2Start}");

            // ── Phase 3: Retry {0x41, 0x04} while transfer mode is (possibly) active ──
            _output.WriteLine("\n=== Phase 3: Retry {0x41, 0x04} after EnterTransferMode ===");
            var phase3Start = frames.Count;
            try
            {
                await session.SendRawDiagnosticCommandAsync(new byte[] { 0x41, 0x04 }, CancellationToken.None);
                _output.WriteLine("  {0x41, 0x04} sent");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  Write failed: {ex.GetType().Name}: {ex.Message}");
            }
            _output.WriteLine("  Waiting 15s for notify responses...");
            await Task.Delay(TimeSpan.FromSeconds(15));
            _output.WriteLine($"  Frames after phase 3: {frames.Count - phase3Start}");

            // ── Summary ──
            _output.WriteLine("\n=== ALL CAPTURED FRAMES ===");
            var sorted = frames.OrderBy(f => f.Time).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var f = sorted[i];
                _output.WriteLine($"  [{i:D3}] {f.Time:HH:mm:ss.fff} ({f.Data.Length}B) {BitConverter.ToString(f.Data)}");
                if (f.Data.Length > 7)
                {
                    var ascii = TryDecodeAscii(f.Data.AsSpan(7));
                    if (!string.IsNullOrWhiteSpace(ascii))
                        _output.WriteLine($"       ASCII [7..]: \"{ascii}\"");
                }
                var fullAscii = TryDecodeAscii(f.Data);
                if (!string.IsNullOrWhiteSpace(fullAscii) && fullAscii.Length > 3)
                    _output.WriteLine($"       ASCII [all]: \"{fullAscii}\"");
            }

            _output.WriteLine($"\nTotal frames: {sorted.Count}");

            // Look for frames containing SSID-like strings
            foreach (var f in sorted)
            {
                var ascii = Encoding.ASCII.GetString(f.Data);
                if (ascii.Contains("DIRECT", StringComparison.OrdinalIgnoreCase) ||
                    ascii.Contains("QC_", StringComparison.OrdinalIgnoreCase) ||
                    ascii.Contains("O_", StringComparison.OrdinalIgnoreCase) ||
                    ascii.Contains("Cyan", StringComparison.OrdinalIgnoreCase) ||
                    ascii.Contains("M01", StringComparison.OrdinalIgnoreCase) ||
                    ascii.Contains("123456789", StringComparison.Ordinal))
                {
                    _output.WriteLine($"  *** SSID CANDIDATE: {BitConverter.ToString(f.Data)}");
                    _output.WriteLine($"      ASCII: \"{ascii}\"");
                }
            }

            // Exit transfer mode
            _output.WriteLine("\n=== Exiting transfer mode ===");
            try
            {
                await session.SendRawDiagnosticCommandAsync(new byte[] { 0x02, 0x01, 0x09 }, CancellationToken.None);
                _output.WriteLine("  ExitTransferMode sent");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  Exit failed: {ex.Message}");
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        finally
        {
            session.RawNotifyReceived -= OnNotify;
        }

        _output.WriteLine("\n=== DONE ===");
    }

    private static string TryDecodeAscii(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length);
        foreach (var b in data)
        {
            if (b >= 0x20 && b < 0x7F)
                sb.Append((char)b);
            else if (b != 0)
                sb.Append('.');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Enumerate ALL GATT services and characteristics on the connected glasses.
    /// The iOS QCSDK may use characteristics under the 7905FFF0 service (not de5bf728).
    /// </summary>
    [SkippableFact]
    [Trait("Category", "WiFiDirectDiag")]
    public async Task DiagEnumerateGattServices_FindAllCharacteristics()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        var fixture = _shared.Inner;
        Skip.If(fixture is null, "Fixture not initialized");

        var bleDevice = fixture.Session.DiagnosticBleDevice;
        Skip.If(bleDevice is null, "BLE device not connected");

        _output.WriteLine($"=== GATT Service Enumeration for {bleDevice.Name} ===\n");

        var servicesResult = await bleDevice.GetGattServicesAsync().AsTask();
        _output.WriteLine($"GetGattServicesAsync status: {servicesResult.Status}");
        _output.WriteLine($"Services found: {servicesResult.Services.Count}\n");

        var writableChars = new List<(GattCharacteristic Char, string ServiceUuid)>();

        foreach (var service in servicesResult.Services)
        {
            _output.WriteLine($"── Service: {service.Uuid} ──");
            var charsResult = await service.GetCharacteristicsAsync().AsTask();
            if (charsResult.Status != GattCommunicationStatus.Success)
            {
                _output.WriteLine($"  (Failed to get characteristics: {charsResult.Status})");
                continue;
            }

            foreach (var ch in charsResult.Characteristics)
            {
                var props = ch.CharacteristicProperties;
                var propList = new List<string>();
                if (props.HasFlag(GattCharacteristicProperties.Read)) propList.Add("Read");
                if (props.HasFlag(GattCharacteristicProperties.Write)) propList.Add("Write");
                if (props.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)) propList.Add("WriteNoResp");
                if (props.HasFlag(GattCharacteristicProperties.Notify)) propList.Add("Notify");
                if (props.HasFlag(GattCharacteristicProperties.Indicate)) propList.Add("Indicate");

                _output.WriteLine($"  Char: {ch.Uuid}  [{string.Join(", ", propList)}]");

                // Try to read readable characteristics
                if (props.HasFlag(GattCharacteristicProperties.Read))
                {
                    try
                    {
                        var readResult = await ch.ReadValueAsync().AsTask();
                        if (readResult.Status == GattCommunicationStatus.Success && readResult.Value.Length > 0)
                        {
                            var bytes = readResult.Value.ToArray();
                            _output.WriteLine($"    Value ({bytes.Length}B): {BitConverter.ToString(bytes)}");
                            var ascii = TryDecodeAscii(bytes);
                            if (!string.IsNullOrWhiteSpace(ascii))
                                _output.WriteLine($"    ASCII: \"{ascii}\"");
                        }
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"    Read failed: {ex.GetType().Name}");
                    }
                }

                // Track writable characteristics for later testing
                if (props.HasFlag(GattCharacteristicProperties.Write) ||
                    props.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
                {
                    writableChars.Add((ch, service.Uuid.ToString()));
                }
            }
            _output.WriteLine("");
        }

        // Summary of writable characteristics (potential command targets)
        _output.WriteLine("=== WRITABLE CHARACTERISTICS (potential command targets) ===");
        foreach (var (ch, svcUuid) in writableChars)
        {
            _output.WriteLine($"  Service {svcUuid} → Char {ch.Uuid}");
        }

        _output.WriteLine("\n=== DONE ===");
    }

    /// <summary>
    /// Try sending the {0x41, 0x04} command on ALL writable characteristics
    /// (not just de5bf72a) and capture responses on ALL notify characteristics.
    /// The iOS QCSDK may use a different write characteristic under the 7905FFF0 service.
    /// </summary>
    [SkippableFact]
    [Trait("Category", "WiFiDirectDiag")]
    public async Task DiagTryAllCharacteristics_SendSetDeviceModeTransfer()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        var fixture = _shared.Inner;
        Skip.If(fixture is null, "Fixture not initialized");

        var session = fixture.Session;
        var bleDevice = fixture.Session.DiagnosticBleDevice;
        Skip.If(bleDevice is null, "BLE device not connected");

        var frames = new System.Collections.Concurrent.ConcurrentBag<(DateTimeOffset Time, string Source, byte[] Data)>();

        // Subscribe to the standard notify channel
        void OnNotify(object? sender, byte[] data)
        {
            var copy = data.ToArray();
            frames.Add((DateTimeOffset.UtcNow, "de5bf729", copy));
            _output.WriteLine($"[de5bf729-NOTIFY] ({copy.Length}B) {BitConverter.ToString(copy)}");
        }
        session.RawNotifyReceived += OnNotify;

        // Find and subscribe to ALL notify characteristics across ALL services
        var notifySubscriptions = new List<(GattCharacteristic Char, string Label)>();
        var servicesResult = await bleDevice.GetGattServicesAsync().AsTask();

        foreach (var service in servicesResult.Services)
        {
            var charsResult = await service.GetCharacteristicsAsync().AsTask();
            if (charsResult.Status != GattCommunicationStatus.Success) continue;

            foreach (var ch in charsResult.Characteristics)
            {
                // Skip the one we already have (de5bf729)
                if (ch.Uuid == Guid.Parse("de5bf729-d711-4e47-af26-65e3012a5dc7"))
                    continue;

                if (ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify) ||
                    ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate))
                {
                    var label = $"{service.Uuid.ToString()[..8]}:{ch.Uuid.ToString()[..8]}";
                    try
                    {
                        var cccdValue = ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate)
                            ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
                            : GattClientCharacteristicConfigurationDescriptorValue.Notify;
                        var subResult = await ch.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue).AsTask();
                        if (subResult == GattCommunicationStatus.Success)
                        {
                            var capturedLabel = label;
                            ch.ValueChanged += (sender, args) =>
                            {
                                var bytes = args.CharacteristicValue.ToArray();
                                frames.Add((DateTimeOffset.UtcNow, capturedLabel, (byte[])bytes.Clone()));
                                _output.WriteLine($"[{capturedLabel}-NOTIFY] ({bytes.Length}B) {BitConverter.ToString(bytes)}");
                                var ascii = TryDecodeAscii(bytes);
                                if (!string.IsNullOrWhiteSpace(ascii) && ascii.Length > 3)
                                    _output.WriteLine($"  ASCII: \"{ascii}\"");
                            };
                            notifySubscriptions.Add((ch, label));
                            _output.WriteLine($"Subscribed to notify: {label}");
                        }
                        else
                        {
                            _output.WriteLine($"Failed to subscribe {label}: {subResult}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"Failed to subscribe {label}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        _output.WriteLine($"\nTotal notify subscriptions (excluding de5bf729): {notifySubscriptions.Count}\n");

        try
        {
            // Find ALL writable characteristics (excluding de5bf72a which we already use)
            var writableChars = new List<(GattCharacteristic Char, string Label)>();
            foreach (var service in servicesResult.Services)
            {
                var charsResult = await service.GetCharacteristicsAsync().AsTask();
                if (charsResult.Status != GattCommunicationStatus.Success) continue;

                foreach (var ch in charsResult.Characteristics)
                {
                    if (ch.Uuid == Guid.Parse("de5bf72a-d711-4e47-af26-65e3012a5dc7"))
                        continue;
                    if (ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) ||
                        ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
                    {
                        var label = $"{service.Uuid.ToString()[..8]}:{ch.Uuid.ToString()[..8]}";
                        writableChars.Add((ch, label));
                    }
                }
            }

            // Phase 1: Send {0x41, 0x04} on the standard de5bf72a characteristic
            _output.WriteLine("=== Phase 1: {0x41, 0x04} on standard de5bf72a ===");
            await session.SendRawDiagnosticCommandAsync(new byte[] { 0x41, 0x04 }, CancellationToken.None);
            _output.WriteLine("  Sent, waiting 10s...");
            await Task.Delay(10_000);
            _output.WriteLine($"  Frames: {frames.Count}");

            // Phase 2: Send {0x41, 0x04} on EACH other writable characteristic
            foreach (var (ch, label) in writableChars)
            {
                _output.WriteLine($"\n=== Phase 2: {{0x41, 0x04}} on {label} ===");
                var before = frames.Count;
                try
                {
                    var writer = new DataWriter();
                    writer.WriteBytes(new byte[] { 0x41, 0x04 });
                    var writeOpt = ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                        ? GattWriteOption.WriteWithoutResponse
                        : GattWriteOption.WriteWithResponse;
                    var result = await ch.WriteValueWithResultAsync(writer.DetachBuffer(), writeOpt).AsTask();
                    _output.WriteLine($"  Write result: {result.Status}");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"  Write failed: {ex.GetType().Name}: {ex.Message}");
                }
                await Task.Delay(5_000);
                _output.WriteLine($"  New frames: {frames.Count - before}");
            }

            // Phase 3: Send standard EnterTransferMode {0x02, 0x01, 0x04} on de5bf72a
            _output.WriteLine("\n=== Phase 3: Standard {0x02, 0x01, 0x04} on de5bf72a ===");
            var phase3Start = frames.Count;
            await session.SendRawDiagnosticCommandAsync(new byte[] { 0x02, 0x01, 0x0F }, CancellationToken.None);
            _output.WriteLine("  ResetP2p sent");
            await Task.Delay(2_000);
            await session.SendRawDiagnosticCommandAsync(new byte[] { 0x02, 0x01, 0x04 }, CancellationToken.None);
            _output.WriteLine("  EnterTransferMode sent, waiting 20s...");
            await Task.Delay(20_000);
            _output.WriteLine($"  Frames: {frames.Count - phase3Start}");

            // Summary
            _output.WriteLine("\n=== ALL CAPTURED FRAMES ===");
            var sorted = frames.OrderBy(f => f.Time).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var f = sorted[i];
                _output.WriteLine($"  [{i:D3}] {f.Time:HH:mm:ss.fff} [{f.Source}] ({f.Data.Length}B) {BitConverter.ToString(f.Data)}");
                var ascii = TryDecodeAscii(f.Data);
                if (!string.IsNullOrWhiteSpace(ascii) && ascii.Length > 3)
                    _output.WriteLine($"       ASCII: \"{ascii}\"");
            }
            _output.WriteLine($"\nTotal frames: {sorted.Count}");

            // Exit transfer mode
            _output.WriteLine("\n=== Cleanup ===");
            try
            {
                await session.SendRawDiagnosticCommandAsync(new byte[] { 0x02, 0x01, 0x09 }, CancellationToken.None);
                _output.WriteLine("  ExitTransferMode sent");
            }
            catch { }
            await Task.Delay(2_000);
        }
        finally
        {
            session.RawNotifyReceived -= OnNotify;
            // Unsubscribe from extra notify characteristics
            foreach (var (ch, _) in notifySubscriptions)
            {
                try
                {
                    await ch.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None).AsTask();
                }
                catch { }
            }
        }

        _output.WriteLine("\n=== DONE ===");
    }

    /// <summary>
    /// Send GAIA-formatted SetDeviceMode commands to the Qualcomm GAIA service (ae30)
    /// and capture responses. GAIA BLE frame format:
    ///   [VendorID MSB] [VendorID LSB] [CommandID MSB] [CommandID LSB] [Payload...]
    /// </summary>
    [SkippableFact]
    [Trait("Category", "WiFiDirectDiag")]
    public async Task DiagGaiaSetDeviceMode_TryMultipleFormats()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        var fixture = _shared.Inner;
        Skip.If(fixture is null, "Fixture not initialized");

        var bleDevice = fixture.Session.DiagnosticBleDevice;
        Skip.If(bleDevice is null, "BLE device not connected");

        // Get GAIA service (0000ae30) write and notify characteristics
        var svcResult = await bleDevice.GetGattServicesForUuidAsync(
            Guid.Parse("0000ae30-0000-1000-8000-00805f9b34fb")).AsTask();
        Skip.If(svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0,
            "GAIA service not found");
        var gaiaSvc = svcResult.Services[0];

        var charsResult = await gaiaSvc.GetCharacteristicsAsync().AsTask();
        var writeChar = charsResult.Characteristics.FirstOrDefault(c =>
            c.Uuid == Guid.Parse("0000ae01-0000-1000-8000-00805f9b34fb"));
        var notifyChar = charsResult.Characteristics.FirstOrDefault(c =>
            c.Uuid == Guid.Parse("0000ae02-0000-1000-8000-00805f9b34fb"));
        Skip.If(writeChar is null || notifyChar is null, "GAIA write/notify chars not found");

        var frames = new System.Collections.Concurrent.ConcurrentBag<(DateTimeOffset Time, byte[] Data)>();

        // Subscribe to GAIA notify
        var subResult = await notifyChar.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask();
        _output.WriteLine($"GAIA notify subscription: {subResult}");

        notifyChar.ValueChanged += (sender, args) =>
        {
            var bytes = args.CharacteristicValue.ToArray();
            frames.Add((DateTimeOffset.UtcNow, bytes));
            _output.WriteLine($"  [GAIA-NOTIFY] ({bytes.Length}B) {BitConverter.ToString(bytes)}");
            var ascii = TryDecodeAscii(bytes);
            if (!string.IsNullOrWhiteSpace(ascii) && ascii.Length > 2)
                _output.WriteLine($"  [GAIA-NOTIFY] ASCII: \"{ascii}\"");
        };

        // Also subscribe to ae04 (second GAIA notify) and ae05 (indicate)
        var notifyChar4 = charsResult.Characteristics.FirstOrDefault(c =>
            c.Uuid == Guid.Parse("0000ae04-0000-1000-8000-00805f9b34fb"));
        if (notifyChar4 is not null)
        {
            await notifyChar4.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask();
            notifyChar4.ValueChanged += (sender, args) =>
            {
                var bytes = args.CharacteristicValue.ToArray();
                frames.Add((DateTimeOffset.UtcNow, bytes));
                _output.WriteLine($"  [GAIA-ae04] ({bytes.Length}B) {BitConverter.ToString(bytes)}");
                var ascii = TryDecodeAscii(bytes);
                if (!string.IsNullOrWhiteSpace(ascii) && ascii.Length > 2)
                    _output.WriteLine($"  [GAIA-ae04] ASCII: \"{ascii}\"");
            };
            _output.WriteLine("Subscribed to ae04 (second GAIA notify)");
        }

        // Also listen on de5bf729 (serial port)
        void OnSerialNotify(object? sender, byte[] data)
        {
            _output.WriteLine($"  [SERIAL-NOTIFY] ({data.Length}B) {BitConverter.ToString(data)}");
            var ascii = TryDecodeAscii(data);
            if (!string.IsNullOrWhiteSpace(ascii) && ascii.Length > 2)
                _output.WriteLine($"  [SERIAL-NOTIFY] ASCII: \"{ascii}\"");
        }
        fixture.Session.RawNotifyReceived += OnSerialNotify;

        try
        {
            // Define GAIA command variants to try
            var commands = new (string Name, byte[] Data)[]
            {
                // Raw opcode (already echoed in previous test)
                ("Raw {0x41, 0x04}", new byte[] { 0x41, 0x04 }),

                // GAIA v3: Vendor=0x001D (Qualcomm), Cmd=0x0041, Payload=0x04
                ("GAIA Vendor=001D Cmd=0041 Pay=04", new byte[] { 0x00, 0x1D, 0x00, 0x41, 0x04 }),

                // GAIA v3: Vendor=0x000A (CSR), Cmd=0x0041, Payload=0x04
                ("GAIA Vendor=000A Cmd=0041 Pay=04", new byte[] { 0x00, 0x0A, 0x00, 0x41, 0x04 }),

                // GAIA v3: Vendor=0x001D, Cmd=0x0104 (feature=0x01, subtype=0x04)
                ("GAIA Vendor=001D Cmd=0104", new byte[] { 0x00, 0x1D, 0x01, 0x04 }),

                // Our serial port EnterTransferMode via GAIA
                ("GAIA Vendor=001D Cmd=0201 Pay=04", new byte[] { 0x00, 0x1D, 0x02, 0x01, 0x04 }),

                // Serial port command through GAIA channel
                ("Serial cmd via GAIA {02, 01, 04}", new byte[] { 0x02, 0x01, 0x04 }),

                // GAIA v3: Vendor=0x00D2 (common OEM), Cmd=0x0041, Payload=0x04
                ("GAIA Vendor=00D2 Cmd=0041 Pay=04", new byte[] { 0x00, 0xD2, 0x00, 0x41, 0x04 }),

                // GAIA v3: Vendor=0x001D, Cmd=0x4104 (0x41 in high byte)
                ("GAIA Vendor=001D Cmd=4104", new byte[] { 0x00, 0x1D, 0x41, 0x04 }),
            };

            for (int i = 0; i < commands.Length; i++)
            {
                var (name, data) = commands[i];
                var before = frames.Count;

                _output.WriteLine($"\n=== [{i+1}/{commands.Length}] {name} ===");
                _output.WriteLine($"  Bytes: {BitConverter.ToString(data)}");

                try
                {
                    var writer = new DataWriter();
                    writer.WriteBytes(data);
                    var result = await writeChar.WriteValueWithResultAsync(
                        writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse).AsTask();
                    _output.WriteLine($"  Write: {result.Status}");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"  Write failed: {ex.GetType().Name}: {ex.Message}");
                }

                // Wait for response
                await Task.Delay(8_000);
                var newFrames = frames.Count - before;
                _output.WriteLine($"  New frames: {newFrames}");
            }

            // Summary
            _output.WriteLine("\n=== ALL GAIA RESPONSES ===");
            var sorted = frames.OrderBy(f => f.Time).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var f = sorted[i];
                _output.WriteLine($"  [{i:D3}] {f.Time:HH:mm:ss.fff} ({f.Data.Length}B) {BitConverter.ToString(f.Data)}");
                var ascii = TryDecodeAscii(f.Data);
                if (!string.IsNullOrWhiteSpace(ascii) && ascii.Length > 2)
                    _output.WriteLine($"       ASCII: \"{ascii}\"");
            }
            _output.WriteLine($"\nTotal: {sorted.Count} frames");
        }
        finally
        {
            fixture.Session.RawNotifyReceived -= OnSerialNotify;
            await notifyChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.None).AsTask();
            if (notifyChar4 is not null)
                await notifyChar4.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None).AsTask();
        }

        _output.WriteLine("\n=== DONE ===");
    }

    /// <summary>
    /// Focus on ae03 (the non-echo GAIA write char) + fresh EnterTransferMode
    /// after full state reset. Also subscribes to ALL channels.
    /// </summary>
    [SkippableFact]
    [Trait("Category", "WiFiDirectDiag")]
    public async Task DiagGaiaAe03_AndFreshTransferMode()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        var fixture = _shared.Inner;
        Skip.If(fixture is null, "Fixture not initialized");

        var session = fixture.Session;
        var bleDevice = session.DiagnosticBleDevice;
        Skip.If(bleDevice is null, "BLE device not connected");

        var allFrames = new System.Collections.Concurrent.ConcurrentBag<(DateTimeOffset T, string Src, byte[] D)>();

        // Subscribe to serial port notifications
        void OnSerial(object? s, byte[] d) =>
            allFrames.Add((DateTimeOffset.UtcNow, "de5bf729", (byte[])d.Clone()));
        session.RawNotifyReceived += OnSerial;

        // Subscribe to ae02, ae04, ae3c, 6e400003, fee3
        var extraSubs = new List<GattCharacteristic>();
        var charTargets = new[]
        {
            ("0000ae30-0000-1000-8000-00805f9b34fb", "0000ae02-0000-1000-8000-00805f9b34fb", "ae02"),
            ("0000ae30-0000-1000-8000-00805f9b34fb", "0000ae04-0000-1000-8000-00805f9b34fb", "ae04"),
            ("0000ae3a-0000-1000-8000-00805f9b34fb", "0000ae3c-0000-1000-8000-00805f9b34fb", "ae3c"),
            ("6e40fff0-b5a3-f393-e0a9-e50e24dcca9e", "6e400003-b5a3-f393-e0a9-e50e24dcca9e", "NUS"),
        };

        foreach (var (svcUuid, charUuid, label) in charTargets)
        {
            try
            {
                var svc = await bleDevice.GetGattServicesForUuidAsync(Guid.Parse(svcUuid)).AsTask();
                if (svc.Services.Count == 0) continue;
                var chars = await svc.Services[0].GetCharacteristicsForUuidAsync(Guid.Parse(charUuid)).AsTask();
                if (chars.Characteristics.Count == 0) continue;
                var ch = chars.Characteristics[0];
                var r = await ch.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask();
                if (r == GattCommunicationStatus.Success)
                {
                    var lbl = label;
                    ch.ValueChanged += (_, args) =>
                    {
                        var b = args.CharacteristicValue.ToArray();
                        allFrames.Add((DateTimeOffset.UtcNow, lbl, (byte[])b.Clone()));
                    };
                    extraSubs.Add(ch);
                    _output.WriteLine($"  Subscribed: {label}");
                }
            }
            catch { }
        }

        // Get ae03 write characteristic
        GattCharacteristic? ae03 = null;
        try
        {
            var svc = await bleDevice.GetGattServicesForUuidAsync(
                Guid.Parse("0000ae30-0000-1000-8000-00805f9b34fb")).AsTask();
            var chars = await svc.Services[0].GetCharacteristicsForUuidAsync(
                Guid.Parse("0000ae03-0000-1000-8000-00805f9b34fb")).AsTask();
            ae03 = chars.Characteristics.FirstOrDefault();
        }
        catch { }

        try
        {
            // ── Step 1: Reset state ──
            _output.WriteLine("\n=== Step 1: Reset state ===");
            await session.SendRawDiagnosticCommandAsync(new byte[] { 0x02, 0x01, 0x09 }, CancellationToken.None);
            _output.WriteLine("  ExitTransferMode sent");
            await Task.Delay(3_000);
            await session.SendRawDiagnosticCommandAsync(new byte[] { 0x02, 0x01, 0x0F }, CancellationToken.None);
            _output.WriteLine("  ResetP2p sent");
            await Task.Delay(3_000);

            // ── Step 2: Verify notifications with GetBattery ──
            _output.WriteLine("\n=== Step 2: GetBattery ===");
            allFrames.Clear();
            var battery = await session.GetBatteryAsync(CancellationToken.None);
            _output.WriteLine($"  Battery: {battery.Percentage}% charging={battery.IsCharging}");
            _output.WriteLine($"  Frames captured: {allFrames.Count}");
            await Task.Delay(2_000);
            _output.WriteLine($"  Frames after 2s: {allFrames.Count}");

            // ── Step 3: EnterTransferMode on de5bf72a ──
            _output.WriteLine("\n=== Step 3: EnterTransferMode on de5bf72a ===");
            var step3Start = allFrames.Count;
            await session.SendRawDiagnosticCommandAsync(new byte[] { 0x02, 0x01, 0x04 }, CancellationToken.None);
            _output.WriteLine("  Sent, waiting 20s...");
            for (int i = 0; i < 4; i++)
            {
                await Task.Delay(5_000);
                _output.WriteLine($"  +{(i+1)*5}s: {allFrames.Count - step3Start} new frames");
            }

            // ── Step 4: Try ae03 with GAIA commands ──
            if (ae03 is not null)
            {
                _output.WriteLine("\n=== Step 4: GAIA commands on ae03 ===");
                var gaiaCommands = new (string Name, byte[] Data)[]
                {
                    ("Raw {0x41, 0x04}", new byte[] { 0x41, 0x04 }),
                    ("GAIA V=001D C=0041 P=04", new byte[] { 0x00, 0x1D, 0x00, 0x41, 0x04 }),
                    ("Serial {02,01,04}", new byte[] { 0x02, 0x01, 0x04 }),
                };

                foreach (var (name, data) in gaiaCommands)
                {
                    var before = allFrames.Count;
                    _output.WriteLine($"\n  [{name}] Bytes: {BitConverter.ToString(data)}");
                    try
                    {
                        var writer = new DataWriter();
                        writer.WriteBytes(data);
                        var result = await ae03.WriteValueWithResultAsync(
                            writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse).AsTask();
                        _output.WriteLine($"  Write: {result.Status}");
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"  Write failed: {ex.Message}");
                    }
                    await Task.Delay(8_000);
                    _output.WriteLine($"  New frames: {allFrames.Count - before}");
                }
            }

            // ── Summary ──
            _output.WriteLine("\n=== ALL FRAMES ===");
            var sorted = allFrames.OrderBy(f => f.T).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var f = sorted[i];
                _output.WriteLine($"  [{i:D3}] {f.T:HH:mm:ss.fff} [{f.Src}] ({f.D.Length}B) {BitConverter.ToString(f.D)}");
                var ascii = TryDecodeAscii(f.D);
                if (!string.IsNullOrWhiteSpace(ascii) && ascii.Length > 3)
                    _output.WriteLine($"       ASCII: \"{ascii}\"");
            }
            _output.WriteLine($"\nTotal: {sorted.Count}");

            // Check for SSID-like data
            foreach (var f in sorted)
            {
                var a = Encoding.ASCII.GetString(f.D);
                if (a.Contains("DIRECT", StringComparison.OrdinalIgnoreCase) ||
                    a.Contains("M01", StringComparison.OrdinalIgnoreCase) ||
                    a.Contains("123456789"))
                    _output.WriteLine($"  *** SSID CANDIDATE [{f.Src}]: \"{a}\" ***");
            }

            // Cleanup
            _output.WriteLine("\n=== Cleanup ===");
            await session.SendRawDiagnosticCommandAsync(new byte[] { 0x02, 0x01, 0x09 }, CancellationToken.None);
            await Task.Delay(2_000);
        }
        finally
        {
            session.RawNotifyReceived -= OnSerial;
            foreach (var ch in extraSubs)
            {
                try { await ch.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None).AsTask(); } catch { }
            }
        }
        _output.WriteLine("=== DONE ===");
    }

    // ──────────────────────────────────────────────────────────────
    // RCA-805  openWifiWithMode protocol investigation
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// RCA-805 Test 1: Send EnterTransferMode ({0x02,0x01,0x04}) and capture
    /// ALL notifications for 90 seconds. The iOS SDK's openWifiWithMode: may
    /// trigger a delayed notification containing WiFi SSID/password that we've
    /// been missing because our code only waits for 0x01 and 0x08 types.
    /// </summary>
    [SkippableFact]
    public async Task Rca805_ExtendedNotifyCapture_AfterEnterTransferMode()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;
        var session = fixture.Session;
        var allNotifications = new List<(TimeSpan elapsed, byte[] data)>();
        var sw = Stopwatch.StartNew();

        session.RawNotifyReceived += OnNotify;
        void OnNotify(object? s, byte[] data)
        {
            allNotifications.Add((sw.Elapsed, data));
            _output.WriteLine($"[{sw.Elapsed:mm\\:ss\\.fff}] NOTIFY ({data.Length}B): {BitConverter.ToString(data)}");
            if (data.Length > 11)
            {
                TryParseWifiInfo(data);
            }
        }

        try
        {
            _output.WriteLine("=== RCA-805 Test 1: Extended Notification Capture ===");
            _output.WriteLine("Sending {0x02,0x01,0x04} and listening for ALL notifications for 90s...\n");

            await session.SendRawDiagnosticCommandAsync(
                new byte[] { 0x02, 0x01, 0x04 }, CancellationToken.None);

            _output.WriteLine("Command sent. Waiting 90 seconds for all notifications...\n");
            await Task.Delay(90_000);

            _output.WriteLine($"\n=== SUMMARY: Received {allNotifications.Count} notifications ===");
            foreach (var (elapsed, data) in allNotifications)
            {
                var type = data.Length >= 7 ? $"type=0x{data[6]:X2}" : "short";
                _output.WriteLine($"  [{elapsed:mm\\:ss\\.fff}] {type} ({data.Length}B)");
            }
        }
        finally
        {
            session.RawNotifyReceived -= OnNotify;
            // Exit transfer mode
            try { await session.SendRawDiagnosticCommandAsync(
                new byte[] { 0x02, 0x01, 0x09 }, CancellationToken.None); } catch { }
            await Task.Delay(2_000);
        }
        _output.WriteLine("=== DONE ===");
    }

    /// <summary>
    /// RCA-805 Test 2: Try alternative deviceType bytes in the command.
    /// setDeviceMode uses {0x02, 0x01, mode}. openWifiWithMode may use
    /// {0x02, X, mode} with a different X value. Try several candidates.
    /// </summary>
    [SkippableFact]
    public async Task Rca805_AlternativeDeviceTypeBytes_ForOpenWifi()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;
        var session = fixture.Session;

        _output.WriteLine("=== RCA-805 Test 2: Alternative deviceType Bytes ===");
        _output.WriteLine("Trying {0x02, X, 0x04} for various X values...\n");

        // deviceType candidates: skip 0x01 (already known = setDeviceMode)
        byte[] candidates = { 0x02, 0x03, 0x05, 0x07, 0x08, 0x0A, 0x0B, 0x10, 0x40, 0x41 };

        foreach (var dt in candidates)
        {
            var notifications = new List<byte[]>();
            session.RawNotifyReceived += OnNotify;
            void OnNotify(object? s, byte[] data) => notifications.Add(data);

            var cmd = new byte[] { 0x02, dt, 0x04 };
            _output.WriteLine($"--- Sending {{{string.Join(",", cmd.Select(b => $"0x{b:X2}"))}}} ---");

            try
            {
                await session.SendRawDiagnosticCommandAsync(cmd, CancellationToken.None);
                await Task.Delay(8_000);

                if (notifications.Count == 0)
                {
                    _output.WriteLine("  No response.\n");
                }
                else
                {
                    foreach (var data in notifications)
                    {
                        _output.WriteLine($"  Response ({data.Length}B): {BitConverter.ToString(data)}");
                        if (data.Length > 11) TryParseWifiInfo(data);
                    }
                    _output.WriteLine("");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  Error: {ex.Message}");
            }
            finally
            {
                session.RawNotifyReceived -= OnNotify;
            }
        }

        // Reset to known good state
        try { await session.SendRawDiagnosticCommandAsync(
            new byte[] { 0x02, 0x01, 0x0b }, CancellationToken.None); } catch { }
        await Task.Delay(2_000);
        _output.WriteLine("=== DONE ===");
    }

    /// <summary>
    /// RCA-805 Test 3: Try ODM_DFU_Operation opcodes directly.
    /// The iOS QCSDK uses opcodes like 0x41 (SetDeviceMode). Try sending
    /// these on the serial port characteristic, with and without framing.
    /// </summary>
    [SkippableFact]
    public async Task Rca805_OdmDfuOpcodes_SetDeviceMode()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;
        var session = fixture.Session;

        _output.WriteLine("=== RCA-805 Test 3: ODM DFU Opcodes ===");
        _output.WriteLine("Trying ODM_DFU_Operation_SetDeviceMode (0x41) variants...\n");

        byte[][] commands =
        {
            new byte[] { 0x41, 0x04 },                         // Raw opcode + mode
            new byte[] { 0x41, 0x01, 0x04 },                   // Opcode + deviceType + mode
            new byte[] { 0x41, 0x00, 0x01, 0x04 },             // Opcode + length(1) + mode
            new byte[] { 0x00, 0x01, 0x41, 0x04 },             // Length(1) + opcode + mode
            new byte[] { 0x00, 0x02, 0x41, 0x01, 0x04 },       // Length(2) + opcode + deviceType + mode
            new byte[] { 0x40, 0x04 },                         // SetupDeviceStatus + mode
            new byte[] { 0x40, 0x01, 0x04 },                   // SetupDeviceStatus + deviceType + mode
        };

        foreach (var cmd in commands)
        {
            var notifications = new List<byte[]>();
            session.RawNotifyReceived += OnNotify;
            void OnNotify(object? s, byte[] data) => notifications.Add(data);

            _output.WriteLine($"--- Sending {{{string.Join(",", cmd.Select(b => $"0x{b:X2}"))}}} ---");

            try
            {
                await session.SendRawDiagnosticCommandAsync(cmd, CancellationToken.None);
                await Task.Delay(5_000);

                if (notifications.Count == 0)
                    _output.WriteLine("  No response.\n");
                else
                {
                    foreach (var data in notifications)
                    {
                        _output.WriteLine($"  Response ({data.Length}B): {BitConverter.ToString(data)}");
                        if (data.Length > 7)
                            _output.WriteLine($"  ASCII: {TryDecodeAscii(data.AsSpan(7))}");
                    }
                    _output.WriteLine("");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  Error: {ex.Message}\n");
            }
            finally
            {
                session.RawNotifyReceived -= OnNotify;
            }
        }
        _output.WriteLine("=== DONE ===");
    }

    /// <summary>
    /// RCA-805 Test 4: Send commands via the Nordic UART Service (6e40fff0)
    /// instead of the serial port (de5bf728). The iOS QCSDK binary references
    /// this service — it might be the actual OdmCmd transport.
    /// </summary>
    [SkippableFact]
    public async Task Rca805_NordicUartService_TryOdmCommands()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;
        var session = fixture.Session;
        var bleDevice = session.DiagnosticBleDevice;

        _output.WriteLine("=== RCA-805 Test 4: Nordic UART Service Commands ===");

        // NUS UUIDs
        var nusServiceUuid = new Guid("6e40fff0-b5a3-f393-e0a9-e50e24dcca9e");
        var nusWriteUuid = new Guid("6e400002-b5a3-f393-e0a9-e50e24dcca9e");
        var nusNotifyUuid = new Guid("6e400003-b5a3-f393-e0a9-e50e24dcca9e");

        var svcResult = await bleDevice.GetGattServicesForUuidAsync(nusServiceUuid).AsTask();
        if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
        {
            _output.WriteLine("NUS service (6e40fff0) NOT FOUND on this device.");
            return;
        }

        var svc = svcResult.Services[0];
        _output.WriteLine($"NUS service found. Getting characteristics...");

        var writeChars = await svc.GetCharacteristicsForUuidAsync(nusWriteUuid).AsTask();
        var notifyChars = await svc.GetCharacteristicsForUuidAsync(nusNotifyUuid).AsTask();

        if (writeChars.Status != GattCommunicationStatus.Success || writeChars.Characteristics.Count == 0)
        {
            _output.WriteLine("NUS write characteristic (6e400002) NOT FOUND.");
            return;
        }
        if (notifyChars.Status != GattCommunicationStatus.Success || notifyChars.Characteristics.Count == 0)
        {
            _output.WriteLine("NUS notify characteristic (6e400003) NOT FOUND.");
            return;
        }

        var writeChar = writeChars.Characteristics[0];
        var notifyChar = notifyChars.Characteristics[0];

        // Subscribe to NUS notifications
        var nusNotifications = new List<byte[]>();
        notifyChar.ValueChanged += (s, e) =>
        {
            var reader = DataReader.FromBuffer(e.CharacteristicValue);
            var bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            nusNotifications.Add(bytes);
            _output.WriteLine($"  [NUS NOTIFY] ({bytes.Length}B): {BitConverter.ToString(bytes)}");
            var ascii = TryDecodeAscii(bytes);
            if (!string.IsNullOrEmpty(ascii))
                _output.WriteLine($"  [NUS ASCII]: {ascii}");
        };

        await notifyChar.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask();

        _output.WriteLine("NUS notifications subscribed. Sending commands...\n");

        // Try various commands on NUS
        byte[][] commands =
        {
            new byte[] { 0x41, 0x04 },                   // ODM SetDeviceMode Transfer
            new byte[] { 0x41, 0x01, 0x04 },             // ODM SetDeviceMode + deviceType + Transfer
            new byte[] { 0x02, 0x01, 0x04 },             // Serial protocol command on NUS
            new byte[] { 0x42 },                          // ODM GetDeviceBattery (quick test)
        };

        foreach (var cmd in commands)
        {
            _output.WriteLine($"--- NUS Write: {{{string.Join(",", cmd.Select(b => $"0x{b:X2}"))}}} ---");
            try
            {
                var writer = new DataWriter();
                writer.WriteBytes(cmd);
                var writeResult = await writeChar.WriteValueAsync(
                    writer.DetachBuffer(),
                    GattWriteOption.WriteWithResponse).AsTask();
                _output.WriteLine($"  Write result: {writeResult}");
                await Task.Delay(5_000);

                if (nusNotifications.Count == 0)
                    _output.WriteLine("  No NUS response.\n");
                else
                    _output.WriteLine($"  Got {nusNotifications.Count} NUS notification(s).\n");

                nusNotifications.Clear();
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  Error: {ex.Message}");
                // Try WriteWithoutResponse as fallback
                try
                {
                    var writer = new DataWriter();
                    writer.WriteBytes(cmd);
                    var writeResult = await writeChar.WriteValueAsync(
                        writer.DetachBuffer(),
                        GattWriteOption.WriteWithoutResponse).AsTask();
                    _output.WriteLine($"  WriteWithoutResponse result: {writeResult}");
                    await Task.Delay(5_000);
                    _output.WriteLine($"  Got {nusNotifications.Count} NUS notification(s).\n");
                    nusNotifications.Clear();
                }
                catch (Exception ex2)
                {
                    _output.WriteLine($"  WriteWithoutResponse also failed: {ex2.Message}\n");
                }
            }
        }

        // Unsubscribe
        try { await notifyChar.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.None).AsTask(); } catch { }

        _output.WriteLine("=== DONE ===");
    }

    /// <summary>
    /// RCA-806 Phase C: Now that BLE notifications are confirmed working,
    /// try multiple openWifiWithMode command format hypotheses.
    /// The iOS SDK uses opcode 0x41 (SetDeviceMode) but openWifiWithMode
    /// likely has an additional byte or different payload structure.
    /// Captures all notify responses and looks for SSID/password.
    /// </summary>
    [SkippableFact]
    [Trait("Category", "WiFiDirectDiag")]
    public async Task Rca806_PhaseC_OpenWifiWithMode_FindCorrectFormat()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;
        var session = fixture.Session;
        var allFrames = new System.Collections.Concurrent.ConcurrentBag<(DateTimeOffset Time, byte[] Data, string Phase)>();

        void OnNotify(object? sender, byte[] data)
        {
            var copy = data.ToArray();
            var phase = "?";
            allFrames.Add((DateTimeOffset.UtcNow, copy, phase));
            _output.WriteLine($"  [NOTIFY] ({copy.Length}B) {BitConverter.ToString(copy)}");
            if (copy.Length > 7)
            {
                var ascii = TryDecodeAscii(copy.AsSpan(7));
                if (!string.IsNullOrWhiteSpace(ascii) && ascii.Length > 2)
                    _output.WriteLine($"    ASCII [7..]: \"{ascii}\"");
            }
            // Try to parse WiFi info at various offsets
            TryParseWifiInfo(copy);
        }

        session.RawNotifyReceived += OnNotify;
        try
        {
            _output.WriteLine("=== RCA-806 Phase C: Find openWifiWithMode command format ===");
            _output.WriteLine($"Session state: {session.State}\n");

            // CRITICAL: Send ResetP2p first — glasses won't respond to mode commands without this
            _output.WriteLine("[Reset] Sending ResetP2p {0x02, 0x01, 0x0F}...");
            await session.SendRawDiagnosticCommandAsync(new byte[] { 0x02, 0x01, 0x0F }, CancellationToken.None);
            _output.WriteLine("[Reset] Done. Waiting 3s...");
            await Task.Delay(3_000);

            // Verify with known-working command: {0x41, 0x04} should produce a notification
            _output.WriteLine("\n[Baseline] Sending {0x41, 0x04} (known to produce ACK notification)...");
            await session.SendRawDiagnosticCommandAsync(new byte[] { 0x41, 0x04 }, CancellationToken.None);
            _output.WriteLine("[Baseline] Waiting 10s for notification...");
            await Task.Delay(10_000);
            _output.WriteLine($"[Baseline] Frames: {allFrames.Count}");
            if (allFrames.Count == 0)
            {
                _output.WriteLine("[Baseline] WARNING: No notification from known-working command!");
                _output.WriteLine("[Baseline] Glasses may need power cycle. Continuing anyway...\n");
            }
            else
            {
                _output.WriteLine("[Baseline] Notifications confirmed working!\n");
            }

            // Reset again before trying hypotheses
            _output.WriteLine("[Reset] Sending ResetP2p before hypotheses...");
            await session.SendRawDiagnosticCommandAsync(new byte[] { 0x02, 0x01, 0x0F }, CancellationToken.None);
            await Task.Delay(3_000);

            // ── Command hypotheses for openWifiWithMode:Transfer ──
            // The iOS SDK has operateDeviceType:mode: which suggests a 3-byte format
            var commands = new (string Name, byte[] Data)[]
            {
                // Most likely: different deviceType byte
                ("H1: {0x41, 0x02, 0x04} opcode+type=wifi+mode=transfer", new byte[] { 0x41, 0x02, 0x04 }),
                ("H2: {0x41, 0x01, 0x04} opcode+type=1+mode=transfer", new byte[] { 0x41, 0x01, 0x04 }),
                // Mode + extra flag variants
                ("H3: {0x41, 0x04, 0x01} opcode+mode+wifiFlag=1", new byte[] { 0x41, 0x04, 0x01 }),
                ("H4: {0x41, 0x04, 0x02} opcode+mode+wifiFlag=2", new byte[] { 0x41, 0x04, 0x02 }),
                // Serial port style sub-commands
                ("H5: {0x02, 0x02, 0x04} category+subCmd=openWifi+mode", new byte[] { 0x02, 0x02, 0x04 }),
                ("H6: {0x02, 0x03, 0x04} category+subCmd=3+mode", new byte[] { 0x02, 0x03, 0x04 }),
                // Try adjacent opcodes (getDeviceWifiIP might be 0x41+0x05 or 0x41+0x06)
                ("H7: {0x41, 0x05} opcode=getIP?", new byte[] { 0x41, 0x05 }),
                ("H8: {0x41, 0x06} opcode=getWifiInfo?", new byte[] { 0x41, 0x06 }),
            };

            for (int i = 0; i < commands.Length; i++)
            {
                var (name, data) = commands[i];
                var before = allFrames.Count;

                // ResetP2p before each hypothesis (glasses only respond once per reset)
                await session.SendRawDiagnosticCommandAsync(new byte[] { 0x02, 0x01, 0x0F }, CancellationToken.None);
                await Task.Delay(2_000);

                _output.WriteLine($"\n[{i + 1}/{commands.Length}] {name}");
                _output.WriteLine($"  Sending: {BitConverter.ToString(data)}");

                try
                {
                    await session.SendRawDiagnosticCommandAsync(data, CancellationToken.None);
                    _output.WriteLine("  Write: Success");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"  Write FAILED: {ex.GetType().Name}: {ex.Message}");
                }

                // Wait for response (10s to allow for WiFi startup delay)
                await Task.Delay(10_000);
                var newFrames = allFrames.Count - before;
                _output.WriteLine($"  New frames: {newFrames}");

                // If we got a long response (>20 bytes), it might contain SSID
                var recent = allFrames.Where(f => f.Time > DateTimeOffset.UtcNow.AddSeconds(-12))
                    .OrderByDescending(f => f.Data.Length).ToList();
                if (recent.Any(f => f.Data.Length > 20))
                {
                    _output.WriteLine("  *** LONG RESPONSE DETECTED — possible WiFi credentials! ***");
                    foreach (var f in recent.Where(ff => ff.Data.Length > 20))
                    {
                        _output.WriteLine($"  >>> ({f.Data.Length}B): {BitConverter.ToString(f.Data)}");
                        var ascii = TryDecodeAscii(f.Data);
                        _output.WriteLine($"  >>> ASCII: \"{ascii}\"");
                    }
                }
            }

            // Also try: send EnterTransferMode FIRST, then query for WiFi info
            _output.WriteLine("\n\n=== PHASE 2: EnterTransferMode then query WiFi info ===");
            _output.WriteLine("Sending EnterTransferMode {0x02, 0x01, 0x04}...");
            await session.SendRawDiagnosticCommandAsync(new byte[] { 0x02, 0x01, 0x04 }, CancellationToken.None);
            _output.WriteLine("Waiting 5s for mode change...");
            await Task.Delay(5_000);

            // Now try querying WiFi info while in transfer mode
            var phase2Commands = new (string Name, byte[] Data)[]
            {
                ("P2-1: {0x41, 0x06} query mode/wifi after transfer", new byte[] { 0x41, 0x06 }),
                ("P2-2: {0x41, 0x04} setDeviceMode while already in transfer", new byte[] { 0x41, 0x04 }),
                ("P2-3: {0x02, 0x02, 0x04} openWifi serial style", new byte[] { 0x02, 0x02, 0x04 }),
                ("P2-4: {0x02, 0x05, 0x04} getWifiIP serial style", new byte[] { 0x02, 0x05, 0x04 }),
                ("P2-5: {0x02, 0x06, 0x04} wifi query serial", new byte[] { 0x02, 0x06, 0x04 }),
            };

            for (int i = 0; i < phase2Commands.Length; i++)
            {
                var (name, data) = phase2Commands[i];
                var before = allFrames.Count;

                _output.WriteLine($"\n[P2-{i + 1}] {name}");
                _output.WriteLine($"  Sending: {BitConverter.ToString(data)}");
                try
                {
                    await session.SendRawDiagnosticCommandAsync(data, CancellationToken.None);
                    _output.WriteLine("  Write: Success");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"  Write FAILED: {ex.Message}");
                }
                await Task.Delay(7_000);
                _output.WriteLine($"  New frames: {allFrames.Count - before}");
            }

            // Summary
            _output.WriteLine("\n\n=== ALL CAPTURED FRAMES ===");
            var sorted = allFrames.OrderBy(f => f.Time).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var f = sorted[i];
                _output.WriteLine($"  [{i:D3}] {f.Time:HH:mm:ss.fff} ({f.Data.Length}B) {BitConverter.ToString(f.Data)}");
                if (f.Data.Length > 4)
                {
                    var ascii = TryDecodeAscii(f.Data);
                    if (!string.IsNullOrWhiteSpace(ascii) && ascii.Length > 2)
                        _output.WriteLine($"       ASCII: \"{ascii}\"");
                }
            }
            _output.WriteLine($"\nTotal frames: {sorted.Count}");

            // Check for SSID-like content
            _output.WriteLine("\n=== SSID/Password search ===");
            foreach (var f in sorted)
            {
                var ascii = Encoding.ASCII.GetString(f.Data);
                if (ascii.Contains("DIRECT", StringComparison.OrdinalIgnoreCase) ||
                    ascii.Contains("M01", StringComparison.OrdinalIgnoreCase) ||
                    ascii.Contains("123456789", StringComparison.Ordinal) ||
                    ascii.Contains("192.168", StringComparison.Ordinal) ||
                    f.Data.Length > 25)
                {
                    _output.WriteLine($"  CANDIDATE ({f.Data.Length}B): {BitConverter.ToString(f.Data)}");
                    _output.WriteLine($"    ASCII: \"{ascii}\"");
                }
            }

            // Exit transfer mode
            _output.WriteLine("\n=== Cleanup: ExitTransferMode ===");
            await session.SendRawDiagnosticCommandAsync(new byte[] { 0x02, 0x01, 0x09 }, CancellationToken.None);
            await Task.Delay(2_000);
        }
        finally
        {
            session.RawNotifyReceived -= OnNotify;
        }

        _output.WriteLine("\n=== DONE ===");
    }

    /// <summary>
    /// RCA-805 Test 5: After entering transfer mode, scan for WiFi networks
    /// that match the glasses pattern and try connecting with default password.
    /// </summary>
    [SkippableFact]
    public async Task Rca805_WifiHotspotScan_AfterTransferMode()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;
        var session = fixture.Session;

        _output.WriteLine("=== RCA-805 Test 5: WiFi Hotspot Scan ===");
        _output.WriteLine("Enter transfer mode, then scan for glasses hotspot networks.\n");

        // Enter transfer mode
        _output.WriteLine("Sending EnterTransferMode {0x02,0x01,0x04}...");
        await session.SendRawDiagnosticCommandAsync(
            new byte[] { 0x02, 0x01, 0x04 }, CancellationToken.None);
        _output.WriteLine("Waiting 10s for glasses WiFi to start...");
        await Task.Delay(10_000);

        // Scan for WiFi networks
        _output.WriteLine("\nScanning for WiFi networks...");
        try
        {
            var wifiAdapterResults = await WiFiAdapter.FindAllAdaptersAsync().AsTask();
            if (wifiAdapterResults.Count == 0)
            {
                _output.WriteLine("No WiFi adapters found!");
            }
            else
            {
                var adapter = wifiAdapterResults[0];
                await adapter.ScanAsync().AsTask();
                var networks = adapter.NetworkReport.AvailableNetworks;

                _output.WriteLine($"Found {networks.Count} WiFi networks:");
                var glassesNetworks = new List<WiFiAvailableNetwork>();
                foreach (var net in networks.OrderByDescending(n => n.NetworkRssiInDecibelMilliwatts))
                {
                    var ssid = net.Ssid;
                    var isCandidate = ssid.Contains("M01", StringComparison.OrdinalIgnoreCase) ||
                                     ssid.Contains("DIRECT", StringComparison.OrdinalIgnoreCase) ||
                                     ssid.Contains("HeyCyan", StringComparison.OrdinalIgnoreCase) ||
                                     ssid.Contains("E6C9", StringComparison.OrdinalIgnoreCase) ||
                                     ssid.Contains("Glasses", StringComparison.OrdinalIgnoreCase);
                    var marker = isCandidate ? " <<<< CANDIDATE" : "";
                    _output.WriteLine($"  [{net.NetworkRssiInDecibelMilliwatts}dBm] " +
                        $"SSID=\"{ssid}\" Security={net.SecuritySettings.NetworkAuthenticationType}{marker}");
                    if (isCandidate) glassesNetworks.Add(net);
                }

                if (glassesNetworks.Count > 0)
                {
                    _output.WriteLine($"\n=== Found {glassesNetworks.Count} candidate network(s)! ===");
                    // Don't auto-connect — just report. Manual testing can be done.
                }
                else
                {
                    _output.WriteLine("\nNo candidate glasses networks found.");
                    _output.WriteLine("The glasses may not have started their hotspot yet,");
                    _output.WriteLine("or may need the openWifiWithMode command first.");
                }
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"WiFi scan failed: {ex.Message}");
        }

        // Second scan after longer wait
        _output.WriteLine("\nWaiting 20 more seconds and scanning again...");
        await Task.Delay(20_000);
        try
        {
            var wifiAdapterResults = await WiFiAdapter.FindAllAdaptersAsync().AsTask();
            if (wifiAdapterResults.Count > 0)
            {
                var adapter = wifiAdapterResults[0];
                await adapter.ScanAsync().AsTask();
                var networks = adapter.NetworkReport.AvailableNetworks;
                _output.WriteLine($"Second scan: {networks.Count} networks");
                foreach (var net in networks.OrderByDescending(n => n.NetworkRssiInDecibelMilliwatts))
                {
                    var ssid = net.Ssid;
                    var isCandidate = ssid.Contains("M01", StringComparison.OrdinalIgnoreCase) ||
                                     ssid.Contains("DIRECT", StringComparison.OrdinalIgnoreCase) ||
                                     ssid.Contains("HeyCyan", StringComparison.OrdinalIgnoreCase) ||
                                     ssid.Contains("E6C9", StringComparison.OrdinalIgnoreCase) ||
                                     ssid.Contains("Glasses", StringComparison.OrdinalIgnoreCase);
                    if (isCandidate)
                        _output.WriteLine($"  <<<< [{net.NetworkRssiInDecibelMilliwatts}dBm] " +
                            $"SSID=\"{ssid}\" Security={net.SecuritySettings.NetworkAuthenticationType}");
                }
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Second WiFi scan failed: {ex.Message}");
        }

        // Exit transfer mode
        try { await session.SendRawDiagnosticCommandAsync(
            new byte[] { 0x02, 0x01, 0x09 }, CancellationToken.None); } catch { }
        await Task.Delay(2_000);

        _output.WriteLine("\n=== DONE ===");
    }

    /// <summary>
    /// Try to parse WiFi SSID/password from a BLE notification frame.
    /// Based on QCSDK binary analysis (RCA-805): the response contains
    /// [nameLen 1B][nameData...][psLen 1B][psData...] at some offset.
    /// </summary>
    private void TryParseWifiInfo(byte[] data)
    {
        // Try parsing at various offsets after the 7-byte header
        for (int startOffset = 7; startOffset < data.Length - 2; startOffset++)
        {
            var nameLen = data[startOffset];
            if (nameLen == 0 || nameLen > 64) continue;
            if (startOffset + 1 + nameLen >= data.Length) continue;

            // Try to decode nameData as ASCII
            var nameEnd = startOffset + 1 + nameLen;
            var nameSpan = data.AsSpan(startOffset + 1, nameLen);
            if (!IsAsciiPrintable(nameSpan)) continue;

            var name = Encoding.ASCII.GetString(nameSpan);

            // Check for psLen + psData after nameData
            if (nameEnd < data.Length)
            {
                var psLen = data[nameEnd];
                if (psLen > 0 && psLen <= 64 && nameEnd + 1 + psLen <= data.Length)
                {
                    var psSpan = data.AsSpan(nameEnd + 1, psLen);
                    if (IsAsciiPrintable(psSpan))
                    {
                        var password = Encoding.ASCII.GetString(psSpan);
                        _output.WriteLine($"  *** WIFI INFO CANDIDATE (offset {startOffset}): " +
                            $"SSID=\"{name}\" Password=\"{password}\" ***");
                    }
                }
            }

            // Even without password, report a potential SSID
            if (name.Length >= 3)
            {
                _output.WriteLine($"  Potential SSID at offset {startOffset}: \"{name}\"");
            }
        }

        // Also try parsing IP at various offsets
        for (int i = 7; i + 3 < data.Length; i++)
        {
            // Look for valid IP patterns (192.168.x.x, 10.x.x.x, 172.16-31.x.x)
            if ((data[i] == 192 && data[i + 1] == 168) ||
                (data[i] == 10) ||
                (data[i] == 172 && data[i + 1] >= 16 && data[i + 1] <= 31))
            {
                var ip = $"{data[i]}.{data[i + 1]}.{data[i + 2]}.{data[i + 3]}";
                _output.WriteLine($"  Potential IP at offset {i}: {ip}");
            }
        }
    }

    /// <summary>
    /// RCA-806 Phase A: Full WiFi hotspot discovery and HTTP file transfer test.
    /// 1. Send EnterTransferMode via BLE
    /// 2. Scan WiFi for glasses hotspot (M01*, DIRECT*, *E6C9*, HeyCyan*)
    /// 3. Connect with known password "123456789"
    /// 4. Probe candidate IPs on port 8080 for HTTP response
    /// 5. Try /filelist endpoint to verify transfer works
    /// 6. Disconnect WiFi, exit transfer mode
    /// </summary>
    [SkippableFact]
    [Trait("Category", "WiFiDirectDiag")]
    public async Task Rca806_PhaseA_WifiHotspotConnect_AndHttpProbe()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_shared.Inner is null, "Fixture not initialized");

        var fixture = _shared.Inner!;
        var session = fixture.Session;

        _output.WriteLine("=== RCA-806 Phase A: WiFi Hotspot Discovery + HTTP Transfer ===\n");

        // Step 1: Send EnterTransferMode
        _output.WriteLine("[Step 1] Sending EnterTransferMode {0x02,0x01,0x04}...");
        await session.SendRawDiagnosticCommandAsync(
            new byte[] { 0x02, 0x01, 0x04 }, CancellationToken.None);
        _output.WriteLine("[Step 1] Command sent. Waiting 12s for glasses to start WiFi hotspot...");
        await Task.Delay(12_000);

        // Step 2: Scan WiFi networks
        _output.WriteLine("\n[Step 2] Scanning WiFi networks...");
        var access = await WiFiAdapter.RequestAccessAsync();
        if (access != WiFiAccessStatus.Allowed)
        {
            _output.WriteLine($"  WiFi access denied: {access}");
            await ExitTransferMode(session);
            return;
        }

        var adapters = await WiFiAdapter.FindAllAdaptersAsync();
        if (adapters.Count == 0)
        {
            _output.WriteLine("  No WiFi adapters found!");
            await ExitTransferMode(session);
            return;
        }
        var adapter = adapters[0];

        // Multiple scan passes (hotspot may take time to appear)
        WiFiAvailableNetwork? bestCandidate = null;
        for (int pass = 1; pass <= 4; pass++)
        {
            _output.WriteLine($"\n  Scan pass {pass}/4...");
            await adapter.ScanAsync();
            var networks = adapter.NetworkReport.AvailableNetworks;

            foreach (var net in networks.OrderByDescending(n => n.NetworkRssiInDecibelMilliwatts))
            {
                var ssid = net.Ssid;
                if (string.IsNullOrEmpty(ssid)) continue;

                var isCandidate = ssid.Contains("M01", StringComparison.OrdinalIgnoreCase) ||
                                  ssid.Contains("DIRECT", StringComparison.OrdinalIgnoreCase) ||
                                  ssid.Contains("HeyCyan", StringComparison.OrdinalIgnoreCase) ||
                                  ssid.Contains("E6C9", StringComparison.OrdinalIgnoreCase) ||
                                  ssid.Contains("Glasses", StringComparison.OrdinalIgnoreCase) ||
                                  ssid.Contains("QC_", StringComparison.OrdinalIgnoreCase);

                if (isCandidate)
                {
                    _output.WriteLine($"  *** CANDIDATE: SSID=\"{ssid}\" signal={net.NetworkRssiInDecibelMilliwatts}dBm " +
                        $"auth={net.SecuritySettings.NetworkAuthenticationType}");
                    if (bestCandidate is null || net.NetworkRssiInDecibelMilliwatts > bestCandidate.NetworkRssiInDecibelMilliwatts)
                        bestCandidate = net;
                }
            }

            if (bestCandidate is not null) break;

            if (pass < 4)
            {
                _output.WriteLine($"  No candidates yet, waiting 8s before next scan...");
                await Task.Delay(8_000);
            }
        }

        if (bestCandidate is null)
        {
            _output.WriteLine("\n  No candidate glasses networks found after 4 scan passes.");
            _output.WriteLine("  Falling back to brute-force IP probing on current network...\n");

            // Even without finding a hotspot SSID, probe known IPs (maybe already connected via WiFi Direct)
            await ProbeHttpEndpoints(_output);
            await ExitTransferMode(session);
            return;
        }

        // Step 3: Connect to candidate network with password "123456789"
        _output.WriteLine($"\n[Step 3] Connecting to SSID=\"{bestCandidate.Ssid}\" with password 123456789...");
        var credential = new Windows.Security.Credentials.PasswordCredential
        {
            Password = "123456789"
        };
        var connectResult = await adapter.ConnectAsync(bestCandidate,
            WiFiReconnectionKind.Manual, credential);

        _output.WriteLine($"  Connection result: {connectResult.ConnectionStatus}");
        if (connectResult.ConnectionStatus != WiFiConnectionStatus.Success)
        {
            _output.WriteLine($"  Failed to connect! Trying without password...");
            connectResult = await adapter.ConnectAsync(bestCandidate, WiFiReconnectionKind.Manual);
            _output.WriteLine($"  Open connect result: {connectResult.ConnectionStatus}");

            if (connectResult.ConnectionStatus != WiFiConnectionStatus.Success)
            {
                _output.WriteLine("  Connection failed. Exiting.");
                await ExitTransferMode(session);
                return;
            }
        }

        _output.WriteLine("  Connected! Waiting 3s for IP assignment...");
        await Task.Delay(3_000);

        // Step 4: Probe candidate IPs on port 8080
        _output.WriteLine("\n[Step 4] Probing candidate IPs on port 8080...");
        var baseUrl = await ProbeHttpEndpoints(_output);

        // Step 5: If we found the server, try /filelist
        if (baseUrl is not null)
        {
            _output.WriteLine($"\n[Step 5] Server found at {baseUrl}! Trying /filelist...");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            try
            {
                var filelistResponse = await http.GetStringAsync($"{baseUrl}/filelist");
                _output.WriteLine($"  /filelist response ({filelistResponse.Length} chars):");
                // Print first 2000 chars
                _output.WriteLine(filelistResponse.Length > 2000
                    ? filelistResponse[..2000] + "\n  ... (truncated)"
                    : filelistResponse);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  /filelist failed: {ex.GetType().Name}: {ex.Message}");

                // Try alternative endpoints
                var endpoints = new[] { "/files/media.config", "/", "/download" };
                foreach (var ep in endpoints)
                {
                    try
                    {
                        var resp = await http.GetAsync($"{baseUrl}{ep}");
                        var body = await resp.Content.ReadAsStringAsync();
                        _output.WriteLine($"  {ep}: HTTP {(int)resp.StatusCode} ({body.Length} chars)");
                        if (body.Length > 0 && body.Length < 500)
                            _output.WriteLine($"    Body: {body}");
                    }
                    catch (Exception ex2)
                    {
                        _output.WriteLine($"  {ep}: {ex2.GetType().Name}");
                    }
                }
            }
        }
        else
        {
            _output.WriteLine("\n[Step 5] No HTTP server found on any candidate IP.");
        }

        // Step 6: Disconnect WiFi + exit transfer mode
        _output.WriteLine("\n[Step 6] Disconnecting WiFi...");
        adapter.Disconnect();
        _output.WriteLine("  WiFi disconnected.");

        await ExitTransferMode(session);
        _output.WriteLine("\n=== RCA-806 Phase A COMPLETE ===");
    }

    private async Task ExitTransferMode(WindowsHeyCyanGlassesSession session)
    {
        _output.WriteLine("\n  Sending ExitTransferMode {0x02,0x01,0x09}...");
        try
        {
            await session.SendRawDiagnosticCommandAsync(
                new byte[] { 0x02, 0x01, 0x09 }, CancellationToken.None);
            _output.WriteLine("  ExitTransferMode sent.");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"  ExitTransferMode failed: {ex.Message}");
        }
        await Task.Delay(2_000);
    }

    private static async Task<string?> ProbeHttpEndpoints(ITestOutputHelper output)
    {
        var probeIps = new[]
        {
            "192.168.43.1", "192.168.4.1", "192.168.49.1",
            "192.168.1.1", "192.168.0.1", "192.168.31.1",
            "192.168.100.1", "192.168.123.1", "192.168.137.1",
            "10.0.0.1", "172.20.10.1"
        };

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        foreach (var ip in probeIps)
        {
            var url = $"http://{ip}:8080";
            try
            {
                var response = await http.GetAsync($"{url}/filelist");
                output.WriteLine($"  *** {ip}:8080 — HTTP {(int)response.StatusCode} — REACHABLE! ***");
                return url;
            }
            catch (TaskCanceledException)
            {
                output.WriteLine($"  {ip}:8080 — timeout");
            }
            catch (HttpRequestException ex)
            {
                output.WriteLine($"  {ip}:8080 — {ex.InnerException?.GetType().Name ?? ex.GetType().Name}");
            }
        }

        // Also try port 80 as fallback
        output.WriteLine("  Trying port 80 fallback...");
        foreach (var ip in probeIps.Take(5))
        {
            var url = $"http://{ip}";
            try
            {
                var response = await http.GetAsync($"{url}/filelist");
                output.WriteLine($"  *** {ip}:80 — HTTP {(int)response.StatusCode} — REACHABLE! ***");
                return url;
            }
            catch (TaskCanceledException)
            {
                output.WriteLine($"  {ip}:80 — timeout");
            }
            catch (HttpRequestException ex)
            {
                output.WriteLine($"  {ip}:80 — {ex.InnerException?.GetType().Name ?? ex.GetType().Name}");
            }
        }

        return null;
    }

    private static bool IsAsciiPrintable(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            if (b < 0x20 || b > 0x7E) return false;
        }
        return true;
    }
}
