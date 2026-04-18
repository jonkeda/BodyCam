using BodyCam.Services.Input;
using BodyCam.Tests.TestInfrastructure.Providers;
using FluentAssertions;

namespace BodyCam.Tests.TestInfrastructure;

public class TestMicProviderTests
{
    [Fact]
    public async Task StartAsync_EmitsChunks()
    {
        var chunks = new List<byte[]>();
        var mic = new TestMicProvider(new byte[9600]);
        mic.AudioChunkAvailable += (_, c) => chunks.Add(c);

        await mic.StartAsync();
        await Task.Delay(500);

        chunks.Should().HaveCountGreaterThanOrEqualTo(3);
        mic.ChunksEmitted.Should().Be(chunks.Count);
    }

    [Fact]
    public async Task StopAsync_StopsEmitting()
    {
        var mic = new TestMicProvider(new byte[320000]);
        await mic.StartAsync();
        await Task.Delay(50);
        await mic.StopAsync();

        mic.IsCapturing.Should().BeFalse();
    }

    [Fact]
    public void SimulateDisconnect_FiresEvent()
    {
        var disconnected = false;
        var mic = new TestMicProvider(new byte[3200]);
        mic.Disconnected += (_, _) => disconnected = true;

        mic.SimulateDisconnect();

        disconnected.Should().BeTrue();
        mic.IsCapturing.Should().BeFalse();
    }
}

public class TestSpeakerProviderTests
{
    [Fact]
    public async Task PlayChunkAsync_CapturesData()
    {
        var speaker = new TestSpeakerProvider();
        await speaker.StartAsync(24000);

        await speaker.PlayChunkAsync(new byte[] { 1, 2, 3 });
        await speaker.PlayChunkAsync(new byte[] { 4, 5 });

        speaker.ChunkCount.Should().Be(2);
        speaker.TotalBytesPlayed.Should().Be(5);
        speaker.WasAudioPlayed.Should().BeTrue();
    }

    [Fact]
    public void ClearBuffer_ClearsChunks()
    {
        var speaker = new TestSpeakerProvider();
        speaker.PlayChunkAsync(new byte[] { 1, 2 }).Wait();

        speaker.ClearBuffer();

        speaker.ChunkCount.Should().Be(0);
        speaker.WasAudioPlayed.Should().BeFalse();
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var speaker = new TestSpeakerProvider();
        speaker.PlayChunkAsync(new byte[] { 1 }).Wait();

        speaker.Reset();

        speaker.WasAudioPlayed.Should().BeFalse();
        speaker.ChunkCount.Should().Be(0);
        speaker.IsPlaying.Should().BeFalse();
    }
}

public class TestCameraProviderTests
{
    [Fact]
    public async Task CaptureFrameAsync_CyclesThroughFrames()
    {
        var frame1 = new byte[] { 0xFF, 0xD8, 1 };
        var frame2 = new byte[] { 0xFF, 0xD8, 2 };
        var camera = new TestCameraProvider(frame1, frame2);

        var f1 = await camera.CaptureFrameAsync();
        var f2 = await camera.CaptureFrameAsync();
        var f3 = await camera.CaptureFrameAsync();

        f1.Should().BeEquivalentTo(frame1);
        f2.Should().BeEquivalentTo(frame2);
        f3.Should().BeEquivalentTo(frame1);
        camera.FramesCaptured.Should().Be(3);
    }

    [Fact]
    public async Task CaptureFrameAsync_ReturnsNull_WhenUnavailable()
    {
        var camera = new TestCameraProvider(new byte[] { 1 });
        camera.IsAvailable = false;

        var result = await camera.CaptureFrameAsync();

        result.Should().BeNull();
    }

    [Fact]
    public void SimulateDisconnect_FiresEvent()
    {
        var disconnected = false;
        var camera = new TestCameraProvider(new byte[] { 1 });
        camera.Disconnected += (_, _) => disconnected = true;

        camera.SimulateDisconnect();

        disconnected.Should().BeTrue();
        camera.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void Reset_RestoresState()
    {
        var camera = new TestCameraProvider(new byte[] { 1 });
        camera.CaptureFrameAsync().Wait();
        camera.SimulateDisconnect();

        camera.Reset();

        camera.IsAvailable.Should().BeTrue();
        camera.FramesCaptured.Should().Be(0);
    }
}

public class TestButtonProviderTests
{
    [Fact]
    public void SimulateClick_FiresDownThenUp()
    {
        var events = new List<RawButtonEventType>();
        var btn = new TestButtonProvider();
        btn.RawButtonEvent += (_, e) => events.Add(e.EventType);

        btn.SimulateClick();

        events.Should().ContainInOrder(
            RawButtonEventType.ButtonDown,
            RawButtonEventType.ButtonUp);
        btn.ClickCount.Should().Be(1);
    }

    [Fact]
    public void SimulateGesture_FiresPreRecognized()
    {
        ButtonGestureEvent? received = null;
        var btn = new TestButtonProvider();
        btn.PreRecognizedGesture += (_, e) => received = e;

        btn.SimulateGesture(ButtonGesture.DoubleTap);

        received.Should().NotBeNull();
        received!.Gesture.Should().Be(ButtonGesture.DoubleTap);
        btn.LastGesture.Should().Be(ButtonGesture.DoubleTap);
        btn.GestureCount.Should().Be(1);
    }

    [Fact]
    public void SimulateDisconnect_FiresEvent()
    {
        var disconnected = false;
        var btn = new TestButtonProvider();
        btn.Disconnected += (_, _) => disconnected = true;

        btn.SimulateDisconnect();

        disconnected.Should().BeTrue();
        btn.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Reset_ClearsCounters()
    {
        var btn = new TestButtonProvider();
        btn.SimulateClick();
        btn.SimulateGesture(ButtonGesture.SingleTap);

        btn.Reset();

        btn.ClickCount.Should().Be(0);
        btn.GestureCount.Should().Be(0);
        btn.LastGesture.Should().BeNull();
    }
}
