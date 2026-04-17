using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tools;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class ToolBaseTests
{
    [Fact]
    public async Task ExecuteAsync_NullArgs_CreatesDefaultInstance()
    {
        var tool = new TestTool();

        await tool.ExecuteAsync(null, CreateTestContext(), CancellationToken.None);

        tool.LastArgs.Should().NotBeNull();
        tool.LastArgs!.Target.Should().Be("");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyJson_CreatesDefaultInstance()
    {
        var tool = new TestTool();

        await tool.ExecuteAsync("", CreateTestContext(), CancellationToken.None);

        tool.LastArgs.Should().NotBeNull();
        tool.LastArgs!.Target.Should().Be("");
    }

    [Fact]
    public async Task ExecuteAsync_ValidJson_DeserializesArgs()
    {
        var tool = new TestTool();

        await tool.ExecuteAsync("{\"target\":\"lamp\",\"count\":5}", CreateTestContext(), CancellationToken.None);

        tool.LastArgs.Should().NotBeNull();
        tool.LastArgs!.Target.Should().Be("lamp");
        tool.LastArgs!.Count.Should().Be(5);
    }

    [Fact]
    public void ParameterSchema_MatchesSchemaGenerator()
    {
        var tool = new TestTool();

        tool.ParameterSchema.Should().Be(SchemaGenerator.Generate<TestArgs>());
    }

    private static ToolContext CreateTestContext() => new()
    {
        CaptureFrame = (ct) => Task.FromResult<byte[]?>(null),
        Session = new SessionContext(),
        Log = _ => { },
        RealtimeClient = Substitute.For<IRealtimeClient>()
    };

    private class TestArgs
    {
        public string Target { get; set; } = "";
        public int? Count { get; set; }
    }

    private class TestTool : ToolBase<TestArgs>
    {
        public override string Name => "test_tool";
        public override string Description => "A test tool";
        public TestArgs? LastArgs { get; private set; }

        protected override Task<ToolResult> ExecuteAsync(TestArgs args, ToolContext context, CancellationToken ct)
        {
            LastArgs = args;
            return Task.FromResult(ToolResult.Success(new { ok = true }));
        }
    }
}
