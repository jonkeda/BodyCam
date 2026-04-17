using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tools;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class SetTranslationModeToolTests
{
    private static ToolContext CreateContext() => new()
    {
        CaptureFrame = ct => Task.FromResult<byte[]?>(null),
        Session = new SessionContext(),
        Log = _ => { },
        RealtimeClient = Substitute.For<IRealtimeClient>()
    };

    [Fact]
    public async Task ExecuteAsync_Activate_ModifiesSession()
    {
        var tool = new SetTranslationModeTool();
        var ctx = CreateContext();
        var argsJson = """{"targetLanguage":"Spanish","active":true}""";

        var result = await tool.ExecuteAsync(argsJson, ctx, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("Spanish");
        ctx.Session.SystemPrompt.Should().Contain("Spanish");
    }

    [Fact]
    public async Task ExecuteAsync_NoLanguage_ReturnsFail()
    {
        var tool = new SetTranslationModeTool();
        var argsJson = """{"active":true,"targetLanguage":""}""";

        var result = await tool.ExecuteAsync(argsJson, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("Target language is required");
    }
}
