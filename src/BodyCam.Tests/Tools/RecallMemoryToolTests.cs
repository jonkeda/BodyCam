using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tools;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class RecallMemoryToolTests
{
    private static ToolContext CreateContext() => new()
    {
        CaptureFrame = ct => Task.FromResult<byte[]?>(null),
        Session = new SessionContext(),
        Log = _ => { },
    };

    [Fact]
    public async Task ExecuteAsync_WithMatches_ReturnsMemories()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var store = new MemoryStore(tempFile);
            await store.SaveAsync(new MemoryEntry { Content = "My red car is parked on level 3" });
            await store.SaveAsync(new MemoryEntry { Content = "Meeting at 2pm" });

            var tool = new RecallMemoryTool(store);
            var argsJson = JsonHelper.ParseElement("""{ "query":"car"}""");

            var result = await tool.ExecuteAsync(argsJson, CreateContext(), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Json.Should().Contain("red car");
            result.Json.Should().Contain("\"found\":true");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NoMatches_ReturnsNotFound()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var store = new MemoryStore(tempFile);
            var tool = new RecallMemoryTool(store);
            var argsJson = JsonHelper.ParseElement("""{ "query":"something"}""");

            var result = await tool.ExecuteAsync(argsJson, CreateContext(), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Json.Should().Contain("\"found\":false");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
