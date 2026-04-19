using BodyCam.Agents;
using BodyCam.Services;
using BodyCam.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class FindObjectToolTests
{
    private static ToolContext CreateContext(byte[]? frame = null) => new()
    {
        CaptureFrame = ct => Task.FromResult(frame),
        Session = new BodyCam.Models.SessionContext(),
        Log = _ => { },
    };

    [Fact]
    public async Task ExecuteAsync_EmptyTarget_ReturnsFail()
    {
        var chatClient = Substitute.For<IChatClient>();
        var vision = new VisionAgent(chatClient, new AppSettings());
        var tool = new FindObjectTool(vision);

        var result = await tool.ExecuteAsync(null, CreateContext(), CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_FoundOnFirstScan_ReturnsSuccess()
    {
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "FOUND - It's on the left side of the table.")));

        var vision = new VisionAgent(chatClient, new AppSettings());
        var tool = new FindObjectTool(vision) { TimeoutSeconds = 5, ScanIntervalSeconds = 1 };

        var frame = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // fake JPEG header
        var result = await tool.ExecuteAsync(
            JsonHelper.ParseElement("{\"target\":\"red mug\"}"), CreateContext(frame), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("found");
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_ReturnsNotFound()
    {
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "NOT_FOUND - I don't see it.")));

        var vision = new VisionAgent(chatClient, new AppSettings());
        var tool = new FindObjectTool(vision) { TimeoutSeconds = 1, ScanIntervalSeconds = 1 };

        var frame = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var result = await tool.ExecuteAsync(
            JsonHelper.ParseElement("{\"target\":\"unicorn\"}"), CreateContext(frame), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(); // Success with found=false
        result.Json.Should().Contain("false");
    }

    [Fact]
    public void Name_IsFindObject()
    {
        var chatClient = Substitute.For<IChatClient>();
        var vision = new VisionAgent(chatClient, new AppSettings());
        var tool = new FindObjectTool(vision);
        tool.Name.Should().Be("find_object");
    }
}
