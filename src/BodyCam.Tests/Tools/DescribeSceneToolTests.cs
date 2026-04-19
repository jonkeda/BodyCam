using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace BodyCam.Tests.Tools;

public class DescribeSceneToolTests
{
    private static (DescribeSceneTool tool, IChatClient chatClient) CreateTool()
    {
        var chatClient = Substitute.For<IChatClient>();
        var vision = new VisionAgent(chatClient, new AppSettings());
        return (new DescribeSceneTool(vision), chatClient);
    }

    private static ToolContext CreateContext(byte[]? frame = null) => new()
    {
        CaptureFrame = (ct) => Task.FromResult(frame),
        Session = new SessionContext(),
        Log = _ => { },
    };

    [Fact]
    public async Task ExecuteAsync_WithFrame_ReturnsDescription()
    {
        var (tool, chatClient) = CreateTool();
        chatClient.GetResponseAsync(Arg.Any<IList<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "A bright room")));

        var ctx = CreateContext(new byte[] { 0xFF, 0xD8 });
        var result = await tool.ExecuteAsync(null, ctx, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("A bright room");
    }

    [Fact]
    public async Task ExecuteAsync_NoFrame_ReturnsFallbackMessage()
    {
        var (tool, _) = CreateTool();
        var ctx = CreateContext(frame: null);
        var result = await tool.ExecuteAsync(null, ctx, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("Camera not available");
    }

    [Fact]
    public async Task ExecuteAsync_WithQuery_PassesQueryToVision()
    {
        var (tool, chatClient) = CreateTool();
        chatClient.GetResponseAsync(Arg.Any<IList<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "It's a book")));

        var ctx = CreateContext(new byte[] { 0xFF, 0xD8 });
        var argsJson = JsonHelper.ParseElement("""{ "query":"What is that object?"}""");
        var result = await tool.ExecuteAsync(argsJson, ctx, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("book");
    }

    [Fact]
    public void Name_IsDescribeScene()
    {
        var (tool, _) = CreateTool();
        tool.Name.Should().Be("describe_scene");
    }

    [Fact]
    public void ParameterSchema_HasQueryProperty()
    {
        var (tool, _) = CreateTool();
        tool.ParameterSchema.Should().Contain("query");
    }
}
