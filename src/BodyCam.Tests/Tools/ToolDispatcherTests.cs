using BodyCam.Tools;
using FluentAssertions;
using System.Text.Json;

namespace BodyCam.Tests.Tools;

public class ToolDispatcherTests
{
    private class FakeTool : ITool
    {
        public string Name { get; set; } = "fake";
        public string Description { get; set; } = "A fake tool";
        public string ParameterSchema => """{"type":"object","properties":{}}""";
        public bool IsEnabled { get; set; } = true;
        public JsonElement? LastArgs { get; private set; }

        public Task<ToolResult> ExecuteAsync(JsonElement? arguments, ToolContext context, CancellationToken ct)
        {
            LastArgs = arguments;
            return Task.FromResult(ToolResult.Success(new { result = "ok" }));
        }
    }

    private static ToolContext CreateContext() => new()
    {
        CaptureFrame = ct => Task.FromResult<byte[]?>(null),
        Session = new BodyCam.Models.SessionContext(),
        Log = _ => { },
        RealtimeClient = NSubstitute.Substitute.For<BodyCam.Services.IRealtimeClient>()
    };

    [Fact]
    public async Task ExecuteAsync_KnownTool_RoutesCorrectly()
    {
        var tool = new FakeTool { Name = "my_tool" };
        var dispatcher = new ToolDispatcher([tool]);

        var result = await dispatcher.ExecuteAsync("my_tool", """{"x":1}""", CreateContext(), CancellationToken.None);

        tool.LastArgs.Should().NotBeNull();
        tool.LastArgs!.Value.GetProperty("x").GetInt32().Should().Be(1);
        result.Should().Contain("ok");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTool_ReturnsError()
    {
        var dispatcher = new ToolDispatcher([]);
        var result = await dispatcher.ExecuteAsync("nonexistent", null, CreateContext(), CancellationToken.None);

        result.Should().Contain("Unknown function");
    }

    [Fact]
    public async Task ExecuteAsync_DisabledTool_ReturnsError()
    {
        var tool = new FakeTool { Name = "disabled_tool", IsEnabled = false };
        var dispatcher = new ToolDispatcher([tool]);

        var result = await dispatcher.ExecuteAsync("disabled_tool", null, CreateContext(), CancellationToken.None);

        result.Should().Contain("disabled");
    }

    [Fact]
    public void GetToolDefinitions_ReturnsOnlyEnabled()
    {
        var enabled = new FakeTool { Name = "enabled", IsEnabled = true };
        var disabled = new FakeTool { Name = "disabled", IsEnabled = false };
        var dispatcher = new ToolDispatcher([enabled, disabled]);

        var defs = dispatcher.GetToolDefinitions();

        defs.Should().HaveCount(1);
        defs[0].Name.Should().Be("enabled");
    }

    [Fact]
    public void GetToolDefinitions_IncludesNameDescriptionSchema()
    {
        var tool = new FakeTool { Name = "test", Description = "Test tool" };
        var dispatcher = new ToolDispatcher([tool]);

        var defs = dispatcher.GetToolDefinitions();

        defs[0].Name.Should().Be("test");
        defs[0].Description.Should().Be("Test tool");
        defs[0].ParametersJson.Should().Contain("object");
    }

    [Fact]
    public void GetTool_KnownName_ReturnsTool()
    {
        var tool = new FakeTool { Name = "finder" };
        var dispatcher = new ToolDispatcher([tool]);

        dispatcher.GetTool("finder").Should().Be(tool);
    }

    [Fact]
    public void GetTool_UnknownName_ReturnsNull()
    {
        var dispatcher = new ToolDispatcher([]);
        dispatcher.GetTool("nope").Should().BeNull();
    }
}
