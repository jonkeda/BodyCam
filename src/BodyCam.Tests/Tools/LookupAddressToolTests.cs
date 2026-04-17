using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tools;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class LookupAddressToolTests
{
    private static ToolContext CreateContext() => new()
    {
        CaptureFrame = ct => Task.FromResult<byte[]?>(null),
        Session = new SessionContext(),
        Log = _ => { },
        RealtimeClient = Substitute.For<IRealtimeClient>()
    };

    [Fact]
    public async Task ExecuteAsync_WithQuery_ReturnsPassthrough()
    {
        var tool = new LookupAddressTool();
        var argsJson = """{"query":"Eiffel Tower"}""";

        var result = await tool.ExecuteAsync(argsJson, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("Eiffel Tower");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyQuery_ReturnsFail()
    {
        var tool = new LookupAddressTool();
        var argsJson = """{"query":""}""";

        var result = await tool.ExecuteAsync(argsJson, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("Search query is required");
    }
}
