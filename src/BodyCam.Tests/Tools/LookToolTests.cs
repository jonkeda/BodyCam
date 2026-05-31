using BodyCam.Models;
using BodyCam.Services.Camera.Commands;
using BodyCam.Tools;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace BodyCam.Tests.Tools;

public class LookToolTests
{
    [Fact]
    public async Task ExecuteAsync_DelegatesToLookCommand()
    {
        var commands = Substitute.For<ICameraCommandService>();
        commands.ExecuteAsync(Arg.Any<CameraCommandRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CameraCommandResult(
                "look",
                true,
                "A desk is ahead.",
                new Dictionary<string, object?> { ["description"] = "A desk is ahead." },
                null));

        var tool = new LookTool(commands);
        var context = TestToolContext.Create();

        var result = await tool.ExecuteAsync(null, context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("A desk is ahead");
        await commands.Received(1).ExecuteAsync(
            Arg.Is<CameraCommandRequest>(r =>
                r.CommandId == "look"
                && r.Origin == CommandTriggerOrigin.LlmToolCall),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_CommandFailure_ReturnsFail()
    {
        var commands = Substitute.For<ICameraCommandService>();
        commands.ExecuteAsync(Arg.Any<CameraCommandRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CameraCommandResult("look", false, "Camera not available.", null, "Camera not available."));

        var tool = new LookTool(commands);

        var result = await tool.ExecuteAsync(null, TestToolContext.Create(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("Camera not available");
    }

    [Fact]
    public void Name_IsLook()
    {
        var tool = new LookTool(Substitute.For<ICameraCommandService>());

        tool.Name.Should().Be("look");
    }

    [Fact]
    public void WakeWord_IsLook()
    {
        var tool = new LookTool(Substitute.For<ICameraCommandService>());

        tool.WakeWord.Should().NotBeNull();
        tool.WakeWord!.KeywordPath.Should().Contain("look");
    }
}

internal static class TestToolContext
{
    public static ToolContext Create(CommandTriggerOrigin? origin = null) => new()
    {
        CaptureFrame = _ => Task.FromResult<byte[]?>(null),
        Session = new SessionContext(),
        Log = _ => { },
        CommandOrigin = origin,
    };
}
