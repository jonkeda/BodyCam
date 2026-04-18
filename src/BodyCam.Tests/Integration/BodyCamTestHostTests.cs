using BodyCam.Services.Audio;
using BodyCam.Services.Input;
using BodyCam.Tests.TestInfrastructure;
using FluentAssertions;

namespace BodyCam.Tests.Integration;

public class BodyCamTestHostTests : IAsyncDisposable
{
    private readonly BodyCamTestHost _host = BodyCamTestHost.Create();

    [Fact]
    public void Create_BuildsServiceGraph()
    {
        _host.Mic.Should().NotBeNull();
        _host.Speaker.Should().NotBeNull();
        _host.Camera.Should().NotBeNull();
        _host.Buttons.Should().NotBeNull();
        _host.ToolDispatcher.Should().NotBeNull();
    }

    [Fact]
    public async Task InitializeAsync_SetsActiveProviders()
    {
        await _host.InitializeAsync();

        _host.AudioInput.Active.Should().NotBeNull();
        _host.AudioOutput.Active.Should().NotBeNull();
    }

    [Fact]
    public async Task AudioInput_RoutesChunks()
    {
        await _host.InitializeAsync();
        var received = new List<byte[]>();
        _host.AudioInput.AudioChunkAvailable += (_, chunk) => received.Add(chunk);

        await _host.AudioInput.StartAsync();
        await Task.Delay(300);
        await _host.AudioInput.StopAsync();

        received.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AudioOutput_CapturesPlayback()
    {
        await _host.InitializeAsync();
        await _host.AudioOutput.StartAsync();

        await _host.AudioOutput.PlayChunkAsync(new byte[] { 1, 2, 3 });

        _host.Speaker.ChunkCount.Should().Be(1);
        _host.Speaker.TotalBytesPlayed.Should().Be(3);
    }

    [Fact]
    public async Task Camera_CapturesFrames()
    {
        await _host.InitializeAsync();

        var frame = await _host.CameraManager.CaptureFrameAsync();

        frame.Should().NotBeNull();
        frame.Should().BeEquivalentTo(TestAssets.MinimalJpeg);
    }

    [Fact]
    public async Task Buttons_DispatchActions()
    {
        await _host.InitializeAsync();
        var actions = new List<ButtonActionEvent>();
        _host.ButtonInput.ActionTriggered += (_, a) => actions.Add(a);

        // PreRecognizedGesture bypasses GestureRecognizer → goes directly to ActionMap
        _host.Buttons.SimulateGesture(ButtonGesture.SingleTap);

        actions.Should().ContainSingle();
        actions[0].Action.Should().Be(ButtonAction.Look);
    }

    [Fact]
    public async Task ToolDispatcher_ExecutesSaveMemory()
    {
        await _host.InitializeAsync();

        var context = new BodyCam.Tools.ToolContext
        {
            CaptureFrame = _ => Task.FromResult<byte[]?>(TestAssets.MinimalJpeg),
            Session = new BodyCam.Models.SessionContext(),
            Log = _ => { },
            RealtimeClient = _host.Services.GetService(typeof(BodyCam.Services.IRealtimeClient)) as BodyCam.Services.IRealtimeClient
                ?? throw new InvalidOperationException(),
        };

        var result = await _host.ToolDispatcher.ExecuteAsync(
            "save_memory", """{"content":"test note"}""", context, CancellationToken.None);

        result.Should().Contain("\"saved\":true");
    }

    [Fact]
    public void Create_AllowsCustomRegistrations()
    {
        using var host = BodyCamTestHost.Create(services =>
        {
            services.AddSingleton("custom-value");
        });

        host.Services.GetService<string>().Should().Be("custom-value");
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
    }
}
