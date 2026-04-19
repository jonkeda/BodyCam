using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tests.TestInfrastructure;
using BodyCam.Tools;
using FluentAssertions;

namespace BodyCam.Tests.Integration;

public class ToolPipelineTests : IAsyncLifetime
{
    private readonly BodyCamTestHost _host = BodyCamTestHost.Create();
    private ToolContext _context = null!;

    public async Task InitializeAsync()
    {
        await _host.InitializeAsync();
        _context = new ToolContext
        {
            CaptureFrame = _ => _host.CameraManager.CaptureFrameAsync(),
            Session = new SessionContext(),
            Log = _ => { },
        };
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task SaveMemory_ThenRecall_RoundTrips()
    {
        await _host.ToolDispatcher.ExecuteAsync(
            "save_memory", """{"content":"The red cup is on the shelf","category":"item"}""",
            _context, CancellationToken.None);

        var result = await _host.ToolDispatcher.ExecuteAsync(
            "recall_memory", """{"query":"red cup"}""",
            _context, CancellationToken.None);

        result.Should().Contain("red cup");
        result.Should().Contain("\"found\":true");
    }

    [Fact]
    public async Task SaveMemory_WithEmptyContent_Fails()
    {
        var result = await _host.ToolDispatcher.ExecuteAsync(
            "save_memory", """{"content":""}""",
            _context, CancellationToken.None);

        result.Should().Contain("error");
    }

    [Fact]
    public async Task RecallMemory_NoResults_ReturnsNotFound()
    {
        var result = await _host.ToolDispatcher.ExecuteAsync(
            "recall_memory", """{"query":"nonexistent item xyz123"}""",
            _context, CancellationToken.None);

        result.Should().Contain("\"found\":false");
    }

    [Fact]
    public async Task UnknownTool_ReturnsError()
    {
        var result = await _host.ToolDispatcher.ExecuteAsync(
            "nonexistent_tool", null, _context, CancellationToken.None);

        result.Should().Contain("Unknown function");
    }

    [Fact]
    public async Task ToolDefinitions_ContainRegisteredTools()
    {
        var defs = _host.ToolDispatcher.GetToolDefinitions();

        defs.Should().Contain(d => d.Name == "save_memory");
        defs.Should().Contain(d => d.Name == "recall_memory");
    }

    [Fact]
    public async Task CaptureFrame_UsesTestCamera()
    {
        var frame = await _context.CaptureFrame(CancellationToken.None);

        frame.Should().NotBeNull();
        frame.Should().BeEquivalentTo(TestAssets.MinimalJpeg);
        _host.Camera.FramesCaptured.Should().Be(1);
    }
}
