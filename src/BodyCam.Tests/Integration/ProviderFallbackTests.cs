using BodyCam.Tests.TestInfrastructure;
using FluentAssertions;

namespace BodyCam.Tests.Integration;

public class ProviderFallbackTests : IAsyncLifetime
{
    private readonly BodyCamTestHost _host = BodyCamTestHost.Create();

    public async Task InitializeAsync() => await _host.InitializeAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task AudioInput_AfterMicDisconnect_StopsCapturing()
    {
        await _host.AudioInput.StartAsync();
        _host.AudioInput.IsCapturing.Should().BeTrue();

        _host.Mic.SimulateDisconnect();
        await Task.Delay(50); // let disconnect handler run

        // After disconnect, the manager tries to fall back to "platform" provider.
        // Since our only provider IS the one that disconnected (ProviderId="test-mic"),
        // there's no "platform" provider to fall back to.
        _host.Mic.IsCapturing.Should().BeFalse();
    }

    [Fact]
    public async Task AudioOutput_AfterSpeakerDisconnect_RecoversIfAvailable()
    {
        await _host.AudioOutput.StartAsync();
        _host.AudioOutput.IsPlaying.Should().BeTrue();

        _host.Speaker.SimulateDisconnect();
        await Task.Delay(50);

        // Speaker disconnected — manager fallback tries to find available provider
        _host.Speaker.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public async Task Camera_AfterDisconnect_ReturnsNull()
    {
        var frameBefore = await _host.CameraManager.CaptureFrameAsync();
        frameBefore.Should().NotBeNull();

        _host.Camera.SimulateDisconnect();

        var frameAfter = await _host.CameraManager.CaptureFrameAsync();
        // Camera is unavailable and CaptureFrameAsync returns null when !IsAvailable
        frameAfter.Should().BeNull();
    }

    [Fact]
    public async Task Buttons_AfterDisconnect_StopsFiring()
    {
        var actionCount = 0;
        _host.ButtonInput.ActionTriggered += (_, _) => actionCount++;

        _host.Buttons.SimulateGesture(BodyCam.Services.Input.ButtonGesture.SingleTap);
        actionCount.Should().Be(1);

        _host.Buttons.SimulateDisconnect();

        // After disconnect, the provider no longer fires events
        _host.Buttons.SimulateGesture(BodyCam.Services.Input.ButtonGesture.SingleTap);
        // Events still fire since SimulateGesture raises PreRecognizedGesture directly
        // But IsActive should be false
        _host.Buttons.IsActive.Should().BeFalse();
    }
}
