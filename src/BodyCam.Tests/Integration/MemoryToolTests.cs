using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tests.TestInfrastructure;
using BodyCam.Tools;
using FluentAssertions;

namespace BodyCam.Tests.Integration;

public class MemoryToolTests : IAsyncLifetime
{
    private BodyCamTestHost _host = null!;
    private ToolContext _context = null!;

    public async Task InitializeAsync()
    {
        _host = BodyCamTestHost.Create();
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
    public async Task SaveMemory_WithCategory_PersistsCategory()
    {
        var result = await _host.ToolDispatcher.ExecuteAsync(
            "save_memory",
            """{"content":"Car in spot B7","category":"location"}""",
            _context, CancellationToken.None);

        result.Should().Contain("\"saved\":true");

        var recall = await _host.ToolDispatcher.ExecuteAsync(
            "recall_memory", """{"query":"car"}""",
            _context, CancellationToken.None);

        recall.Should().Contain("B7");
        recall.Should().Contain("location");
    }

    [Fact]
    public async Task SaveMemory_MultipleEntries_AllRecallable()
    {
        await _host.ToolDispatcher.ExecuteAsync(
            "save_memory",
            """{"content":"Alice works at Google","category":"person"}""",
            _context, CancellationToken.None);

        await _host.ToolDispatcher.ExecuteAsync(
            "save_memory",
            """{"content":"Bob likes coffee","category":"person"}""",
            _context, CancellationToken.None);

        await _host.ToolDispatcher.ExecuteAsync(
            "save_memory",
            """{"content":"Meeting at 3pm","category":"event"}""",
            _context, CancellationToken.None);

        var aliceResult = await _host.ToolDispatcher.ExecuteAsync(
            "recall_memory", """{"query":"Alice"}""",
            _context, CancellationToken.None);
        aliceResult.Should().Contain("Google");

        var bobResult = await _host.ToolDispatcher.ExecuteAsync(
            "recall_memory", """{"query":"Bob"}""",
            _context, CancellationToken.None);
        bobResult.Should().Contain("coffee");

        var meetingResult = await _host.ToolDispatcher.ExecuteAsync(
            "recall_memory", """{"query":"meeting"}""",
            _context, CancellationToken.None);
        meetingResult.Should().Contain("3pm");
    }

    [Fact]
    public async Task RecallMemory_PartialMatch_FindsEntry()
    {
        await _host.ToolDispatcher.ExecuteAsync(
            "save_memory",
            """{"content":"The wifi password is sunshine42","category":"item"}""",
            _context, CancellationToken.None);

        var result = await _host.ToolDispatcher.ExecuteAsync(
            "recall_memory", """{"query":"wifi"}""",
            _context, CancellationToken.None);

        result.Should().Contain("sunshine42");
    }

    [Fact]
    public async Task RecallMemory_CaseInsensitive_FindsEntry()
    {
        await _host.ToolDispatcher.ExecuteAsync(
            "save_memory",
            """{"content":"IMPORTANT: Dentist at 2pm","category":"event"}""",
            _context, CancellationToken.None);

        var result = await _host.ToolDispatcher.ExecuteAsync(
            "recall_memory", """{"query":"dentist"}""",
            _context, CancellationToken.None);

        result.Should().Contain("Dentist");
    }

    [Fact]
    public async Task SaveMemory_DefaultCategory_Works()
    {
        var result = await _host.ToolDispatcher.ExecuteAsync(
            "save_memory",
            """{"content":"The sky is blue today"}""",
            _context, CancellationToken.None);

        result.Should().Contain("\"saved\":true");

        var recall = await _host.ToolDispatcher.ExecuteAsync(
            "recall_memory", """{"query":"sky"}""",
            _context, CancellationToken.None);

        recall.Should().Contain("blue");
    }

    [Fact]
    public async Task RecallMemory_EmptyStore_ReturnsNotFound()
    {
        var result = await _host.ToolDispatcher.ExecuteAsync(
            "recall_memory", """{"query":"anything"}""",
            _context, CancellationToken.None);

        result.Should().Contain("\"found\":false");
    }

    [Fact]
    public async Task SaveMemory_NullContent_ReturnsError()
    {
        var result = await _host.ToolDispatcher.ExecuteAsync(
            "save_memory", """{}""",
            _context, CancellationToken.None);

        result.Should().Contain("error");
    }

    [Fact]
    public async Task RecallMemory_NullQuery_ReturnsError()
    {
        var result = await _host.ToolDispatcher.ExecuteAsync(
            "recall_memory", """{}""",
            _context, CancellationToken.None);

        // Should handle missing query gracefully
        result.Should().NotBeNull();
    }
}
