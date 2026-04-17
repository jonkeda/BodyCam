using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tools;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class SendMessageToolTests
{
    private static ToolContext CreateContext() => new()
    {
        CaptureFrame = ct => Task.FromResult<byte[]?>(null),
        Session = new SessionContext(),
        Log = _ => { },
        RealtimeClient = Substitute.For<IRealtimeClient>()
    };

    [Fact]
    public async Task ExecuteAsync_EmptyRecipient_ReturnsFail()
    {
        var tool = new SendMessageTool();
        var argsJson = """{"recipient":"","message":"Hello"}""";

        var result = await tool.ExecuteAsync(argsJson, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("Recipient is required");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyMessage_ReturnsFail()
    {
        var tool = new SendMessageTool();
        var argsJson = """{"recipient":"555-1234","message":""}""";

        var result = await tool.ExecuteAsync(argsJson, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("Message text is required");
    }

    [Fact]
    public void Name_IsSendMessage()
    {
        var tool = new SendMessageTool();
        tool.Name.Should().Be("send_message");
    }
}
