#if IOS
using BodyCam.Platforms.iOS.HeyCyan;
using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BodyCam.RealTests.Services.Glasses.HeyCyan;

/// <summary>
/// iOS real-device integration tests for IosHeyCyanGlassesSession.
/// Requires BODYCAM_HEYCYAN_REAL_DEVICE=1 and paired HeyCyan glasses.
/// Run on a physical iOS device via: dotnet test -f net10.0-ios --filter "Category=RealDevice"
/// </summary>
[Trait("Category", "RealDevice")]
public sealed class IosHeyCyanRealDeviceTests
{
    private static bool IsRealDeviceEnabled =>
        Environment.GetEnvironmentVariable("BODYCAM_HEYCYAN_REAL_DEVICE") == "1";

    private static IosHeyCyanGlassesSession CreateSession()
    {
        return new IosHeyCyanGlassesSession(NullLoggerFactory.Instance);
    }

    [SkippableFact]
    public async Task Scan_returns_paired_glasses()
    {
        Skip.If(!IsRealDeviceEnabled, "BODYCAM_HEYCYAN_REAL_DEVICE not set to 1");

        await using var session = CreateSession();

        var devices = await session.ScanAsync(TimeSpan.FromSeconds(15), default);

        devices.Should().NotBeEmpty("at least one HeyCyan glasses device should be in range");
        devices.Should().OnlyContain(d => !string.IsNullOrWhiteSpace(d.Name));
        devices.Should().OnlyContain(d => !string.IsNullOrWhiteSpace(d.Address));
    }

    [SkippableFact]
    public async Task Connect_then_get_version_and_battery()
    {
        Skip.If(!IsRealDeviceEnabled, "BODYCAM_HEYCYAN_REAL_DEVICE not set to 1");

        await using var session = CreateSession();

        var devices = await session.ScanAsync(TimeSpan.FromSeconds(15), default);
        devices.Should().NotBeEmpty();

        await session.ConnectAsync(devices[0], default);

        session.State.Should().Be(HeyCyanState.Connected);
        session.Device.Should().NotBeNull();
        session.Device!.Address.Should().Be(devices[0].Address);

        var version = await session.GetVersionAsync(default);
        version.Firmware.Should().NotBeNullOrEmpty();
        version.Hardware.Should().NotBeNullOrEmpty();

        var battery = await session.GetBatteryAsync(default);
        battery.Level.Should().BeInRange(0, 100);
    }

    [SkippableFact]
    public async Task TakePhoto_then_retrieve_via_transfer()
    {
        Skip.If(!IsRealDeviceEnabled, "BODYCAM_HEYCYAN_REAL_DEVICE not set to 1");

        await using var session = CreateSession();

        var devices = await session.ScanAsync(TimeSpan.FromSeconds(15), default);
        await session.ConnectAsync(devices[0], default);

        await session.SyncTimeAsync(default);
        await session.TakePhotoAsync(default);

        // Wait for media count update
        await Task.Delay(TimeSpan.FromSeconds(2));

        await using var transfer = await session.EnterTransferModeAsync(default);

        session.State.Should().Be(HeyCyanState.TransferMode);
        transfer.BaseUrl.Should().StartWith("http://");
        transfer.FileNames.Should().NotBeEmpty("at least one photo should exist");
    }

    [SkippableFact]
    public async Task TakeAiPhoto_receives_image_data()
    {
        Skip.If(!IsRealDeviceEnabled, "BODYCAM_HEYCYAN_REAL_DEVICE not set to 1");

        await using var session = CreateSession();

        var devices = await session.ScanAsync(TimeSpan.FromSeconds(15), default);
        await session.ConnectAsync(devices[0], default);

        byte[]? receivedImage = null;
        session.AiPhotoReceived += (s, img) => receivedImage = img;

        await session.TakeAiPhotoAsync(default);

        // Wait for multi-packet transfer (can take 2-3 seconds)
        await Task.Delay(TimeSpan.FromSeconds(5));

        receivedImage.Should().NotBeNull("AI photo should arrive via AiPhotoReceived event");
        receivedImage.Should().StartWith(new byte[] { 0xFF, 0xD8 }, "should be valid JPEG");
    }

    [SkippableFact]
    public async Task Transfer_mode_round_trip_returns_to_wifi()
    {
        Skip.If(!IsRealDeviceEnabled, "BODYCAM_HEYCYAN_REAL_DEVICE not set to 1");

        await using var session = CreateSession();

        var devices = await session.ScanAsync(TimeSpan.FromSeconds(15), default);
        await session.ConnectAsync(devices[0], default);

        // Enter transfer mode
        await using var transfer = await session.EnterTransferModeAsync(default);
        session.State.Should().Be(HeyCyanState.TransferMode);

        // Exit transfer mode
        await session.ExitTransferModeAsync(default);
        session.State.Should().Be(HeyCyanState.Connected);

        // Verify iPhone returned to previous Wi-Fi network
        // (NEHotspotConfigurationManager automatically removes the hotspot config)
        // Manual verification step: check Settings → Wi-Fi on the device
    }

    [SkippableFact]
    public async Task ButtonPress_events_received()
    {
        Skip.If(!IsRealDeviceEnabled, "BODYCAM_HEYCYAN_REAL_DEVICE not set to 1");

        await using var session = CreateSession();

        var devices = await session.ScanAsync(TimeSpan.FromSeconds(15), default);
        await session.ConnectAsync(devices[0], default);

        var buttonEvents = new List<HeyCyanButtonGesture>();
        session.ButtonPressed += (s, e) => buttonEvents.Add(e.Gesture);

        // Manual step: press the glasses button (tap / double-tap / long press)
        // Wait 10 seconds for user interaction
        await Task.Delay(TimeSpan.FromSeconds(10));

        buttonEvents.Should().NotBeEmpty("at least one button press should be detected during the 10s window");
    }

    [SkippableFact]
    public async Task Disconnect_cleans_up_resources()
    {
        Skip.If(!IsRealDeviceEnabled, "BODYCAM_HEYCYAN_REAL_DEVICE not set to 1");

        await using var session = CreateSession();

        var devices = await session.ScanAsync(TimeSpan.FromSeconds(15), default);
        await session.ConnectAsync(devices[0], default);
        session.State.Should().Be(HeyCyanState.Connected);

        await session.DisconnectAsync(default);

        session.State.Should().Be(HeyCyanState.Disconnected);
        session.Device.Should().BeNull();
    }
}
#endif
