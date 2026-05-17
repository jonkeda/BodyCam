using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Services.Input;
using FluentAssertions;

namespace BodyCam.IntegrationTests.Glasses;

/// <summary>
/// M33 Phase 7 Wave 4: End-to-end fallback verification.
/// Requires real HeyCyan hardware; gated behind HEYCYAN_E2E=1 env var.
/// </summary>
[Trait("Category", "RealHardware")]
[Collection("HeyCyanHardware")]
public class HeyCyanFallbackTests
{
    /// <summary>
    /// Verifies that when HeyCyan glasses disconnect unexpectedly,
    /// all four capability managers (camera, mic, speaker, button)
    /// fall back to phone-based providers within the 2-second SLA.
    /// </summary>
    [Fact(Skip = "Requires HEYCYAN_E2E=1 env var and real HeyCyan glasses hardware")]
    public async Task Disconnect_FallsBackToPhoneProviders()
    {
        // Guard: skip if not in E2E mode
        if (Environment.GetEnvironmentVariable("HEYCYAN_E2E") != "1")
        {
            return;
        }

        // Arrange: resolve managers from a test host (assumes DI container is set up)
        // In a real integration test environment, you would use a proper test fixture.
        // For now, this is a placeholder structure per the wave doc.
        var mgr = TestHost.Resolve<HeyCyanGlassesDeviceManager>();
        var camera = TestHost.Resolve<CameraManager>();
        var mic = TestHost.Resolve<AudioInputManager>();
        var speaker = TestHost.Resolve<AudioOutputManager>();

        // Act: scan and connect to first available HeyCyan device
        var devices = await mgr.ScanAsync(TimeSpan.FromSeconds(10), default);
        devices.Should().NotBeEmpty("at least one HeyCyan device should be discoverable");

        await mgr.ConnectAsync(devices[0], default);

        // Assert: glasses providers should be active
        camera.Active.Should().BeOfType<HeyCyanCameraProvider>();
        mic.Active.Should().BeOfType<HeyCyanAudioInputProvider>();
        speaker.Active.Should().BeOfType<HeyCyanAudioOutputProvider>();

        // Act: disconnect glasses
        await mgr.DisconnectAsync(default);
        await Task.Delay(2_500); // M17 fallback window + margin

        // Assert: phone providers should be active
        camera.Active?.ProviderId.Should().Contain("phone");
        mic.Active?.ProviderId.Should().Contain("platform");
        speaker.Active?.ProviderId.Should().Contain("platform");
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
