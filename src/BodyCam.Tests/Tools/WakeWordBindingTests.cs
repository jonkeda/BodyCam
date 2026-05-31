using BodyCam.Agents;
using BodyCam.Services;
using BodyCam.Services.Camera.Commands;
using BodyCam.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class WakeWordBindingTests
{
    [Fact]
    public void DescribeSceneTool_HasNoWakeWord()
    {
        var chatClient = Substitute.For<IChatClient>();
        var vision = new VisionAgent(chatClient, new AppSettings());
        var tool = new DescribeSceneTool(vision);

        tool.WakeWord.Should().BeNull();
    }

    [Fact]
    public void LookTool_HasWakeWord()
    {
        var tool = new LookTool(Substitute.For<ICameraCommandService>());

        tool.WakeWord.Should().NotBeNull();
        tool.WakeWord!.KeywordPath.Should().Contain("bodycam-look");
        tool.WakeWord.Mode.Should().Be(WakeWordMode.QuickAction);
    }

    [Fact]
    public void DeepAnalysisTool_HasNoWakeWord()
    {
        var chatClient = Substitute.For<IChatClient>();
        var conversation = new ConversationAgent(chatClient, new AppSettings());
        var tool = new DeepAnalysisTool(conversation);

        tool.WakeWord.Should().BeNull();
    }

    [Fact]
    public void BuildWakeWordEntries_IncludesSystemKeywords()
    {
        var dispatcher = new ToolDispatcher(Array.Empty<ITool>());
        var entries = dispatcher.BuildWakeWordEntries();

        entries.Should().Contain(e => e.Label == "Hey BodyCam" && e.Action == WakeWordAction.StartSession);
        entries.Should().Contain(e => e.Label == "Go to sleep" && e.Action == WakeWordAction.GoToSleep);
    }

    [Fact]
    public void BuildWakeWordEntries_IncludesToolWithWakeWord()
    {
        var lookTool = new LookTool(Substitute.For<ICameraCommandService>());
        var dispatcher = new ToolDispatcher(new ITool[] { lookTool });

        var entries = dispatcher.BuildWakeWordEntries();

        entries.Should().Contain(e => e.ToolName == "look" && e.Action == WakeWordAction.InvokeTool);
    }

    [Fact]
    public void BuildWakeWordEntries_ExcludesToolWithoutWakeWord()
    {
        var chatClient = Substitute.For<IChatClient>();
        var conversation = new ConversationAgent(chatClient, new AppSettings());
        var deepTool = new DeepAnalysisTool(conversation);
        var dispatcher = new ToolDispatcher(new ITool[] { deepTool });

        var entries = dispatcher.BuildWakeWordEntries();

        entries.Should().NotContain(e => e.ToolName == "deep_analysis");
    }

    [Fact]
    public void BuildWakeWordEntries_NoDuplicateKeywordPaths()
    {
        var lookTool = new LookTool(Substitute.For<ICameraCommandService>());
        var dispatcher = new ToolDispatcher(new ITool[] { lookTool });

        var entries = dispatcher.BuildWakeWordEntries();
        var paths = entries.Select(e => e.KeywordPath).ToList();

        paths.Should().OnlyHaveUniqueItems();
    }
}
