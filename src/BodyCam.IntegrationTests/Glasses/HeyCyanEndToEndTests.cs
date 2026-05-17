using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Glasses;
using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Services.Input;
using FluentAssertions;

namespace BodyCam.IntegrationTests.Glasses;

/// <summary>
/// M33 Phase 7 Wave 5: Real-hardware end-to-end acceptance test.
/// Requires real HeyCyan hardware; gated behind HEYCYAN_E2E=1 env var.
/// </summary>
[Trait("Category", "RealHardware")]
[Collection("HeyCyanHardware")]
public class HeyCyanEndToEndTests
{
    /// <summary>
    /// End-to-end test: Connect → Disconnect → Reconnect cycle.
    /// Verifies:
    /// - Scan returns HeyCyan devices
    /// - Connect populates status (battery, version, media count)
    /// - Disconnect triggers fallback to phone providers
    /// - Reconnect restores glasses providers
    /// </summary>
    [Fact(Skip = "Requires HEYCYAN_E2E=1 env var and real HeyCyan glasses hardware")]
    public async Task Connect_Disconnect_Reconnect_FallsBackAndRestores()
    {
        // Guard: skip if not in E2E mode
        if (Environment.GetEnvironmentVariable("HEYCYAN_E2E") != "1")
        {
            return;
        }

        // Arrange: resolve managers from test host
        var mgr = TestHost.Resolve<HeyCyanGlassesDeviceManager>();
        var camera = TestHost.Resolve<CameraManager>();
        var mic = TestHost.Resolve<AudioInputManager>();
        var speaker = TestHost.Resolve<AudioOutputManager>();

        // Act: scan for HeyCyan devices
        var devices = await mgr.ScanAsync(TimeSpan.FromSeconds(10), default);
        
        // Assert: at least one device found
        devices.Should().NotBeEmpty("at least one HeyCyan device should be discoverable");

        // Act: connect to first device
        await mgr.ConnectAsync(devices[0], default);

        // Assert: connection state and status populated
        mgr.State.Should().Be(GlassesConnectionState.Connected);
        mgr.Battery.Should().NotBeNull();
        mgr.Battery!.Percentage.Should().BeGreaterThan(0, "battery percentage should be positive");
        mgr.Version.Should().NotBeNull();
        mgr.Version!.MacAddress.Should().NotBeNullOrEmpty("MAC address should be populated");
        mgr.MediaCount.Should().NotBeNull("media count should be populated");

        // Assert: glasses providers active
        camera.Active.Should().BeOfType<HeyCyanCameraProvider>();
        mic.Active.Should().BeOfType<HeyCyanAudioInputProvider>();
        speaker.Active.Should().BeOfType<HeyCyanAudioOutputProvider>();

        // Act: disconnect manually
        await mgr.DisconnectAsync(default);
        await Task.Delay(2_500); // M17 fallback window + margin

        // Assert: fallback to phone providers (cross-checked against Wave 4)
        camera.Active?.ProviderId.Should().Contain("phone", "camera should fall back to phone");
        mic.Active?.ProviderId.Should().Contain("platform", "mic should fall back to platform");
        speaker.Active?.ProviderId.Should().Contain("platform", "speaker should fall back to platform");

        // Act: reconnect manually (auto-reconnect is exercised in Wave 4)
        await mgr.ConnectAsync(devices[0], default);

        // Assert: restored to connected state with glasses providers
        mgr.State.Should().Be(GlassesConnectionState.Connected);
        camera.Active.Should().BeOfType<HeyCyanCameraProvider>();
        mic.Active.Should().BeOfType<HeyCyanAudioInputProvider>();
        speaker.Active.Should().BeOfType<HeyCyanAudioOutputProvider>();
    }

    /// <summary>
    /// Placeholder TestHost for DI resolution.
    /// In a real integration test, this would be replaced with a proper test fixture
    /// that initializes the MAUI service container.
    /// </summary>
    private static class TestHost
    {
        public static T Resolve<T>() where T : class
        {
            throw new NotImplementedException(
                "TestHost.Resolve requires a real integration test fixture with DI container. " +
                "This test is gated behind HEYCYAN_E2E=1 and must be run manually on real hardware.");
        }
    }
}
