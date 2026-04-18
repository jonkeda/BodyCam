using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace BodyCam.Tests.Tools;

public class ReadTextToolTests
{
    private static (ReadTextTool tool, IChatClient chatClient) CreateTool()
    {
        var chatClient = Substitute.For<IChatClient>();
        var vision = new VisionAgent(chatClient, new AppSettings());
        return (new ReadTextTool(vision), chatClient);
    }

    private static ToolContext CreateContext(byte[]? frame = null) => new()
    {
        CaptureFrame = ct => Task.FromResult(frame),
        Session = new SessionContext(),
        Log = _ => { },
        RealtimeClient = Substitute.For<IRealtimeClient>()
    };

    [Fact]
    public async Task ExecuteAsync_WithFrame_ReturnsText()
    {
        var (tool, chatClient) = CreateTool();
        chatClient.GetResponseAsync(Arg.Any<IList<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "EXIT ONLY")));

        var ctx = CreateContext(new byte[] { 0xFF, 0xD8 });
        var result = await tool.ExecuteAsync(null, ctx, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("EXIT ONLY");
    }

    [Fact]
    public async Task ExecuteAsync_NoFrame_ReturnsFail()
    {
        var (tool, _) = CreateTool();
        var ctx = CreateContext(frame: null);
        var result = await tool.ExecuteAsync(null, ctx, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("Camera not available");
    }

    [Fact]
    public async Task ExecuteAsync_WithFocus_PassesFocusToPrompt()
    {
        var (tool, chatClient) = CreateTool();
        IList<ChatMessage>? capturedMessages = null;
        chatClient.GetResponseAsync(Arg.Any<IList<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedMessages = ci.ArgAt<IList<ChatMessage>>(0);
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Menu items"));
            });

        var ctx = CreateContext(new byte[] { 0xFF, 0xD8 });
        var argsJson = JsonHelper.ParseElement("""{ "focus":"menu"}""");
        var result = await tool.ExecuteAsync(argsJson, ctx, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedMessages.Should().NotBeNull();
        capturedMessages!.Any(m => m.Contents.OfType<TextContent>().Any(t => t.Text!.Contains("menu")))
            .Should().BeTrue();
    }

    [Fact]
    public void Name_IsReadText()
    {
        var (tool, _) = CreateTool();
        tool.Name.Should().Be("read_text");
    }
}
