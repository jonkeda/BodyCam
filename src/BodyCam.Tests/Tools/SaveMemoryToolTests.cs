using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tools;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class SaveMemoryToolTests
{
    private static ToolContext CreateContext() => new()
    {
        CaptureFrame = ct => Task.FromResult<byte[]?>(null),
        Session = new SessionContext(),
        Log = _ => { },
        RealtimeClient = Substitute.For<IRealtimeClient>()
    };

    [Fact]
    public async Task ExecuteAsync_WithContent_SavesMemory()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var store = new MemoryStore(tempFile);
            var tool = new SaveMemoryTool(store);
            var argsJson = """{"content":"Buy milk","category":"general"}""";

            var result = await tool.ExecuteAsync(argsJson, CreateContext(), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Json.Should().Contain("Buy milk");
            store.Count.Should().Be(1);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_EmptyContent_ReturnsFail()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var store = new MemoryStore(tempFile);
            var tool = new SaveMemoryTool(store);
            var argsJson = """{"content":""}""";

            var result = await tool.ExecuteAsync(argsJson, CreateContext(), CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Name_IsSaveMemory()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var store = new MemoryStore(tempFile);
            var tool = new SaveMemoryTool(store);
            tool.Name.Should().Be("save_memory");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
