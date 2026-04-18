using BodyCam.Tests.TestInfrastructure;
using BodyCam.Tests.TestInfrastructure.Providers;
using FluentAssertions;

namespace BodyCam.Tests.Integration;

public class CameraPipelineTests : IAsyncLifetime
{
    private BodyCamTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = BodyCamTestHost.Create();
        await _host.InitializeAsync();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task CaptureFrame_ReturnsSingleFrame()
    {
        var frame = await _host.CameraManager.CaptureFrameAsync();

        frame.Should().NotBeNull();
        frame.Should().BeEquivalentTo(TestAssets.MinimalJpeg);
        _host.Camera.FramesCaptured.Should().Be(1);
    }

    [Fact]
    public async Task CaptureFrame_MultipleCalls_IncrementCounter()
    {
        await _host.CameraManager.CaptureFrameAsync();
        await _host.CameraManager.CaptureFrameAsync();
        await _host.CameraManager.CaptureFrameAsync();

        _host.Camera.FramesCaptured.Should().Be(3);
    }

    [Fact]
    public async Task CaptureFrame_MultipleCalls_AllReturnValidFrames()
    {
        var captured1 = await _host.CameraManager.CaptureFrameAsync();
        var captured2 = await _host.CameraManager.CaptureFrameAsync();
        var captured3 = await _host.CameraManager.CaptureFrameAsync();

        captured1.Should().NotBeNull();
        captured2.Should().NotBeNull();
        captured3.Should().NotBeNull();

        // All frames are valid JPEG (default test provider always returns MinimalJpeg)
        captured1.Should().BeEquivalentTo(TestAssets.MinimalJpeg);
        captured2.Should().BeEquivalentTo(TestAssets.MinimalJpeg);
        captured3.Should().BeEquivalentTo(TestAssets.MinimalJpeg);

        _host.Camera.FramesCaptured.Should().Be(3);
    }

    [Fact]
    public async Task CaptureFrame_AfterDisconnect_ReturnsNull()
    {
        var frame = await _host.CameraManager.CaptureFrameAsync();
        frame.Should().NotBeNull();

        _host.Camera.SimulateDisconnect();

        var nullFrame = await _host.CameraManager.CaptureFrameAsync();
        nullFrame.Should().BeNull();
    }

    [Fact]
    public async Task CaptureFrame_AfterReset_CounterResetsToZero()
    {
        await _host.CameraManager.CaptureFrameAsync();
        await _host.CameraManager.CaptureFrameAsync();
        _host.Camera.FramesCaptured.Should().Be(2);

        _host.Camera.Reset();
        _host.Camera.FramesCaptured.Should().Be(0);
        _host.Camera.IsAvailable.Should().BeTrue();

        // Can still capture after reset
        var afterReset = await _host.CameraManager.CaptureFrameAsync();
        afterReset.Should().NotBeNull();
        afterReset.Should().BeEquivalentTo(TestAssets.MinimalJpeg);
        _host.Camera.FramesCaptured.Should().Be(1);
    }

    [Fact]
    public async Task StreamFrames_EmitsMultipleFrames()
    {
        var frames = new List<byte[]>();
        var cts = new CancellationTokenSource(350); // stop after ~350ms

        try
        {
            await foreach (var frame in _host.Camera.StreamFramesAsync(cts.Token))
            {
                frames.Add(frame);
            }
        }
        catch (OperationCanceledException) { }

        // At 100ms interval, 350ms should yield 3-4 frames
        frames.Should().HaveCountGreaterThanOrEqualTo(2);
        frames.Should().AllSatisfy(f => f.Should().BeEquivalentTo(TestAssets.MinimalJpeg));
    }

    [Fact]
    public async Task StreamFrames_StopsOnDisconnect()
    {
        var frames = new List<byte[]>();
        var cts = new CancellationTokenSource(2000);

        // Disconnect after a short delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            _host.Camera.SimulateDisconnect();
        });

        try
        {
            await foreach (var frame in _host.Camera.StreamFramesAsync(cts.Token))
            {
                frames.Add(frame);
            }
        }
        catch (OperationCanceledException) { }

        // Should have gotten some frames before disconnect
        frames.Should().NotBeEmpty();
        _host.Camera.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CameraManager_ActiveProvider_IsTestCamera()
    {
        _host.CameraManager.Active.Should().NotBeNull();
        _host.CameraManager.Active!.ProviderId.Should().Be("test-camera");
        _host.CameraManager.Active!.DisplayName.Should().Be("Test Camera");
    }
}
