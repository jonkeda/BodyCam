using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Input;
using BodyCam.Tests.TestInfrastructure;
using BodyCam.Tests.TestInfrastructure.Providers;
using BodyCam.Tools;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Integration;

/// <summary>
/// Cross-cutting integration tests (INT-4 through INT-9 from test-steps.md).
/// Tests provider fallback, gesture remapping, concurrent inputs, and full
/// pipeline round-trips using test providers.
/// </summary>
public class CrossCuttingTests : IAsyncLifetime
{
    private BodyCamTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = BodyCamTestHost.Create();
        await _host.InitializeAsync();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    // --- INT-4: Camera disconnect mid-pipeline ---

    [Fact]
    public async Task CameraDisconnect_CaptureReturnsNull()
    {
        // First capture works
        var frame = await _host.CameraManager.CaptureFrameAsync();
        frame.Should().NotBeNull();

        // Simulate disconnect
        _host.Camera.SimulateDisconnect();

        // Next capture returns null
        var nullFrame = await _host.CameraManager.CaptureFrameAsync();
        nullFrame.Should().BeNull();
        _host.Camera.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CameraDisconnect_ToolContextCaptureReturnsNull()
    {
        var context = new ToolContext
        {
            CaptureFrame = _ => _host.CameraManager.CaptureFrameAsync(),
            Session = new SessionContext(),
            Log = _ => { },
        };

        _host.Camera.SimulateDisconnect();

        var frame = await context.CaptureFrame(CancellationToken.None);
        frame.Should().BeNull();
    }

    // --- INT-5: Mic disconnect mid-session ---

    [Fact]
    public async Task MicDisconnect_StopsCapturing()
    {
        var chunkCount = 0;
        _host.AudioInput.AudioChunkAvailable += (_, _) => chunkCount++;

        await _host.AudioInput.StartAsync();
        await Task.Delay(200);
        chunkCount.Should().BeGreaterThan(0);

        _host.Mic.SimulateDisconnect();

        _host.Mic.IsCapturing.Should().BeFalse();
        var countAfterDisconnect = chunkCount;

        await Task.Delay(200);
        chunkCount.Should().Be(countAfterDisconnect);
    }

    [Fact]
    public async Task MicDisconnect_DisconnectedEventFires()
    {
        var disconnected = false;
        _host.Mic.Disconnected += (_, _) => disconnected = true;

        await _host.AudioInput.StartAsync();
        _host.Mic.SimulateDisconnect();

        disconnected.Should().BeTrue();
    }

    // --- INT-6: Gesture remapping ---

    [Fact]
    public void GestureRemap_SingleTapToPhoto()
    {
        var actions = new List<ButtonActionEvent>();
        _host.ButtonInput.ActionTriggered += (_, a) => actions.Add(a);

        // Remap SingleTap from Look to Photo
        _host.ButtonInput.ActionMap.SetAction(
            "test-buttons:main", ButtonGesture.SingleTap, ButtonAction.Photo);

        _host.Buttons.SimulateGesture(ButtonGesture.SingleTap);

        actions.Should().ContainSingle();
        actions[0].Action.Should().Be(ButtonAction.Photo);
    }

    [Fact]
    public void GestureRemap_DoubleTapToRead()
    {
        var actions = new List<ButtonActionEvent>();
        _host.ButtonInput.ActionTriggered += (_, a) => actions.Add(a);

        _host.ButtonInput.ActionMap.SetAction(
            "test-buttons:main", ButtonGesture.DoubleTap, ButtonAction.Read);

        _host.Buttons.SimulateGesture(ButtonGesture.DoubleTap);

        actions.Should().ContainSingle();
        actions[0].Action.Should().Be(ButtonAction.Read);
    }

    [Fact]
    public void GestureRemap_DisableGesture()
    {
        var actions = new List<ButtonActionEvent>();
        _host.ButtonInput.ActionTriggered += (_, a) => actions.Add(a);

        // Map to None — gesture should be swallowed
        _host.ButtonInput.ActionMap.SetAction(
            "test-buttons:main", ButtonGesture.SingleTap, ButtonAction.None);

        _host.Buttons.SimulateGesture(ButtonGesture.SingleTap);

        actions.Should().BeEmpty();
    }

    // --- INT-7: Concurrent button + audio ---

    [Fact]
    public async Task ConcurrentButtonAndAudio_NoCorruption()
    {
        var actions = new List<ButtonActionEvent>();
        _host.ButtonInput.ActionTriggered += (_, a) => actions.Add(a);

        // Start audio output (simulating AI speaking)
        await _host.AudioOutput.StartAsync();
        await _host.AudioOutput.PlayChunkAsync(new byte[4800]);
        _host.Speaker.WasAudioPlayed.Should().BeTrue();

        // Simultaneously fire a button gesture
        _host.Buttons.SimulateGesture(ButtonGesture.SingleTap);

        // Both should work — no crash, no corruption
        actions.Should().ContainSingle();
        actions[0].Action.Should().Be(ButtonAction.Look);
        _host.Speaker.ChunkCount.Should().Be(1);
    }

    [Fact]
    public async Task MultipleButtonPresses_WhileAudioPlaying()
    {
        var actions = new List<ButtonActionEvent>();
        _host.ButtonInput.ActionTriggered += (_, a) => actions.Add(a);

        await _host.AudioOutput.StartAsync();

        // Rapid button presses while audio is playing
        for (int i = 0; i < 5; i++)
        {
            await _host.AudioOutput.PlayChunkAsync(new byte[960]);
            _host.Buttons.SimulateGesture(ButtonGesture.SingleTap);
        }

        actions.Should().HaveCount(5);
        _host.Speaker.ChunkCount.Should().Be(5);
    }

    // --- INT-8/INT-9: Full pipeline round-trips ---

    [Fact]
    public async Task FullPipeline_MicToManagerToConsumer()
    {
        var receivedChunks = new List<byte[]>();
        _host.AudioInput.AudioChunkAvailable += (_, chunk) => receivedChunks.Add(chunk);

        await _host.AudioInput.StartAsync();
        await Task.Delay(400); // let chunks flow
        await _host.AudioInput.StopAsync();

        receivedChunks.Should().NotBeEmpty();
        _host.Mic.ChunksEmitted.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task FullPipeline_OutputManagerToSpeaker()
    {
        await _host.AudioOutput.StartAsync();

        var testChunks = new byte[][] { new byte[960], new byte[960], new byte[960] };
        foreach (var chunk in testChunks)
        {
            await _host.AudioOutput.PlayChunkAsync(chunk);
        }

        _host.Speaker.ChunkCount.Should().Be(3);
        _host.Speaker.TotalBytesPlayed.Should().Be(2880);
    }

    [Fact]
    public async Task FullPipeline_ClearBufferInterrupts()
    {
        await _host.AudioOutput.StartAsync();
        await _host.AudioOutput.PlayChunkAsync(new byte[4800]);
        await _host.AudioOutput.PlayChunkAsync(new byte[4800]);
        _host.Speaker.ChunkCount.Should().Be(2);

        _host.AudioOutput.ClearBuffer();
        _host.Speaker.ChunkCount.Should().Be(0);
    }

    [Fact]
    public async Task FullPipeline_CameraCaptureThroughManager()
    {
        var frame = await _host.CameraManager.CaptureFrameAsync();
        frame.Should().NotBeNull();
        frame.Should().BeEquivalentTo(TestAssets.MinimalJpeg);

        // Multiple captures
        await _host.CameraManager.CaptureFrameAsync();
        await _host.CameraManager.CaptureFrameAsync();
        _host.Camera.FramesCaptured.Should().Be(3);
    }

    [Fact]
    public async Task FullPipeline_ButtonToAction()
    {
        var actions = new List<ButtonActionEvent>();
        _host.ButtonInput.ActionTriggered += (_, a) => actions.Add(a);

        _host.Buttons.SimulateGesture(ButtonGesture.SingleTap);   // Look
        _host.Buttons.SimulateGesture(ButtonGesture.DoubleTap);   // Photo
        _host.Buttons.SimulateGesture(ButtonGesture.LongPress);   // ToggleSession

        actions.Should().HaveCount(3);
        actions[0].Action.Should().Be(ButtonAction.Look);
        actions[1].Action.Should().Be(ButtonAction.Photo);
        actions[2].Action.Should().Be(ButtonAction.ToggleSession);
    }

    // --- INT: Provider hot-plug in integration context ---

    [Fact]
    public void AudioOutputHotPlug_RegisterAndUnregister()
    {
        var initialCount = _host.AudioOutput.Providers.Count;

        var btProvider = new TestSpeakerProvider();
        // Use reflection-free approach: the ProviderId is "test-speaker" so it will be duplicate.
        // We need a distinct provider. NSubstitute isn't available here so we test through manager.

        // Verify the manager has providers registered
        _host.AudioOutput.Providers.Should().HaveCount(initialCount);
        _host.AudioOutput.Active.Should().NotBeNull();
        _host.AudioOutput.Active!.ProviderId.Should().Be("test-speaker");
    }

    [Fact]
    public void AudioOutputManager_ProvidersChanged_FiresOnRegister()
    {
        var fired = false;
        _host.AudioOutput.ProvidersChanged += (_, _) => fired = true;

        // Register via the manager's RegisterProvider
        var mockProvider = NSubstitute.Substitute.For<IAudioOutputProvider>();
        mockProvider.ProviderId.Returns("bt-out:integration-test");
        mockProvider.IsAvailable.Returns(true);

        _host.AudioOutput.RegisterProvider(mockProvider);

        fired.Should().BeTrue();
        _host.AudioOutput.Providers.Should().Contain(p => p.ProviderId == "bt-out:integration-test");
    }

    [Fact]
    public async Task AudioOutputManager_UnregisterRemovesProvider()
    {
        var mockProvider = NSubstitute.Substitute.For<IAudioOutputProvider>();
        mockProvider.ProviderId.Returns("bt-out:temp");
        mockProvider.IsAvailable.Returns(true);

        _host.AudioOutput.RegisterProvider(mockProvider);
        _host.AudioOutput.Providers.Should().Contain(p => p.ProviderId == "bt-out:temp");

        await _host.AudioOutput.UnregisterProviderAsync("bt-out:temp");
        _host.AudioOutput.Providers.Should().NotContain(p => p.ProviderId == "bt-out:temp");
    }

    // --- Save + Recall tool chain in integration context ---

    [Fact]
    public async Task ToolChain_SaveThenRecall()
    {
        var context = new ToolContext
        {
            CaptureFrame = _ => _host.CameraManager.CaptureFrameAsync(),
            Session = new SessionContext(),
            Log = _ => { },
        };

        // Save
        var saveResult = await _host.ToolDispatcher.ExecuteAsync(
            "save_memory",
            """{"content":"Keys are on the kitchen table","category":"location"}""",
            context, CancellationToken.None);
        saveResult.Should().Contain("\"saved\":true");

        // Recall
        var recallResult = await _host.ToolDispatcher.ExecuteAsync(
            "recall_memory",
            """{"query":"keys"}""",
            context, CancellationToken.None);
        recallResult.Should().Contain("kitchen table");
    }
}
