using BodyCam;
using BodyCam.Agents;
using BodyCam.Services;
using BodyCam.Services.Camera.Commands;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace BodyCam.Tests.Services.Camera.Commands;

public class ReadCommandTests
{
    [Fact]
    public void ResolveOptions_UsesSettingsDefaultDetail()
    {
        var command = CreateCommand(out _);
        var settings = CreateSettings();
        settings.DefaultReadDetailLevel.Returns(ReadDetailLevel.Overview);
        var context = CreateContext(settings, options: null);

        var options = command.ResolveOptions(context);

        options.DetailLevel.Should().Be(ReadDetailLevel.Overview);
    }

    [Fact]
    public async Task ExecuteAsync_NoText_ReturnsFriendlyTranscript()
    {
        var command = CreateCommand(out var chatClient);
        chatClient.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "NO_TEXT")));

        var result = await command.ExecuteAsync(
            CreateContext(CreateSettings(), new ReadCommandOptions(ReadDetailLevel.Full, null)),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.TranscriptText.Should().Be("No text detected.");
    }

    private static ReadCommand CreateCommand(out IChatClient chatClient)
    {
        chatClient = Substitute.For<IChatClient>();
        return new ReadCommand(new VisionAgent(chatClient, new AppSettings()));
    }

    private static CameraCommandContext CreateContext(ISettingsService settings, object? options) =>
        new(
            new CameraCommandRequest("read", CameraCommandMode.FullAuto, CommandTriggerOrigin.Automation, options, null),
            CameraCommandMode.FullAuto,
            null!,
            settings,
            _ => Task.FromResult<byte[]?>([0xFF, 0xD8]),
            _ => Task.FromResult<byte[]?>([0xFF, 0xD8]));

    private static ISettingsService CreateSettings()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.DefaultTouchCommandMode.Returns(CameraCommandMode.ManualAim);
        settings.DefaultLookDetailLevel.Returns(LookDetailLevel.Summary);
        settings.DefaultReadDetailLevel.Returns(ReadDetailLevel.Full);
        settings.ConfirmExternalScanActions.Returns(true);
        return settings;
    }
}
