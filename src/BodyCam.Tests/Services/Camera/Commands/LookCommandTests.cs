using BodyCam;
using BodyCam.Agents;
using BodyCam.Services;
using BodyCam.Services.Camera;
using BodyCam.Services.Camera.Commands;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace BodyCam.Tests.Services.Camera.Commands;

public class LookCommandTests
{
    [Fact]
    public void BuildPrompt_Summary_RequestsShortUsefulAnswer()
    {
        var prompt = LookCommand.BuildPrompt(new LookCommandOptions(
            LookDetailLevel.Summary,
            Focus: null,
            Question: null));

        prompt.Should().Contain("one or two sentences");
        prompt.Should().Contain("Safety-relevant observations come first");
    }

    [Fact]
    public void BuildPrompt_Detailed_IncludesSpatialAndConfidenceGuidance()
    {
        var prompt = LookCommand.BuildPrompt(new LookCommandOptions(
            LookDetailLevel.Detailed,
            Focus: "door",
            Question: "Is it open?"));

        prompt.Should().Contain("structured scene description");
        prompt.Should().Contain("left, right, ahead");
        prompt.Should().Contain("door");
        prompt.Should().Contain("Is it open?");
    }

    [Fact]
    public void ResolveOptions_UsesSettingsDefaultDetail()
    {
        var command = CreateCommand(out _);
        var settings = CreateSettings();
        settings.DefaultLookDetailLevel.Returns(LookDetailLevel.Full);
        var context = CreateContext(settings, options: null);

        var options = command.ResolveOptions(context);

        options.DetailLevel.Should().Be(LookDetailLevel.Full);
    }

    [Fact]
    public async Task ExecuteAsync_FullAuto_CapturesAndDescribes()
    {
        var command = CreateCommand(out var chatClient);
        chatClient.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "A chair is ahead.")));

        var captured = 0;
        var context = CreateContext(
            CreateSettings(),
            options: new LookCommandOptions(LookDetailLevel.Overview, null, null),
            mode: CameraCommandMode.FullAuto,
            captureFrame: _ =>
            {
                captured++;
                return Task.FromResult<byte[]?>([0xFF, 0xD8]);
            });

        var result = await command.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.TranscriptText.Should().Be("A chair is ahead.");
        captured.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ManualAim_UsesManualCapture()
    {
        var command = CreateCommand(out var chatClient);
        chatClient.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Manual frame described.")));

        var autoCaptures = 0;
        var manualCaptures = 0;
        var context = CreateContext(
            CreateSettings(),
            options: new LookCommandOptions(LookDetailLevel.Summary, null, null),
            mode: CameraCommandMode.ManualAim,
            captureFrame: _ =>
            {
                autoCaptures++;
                return Task.FromResult<byte[]?>(null);
            },
            waitForManualCapture: _ =>
            {
                manualCaptures++;
                return Task.FromResult<byte[]?>([0xFF, 0xD8]);
            });

        var result = await command.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        autoCaptures.Should().Be(0);
        manualCaptures.Should().Be(1);
    }

    private static LookCommand CreateCommand(out IChatClient chatClient)
    {
        chatClient = Substitute.For<IChatClient>();
        return new LookCommand(new VisionAgent(chatClient, new AppSettings()));
    }

    private static ISettingsService CreateSettings()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.DefaultTouchCommandMode.Returns(CameraCommandMode.ManualAim);
        settings.DefaultLookDetailLevel.Returns(LookDetailLevel.Summary);
        settings.DefaultReadDetailLevel.Returns(ReadDetailLevel.Full);
        settings.ConfirmExternalScanActions.Returns(true);
        return settings;
    }

    private static CameraCommandContext CreateContext(
        ISettingsService settings,
        object? options,
        CameraCommandMode mode = CameraCommandMode.FullAuto,
        Func<CancellationToken, Task<byte[]?>>? captureFrame = null,
        Func<CancellationToken, Task<byte[]?>>? waitForManualCapture = null)
    {
        var request = new CameraCommandRequest(
            "look",
            mode,
            CommandTriggerOrigin.Automation,
            options,
            null);

        return new CameraCommandContext(
            request,
            mode,
            null!,
            settings,
            captureFrame ?? (_ => Task.FromResult<byte[]?>([0xFF, 0xD8])),
            waitForManualCapture ?? (_ => Task.FromResult<byte[]?>([0xFF, 0xD8])));
    }
}
