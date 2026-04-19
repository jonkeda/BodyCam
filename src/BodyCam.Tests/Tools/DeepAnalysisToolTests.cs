using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace BodyCam.Tests.Tools;

public class DeepAnalysisToolTests
{
    private static (DeepAnalysisTool tool, IChatClient chatClient) CreateTool()
    {
        var chatClient = Substitute.For<IChatClient>();
        var conversation = new ConversationAgent(chatClient, new AppSettings());
        return (new DeepAnalysisTool(conversation), chatClient);
    }

    private static ToolContext CreateContext() => new()
    {
        CaptureFrame = (ct) => Task.FromResult<byte[]?>(null),
        Session = new SessionContext(),
        Log = _ => { },
    };

    [Fact]
    public async Task ExecuteAsync_WithQuery_ReturnsAnalysis()
    {
        var (tool, chatClient) = CreateTool();
        chatClient.GetResponseAsync(Arg.Any<IList<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Deep insight here")));

        var argsJson = JsonHelper.ParseElement("""{ "query":"Explain quantum computing"}""" );
        var result = await tool.ExecuteAsync(argsJson, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("Deep insight");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyQuery_ReturnsFail()
    {
        var (tool, _) = CreateTool();
        var argsJson = JsonHelper.ParseElement("""{ "query":""}""" );
        var result = await tool.ExecuteAsync(argsJson, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("error");
    }

    [Fact]
    public void Name_IsDeepAnalysis()
    {
        var (tool, _) = CreateTool();
        tool.Name.Should().Be("deep_analysis");
    }

    [Fact]
    public void ParameterSchema_HasQueryRequired()
    {
        var (tool, _) = CreateTool();
        tool.ParameterSchema.Should().Contain("query");
        tool.ParameterSchema.Should().Contain("required");
    }
}
