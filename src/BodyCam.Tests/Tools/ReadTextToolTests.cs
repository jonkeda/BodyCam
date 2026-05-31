using BodyCam.Models;
using BodyCam.Services.Camera.Commands;
using BodyCam.Tools;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class ReadTextToolTests
{
    private static ToolContext CreateContext() => new()
    {
        CaptureFrame = _ => Task.FromResult<byte[]?>(null),
        Session = new SessionContext(),
        Log = _ => { },
    };

    [Fact]
    public async Task ExecuteAsync_DelegatesToReadCommand()
    {
        var commands = Substitute.For<ICameraCommandService>();
        commands.ExecuteAsync(Arg.Any<CameraCommandRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CameraCommandResult(
                "read",
                true,
                "EXIT ONLY",
                new Dictionary<string, object?> { ["text"] = "EXIT ONLY" },
                null));

        var tool = new ReadTextTool(commands);
        var result = await tool.ExecuteAsync(null, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Json.Should().Contain("EXIT ONLY");
        await commands.Received(1).ExecuteAsync(
            Arg.Is<CameraCommandRequest>(r =>
                r.CommandId == "read"
                && r.Origin == CommandTriggerOrigin.LlmToolCall),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithFocus_PassesFocusToOptions()
    {
        var commands = Substitute.For<ICameraCommandService>();
        CameraCommandRequest? captured = null;
        commands.ExecuteAsync(Arg.Do<CameraCommandRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new CameraCommandResult("read", true, "Menu items", new { text = "Menu items" }, null));

        var tool = new ReadTextTool(commands);
        var argsJson = JsonHelper.ParseElement("""{ "focus":"menu"}""");
        await tool.ExecuteAsync(argsJson, CreateContext(), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Options.Should().BeOfType<ReadCommandOptions>();
        ((ReadCommandOptions)captured.Options!).Focus.Should().Be("menu");
    }

    [Fact]
    public async Task ExecuteAsync_CommandFailure_ReturnsFail()
    {
        var commands = Substitute.For<ICameraCommandService>();
        commands.ExecuteAsync(Arg.Any<CameraCommandRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CameraCommandResult("read", false, "Camera not available.", null, "Camera not available."));

        var tool = new ReadTextTool(commands);
        var result = await tool.ExecuteAsync(null, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("Camera not available");
    }

    [Fact]
    public void Name_IsReadText()
    {
        var tool = new ReadTextTool(Substitute.For<ICameraCommandService>());

        tool.Name.Should().Be("read_text");
    }
}
