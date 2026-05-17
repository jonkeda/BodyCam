using BodyCam.RealTests.Fixtures;
using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Services.Glasses.HeyCyan;

/// <summary>
/// Real-hardware tests for the HeyCyan Windows BLE + Classic BT connection flow.
/// Exercises the full production code path: WindowsHeyCyanGlassesSession,
/// WindowsBluetoothEnumerator, HeyCyanAudioRouter, and HeyCyanGlassesDeviceManager.
///
/// Requires:
/// - HeyCyan glasses powered on and discoverable
/// - BODYCAM_REAL_HEYCYAN=1
/// - BODYCAM_REAL_HEYCYAN_MAC=D8:79:B8:7F:E6:C9 (your glasses' address)
/// - Optionally BODYCAM_REAL_HEYCYAN_NAME=M01 Pro
///
/// Run with:
///   dotnet test src/BodyCam.RealTests -f net10.0-windows10.0.19041.0 --filter "Category=RealConnection" -v normal
/// </summary>
[Trait("Category", "RealConnection")]
[Collection("HeyCyanHardware")]
public sealed class WindowsConnectionFlowTests
{
    private readonly ITestOutputHelper _output;
    private readonly SharedHeyCyanFixture _shared;
    private WindowsHeyCyanRealFixture? _fixture;

    private static bool RealEnabled =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN") == "1";

    private static string Mac =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_MAC") ?? "";

    private static string? ExpectedName =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_NAME");

    public WindowsConnectionFlowTests(SharedHeyCyanFixture shared, ITestOutputHelper output)
    {
        _shared = shared;
        _output = output;
        _fixture = shared.Inner;
    }

    // ── Scan & Discovery ────────────────────────────────────────────

    [SkippableFact]
    public async Task Scan_FindsGlassesByMac()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");
        // Glasses don't BLE-advertise while connected — skip if fixture is connected
        Skip.If(_fixture!.Session.State == HeyCyanState.Connected,
            "Glasses are already connected via shared fixture; BLE advertising is disabled");

        // Glasses may not always be BLE-advertising — retry a few times
        IReadOnlyList<HeyCyanDeviceInfo> devices = [];
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            devices = await _fixture.Session.ScanAsync(TimeSpan.FromSeconds(10), cts.Token);

            _output.WriteLine($"Attempt {attempt}: Found {devices.Count} device(s):");
            foreach (var d in devices)
                _output.WriteLine($"  {d.Name} ({d.Address}) RSSI={d.Rssi}");

            if (devices.Any(d => string.Equals(d.Address, Mac, StringComparison.OrdinalIgnoreCase)))
                break;

            if (attempt < 3)
            {
                _output.WriteLine("Glasses not found, retrying...");
                await Task.Delay(2000);
            }
        }

        devices.Should().Contain(d =>
            string.Equals(d.Address, Mac, StringComparison.OrdinalIgnoreCase),
            $"scan should find glasses with MAC {Mac}");
    }

    [SkippableFact]
    public async Task Scan_DeviceNameMatchesExpected()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");
        Skip.If(string.IsNullOrEmpty(ExpectedName), "BODYCAM_REAL_HEYCYAN_NAME not set");
        Skip.If(_fixture!.Session.State == HeyCyanState.Connected,
            "Glasses are already connected via shared fixture; BLE advertising is disabled");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var devices = await _fixture!.Session.ScanAsync(TimeSpan.FromSeconds(10), cts.Token);
        var target = devices.First(d =>
            string.Equals(d.Address, Mac, StringComparison.OrdinalIgnoreCase));

        _output.WriteLine($"Device name: '{target.Name}', expected prefix: '{ExpectedName}'");
        target.Name.Should().StartWith(ExpectedName!);
    }

    [SkippableFact]
    public async Task Scan_RssiIsNegative()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");
        Skip.If(_fixture!.Session.State == HeyCyanState.Connected,
            "Glasses are already connected via shared fixture; BLE advertising is disabled");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var devices = await _fixture!.Session.ScanAsync(TimeSpan.FromSeconds(10), cts.Token);
        var target = devices.First(d =>
            string.Equals(d.Address, Mac, StringComparison.OrdinalIgnoreCase));

        _output.WriteLine($"RSSI: {target.Rssi} dBm");
        target.Rssi.Should().BeNegative("BLE RSSI values are always negative");
    }

    // ── Full Connection ─────────────────────────────────────────────

    [SkippableFact]
    public async Task Connect_PopulatesVersionAndBattery()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        _output.WriteLine($"Connected to {_shared.ConnectedDevice?.Name} ({_shared.ConnectedDevice?.Address})");
        _output.WriteLine($"Version: HW={_fixture!.DeviceManager.Version?.Hardware}, FW={_fixture.DeviceManager.Version?.Firmware}");
        _output.WriteLine($"Battery: {_fixture.DeviceManager.Battery?.Percentage}% (charging={_fixture.DeviceManager.Battery?.IsCharging})");

        _fixture.DeviceManager.Version.Should().NotBeNull();
        _fixture.DeviceManager.Battery.Should().NotBeNull();
        _fixture.DeviceManager.Battery!.Percentage.Should().BeInRange(0, 100);
    }

    [SkippableFact]
    public async Task Connect_SessionStateIsConnected()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        _output.WriteLine($"Session state: {_fixture!.Session.State}");
        _fixture.Session.State.Should().Be(HeyCyanState.Connected);
    }

    [SkippableFact]
    public async Task Connect_SavesDeviceToSettings()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        _output.WriteLine($"Settings.LastHeyCyanDeviceAddress: {_fixture!.Settings.LastHeyCyanDeviceAddress}");
        _output.WriteLine($"Settings.LastHeyCyanDeviceName: {_fixture.Settings.LastHeyCyanDeviceName}");

        _fixture.Settings.LastHeyCyanDeviceAddress.Should().NotBeNullOrEmpty();
        _fixture.Settings.LastHeyCyanDeviceAddress.Should().BeEquivalentTo(Mac);
    }

    // ── Classic BT Audio Endpoints ──────────────────────────────────

    [SkippableFact]
    public async Task Connect_BtCaptureEndpointExists()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var found = await PollForEndpointAsync(
            () => _fixture!.BtEnumerator.HasEndpointWithMac(Mac),
            "BT capture", cts.Token);

        found.Should().BeTrue("capture endpoint should exist after connection");
    }

    [SkippableFact]
    public async Task Connect_BtRenderEndpointExists()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var found = await PollForEndpointAsync(
            () => _fixture!.BtOutputEnumerator.HasEndpointWithMac(Mac),
            "BT render", cts.Token);

        found.Should().BeTrue("render endpoint should exist after connection");
    }

    [SkippableFact]
    public async Task Connect_MicProviderIsAvailable()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var available = await PollForEndpointAsync(
            () => _fixture!.MicProvider.IsAvailable,
            "HeyCyan mic", cts.Token);

        _output.WriteLine($"MicProvider.IsAvailable: {_fixture!.MicProvider.IsAvailable}");
        available.Should().BeTrue("HeyCyan mic provider should be available after connect");
    }

    [SkippableFact]
    public async Task Connect_SpeakerProviderIsAvailable()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var available = await PollForEndpointAsync(
            () => _fixture!.SpeakerProvider.IsAvailable,
            "HeyCyan speaker", cts.Token);

        _output.WriteLine($"SpeakerProvider.IsAvailable: {_fixture!.SpeakerProvider.IsAvailable}");
        available.Should().BeTrue("HeyCyan speaker provider should be available after connect");
    }

    // ── Audio Router Auto-Selection ─────────────────────────────────

    [SkippableFact]
    public async Task Router_AutoSelectsGlassesMicOnConnect()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        // Poll for router to auto-select glasses mic (triggers via EndpointRegistered)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var selected = await PollForEndpointAsync(
            () => _fixture!.AudioInput.ActiveProviderId == "heycyan-glasses",
            "router auto-select mic", cts.Token);

        _output.WriteLine($"ActiveAudioInputProvider: {_fixture!.AudioInput.ActiveProviderId}");
        selected.Should().BeTrue("router should auto-select glasses mic on connect");
    }

    [SkippableFact]
    public async Task Router_AutoSelectsGlassesSpeakerOnConnect()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        // Poll for router to auto-select glasses speaker (triggers via EndpointRegistered)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var selected = await PollForEndpointAsync(
            () => _fixture!.AudioOutput.ActiveProviderId == "heycyan-glasses",
            "router auto-select speaker", cts.Token);

        _output.WriteLine($"ActiveAudioOutputProvider: {_fixture!.AudioOutput.ActiveProviderId}");
        selected.Should().BeTrue("router should auto-select glasses speaker on connect");
    }

    // ── Edge Cases ──────────────────────────────────────────────────

    [SkippableFact]
    public async Task Scan_PairedDeviceFallback_FindsConnectedGlasses()
    {
        // RCA 002: When glasses are already connected via Classic BT, they stop
        // advertising BLE packets. The paired-device fallback should still find them.
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");
        Skip.If(_fixture!.Session.State != HeyCyanState.Connected,
            "Glasses must be connected for this test — paired-device fallback only matters when BLE ads are suppressed");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var devices = await _fixture.Session.ScanAsync(TimeSpan.FromSeconds(8), cts.Token);

        _output.WriteLine($"Found {devices.Count} device(s) while glasses are connected:");
        foreach (var d in devices)
            _output.WriteLine($"  {d.Name} ({d.Address}) RSSI={d.Rssi}");

        var match = devices.FirstOrDefault(d =>
            string.Equals(d.Address, Mac, StringComparison.OrdinalIgnoreCase));

        match.Should().NotBeNull(
            $"paired-device fallback should find connected glasses at {Mac} even without BLE advertising");

        // Paired (non-advertising) devices have RSSI=0 since there's no advertisement signal
        _output.WriteLine($"Matched device RSSI: {match!.Rssi} (0 = found via paired-device fallback)");
        match.Rssi.Should().Be(0,
            "devices found via paired-device fallback have RSSI=0 (no BLE advertisement)");
    }

    [SkippableFact]
    public async Task Connect_CancellationDuringBle_Throws()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");
        Skip.If(_fixture!.Session.State == HeyCyanState.Connected,
            "Glasses are already connected via shared fixture; BLE advertising is disabled");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var devices = await _fixture!.Session.ScanAsync(TimeSpan.FromSeconds(10), default);
        var target = devices.FirstOrDefault(d =>
            string.Equals(d.Address, Mac, StringComparison.OrdinalIgnoreCase));

        Skip.If(target == null, $"Glasses {Mac} not found in scan");

        var act = () => _fixture.DeviceManager.ConnectAsync(target, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [SkippableFact]
    public async Task Scan_GlassesPoweredOff_ReturnsEmpty()
    {
        // This test is meaningful only when glasses are intentionally OFF.
        // Gate with a separate env var so it doesn't fail during normal runs.
        var offTest = Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_GLASSES_OFF") == "1";
        Skip.IfNot(offTest, "BODYCAM_REAL_HEYCYAN_GLASSES_OFF not set (glasses must be powered off)");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var devices = await _fixture!.Session.ScanAsync(TimeSpan.FromSeconds(5), cts.Token);

        _output.WriteLine($"Found {devices.Count} device(s) with glasses off");
        devices.Should().NotContain(d =>
            string.Equals(d.Address, Mac, StringComparison.OrdinalIgnoreCase),
            "powered-off glasses should not appear in scan");
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private async Task<bool> PollForEndpointAsync(
        Func<bool> check, string label, CancellationToken ct)
    {
        for (int i = 0; i < 15; i++)
        {
            if (check())
            {
                _output.WriteLine($"{label} found after {(i + 1) * 2}s");
                return true;
            }

            _output.WriteLine($"Waiting for {label}... ({(i + 1) * 2}s)");
            _fixture!.BtEnumerator.ScanAndRegister();
            _fixture.BtOutputEnumerator.ScanAndRegister();
            await Task.Delay(2000, ct);
        }

        return check();
    }
}
