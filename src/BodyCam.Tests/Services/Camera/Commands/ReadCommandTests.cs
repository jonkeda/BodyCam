using BodyCam;
using BodyCam.Agents;
using BodyCam.Services;
using BodyCam.Services.AiProviders;
using BodyCam.Services.Camera.Commands;
using BodyCam.Tests.Services.AiProviders;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace BodyCam.Tests.Services.Camera.Commands;

public class ReadCommandTests
{
    [Fact]
    public void PromptDefinitions_ExposeTextAndPrompt()
    {
        var command = CreateCommand(out _);

        var full = command.PromptDefinitions.Single(p => p.Key == nameof(ReadDetailLevel.Full));

        full.DisplayName.Should().Be("Full");
        full.Text.Should().Be("Read all visible text.");
        full.Prompt.Should().Contain("Read the visible text as completely");
    }

    [Fact]
    public void CameraActionVariants_ExposeSummaryOverviewAndFull()
    {
        var command = CreateCommand(out _);

        command.CameraActionVariants.Select(variant => variant.DisplayName)
            .Should()
            .Equal("Summary", "Overview", "Full");
        command.CameraActionVariants.Single(variant => variant.DisplayName == "Full")
            .IsDefault
            .Should()
            .BeTrue();
    }

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

    [Fact]
    public async Task ExecuteAsync_TextOnlyProvider_FailsBeforeCapturing()
    {
        var registry = new AiProviderRegistry([
            new FakeProviderAdapter("text-only", AiProviderCapability.Chat)
        ]);
        var analytics = new RecordingAnalyticsService();
        var command = CreateCommand(out _, registry, analytics);
        var settings = CreateSettings();
        settings.ProviderId.Returns("text-only");
        var captured = 0;
        var context = new CameraCommandContext(
            new CameraCommandRequest("read", CameraCommandMode.FullAuto, CommandTriggerOrigin.Automation, null, null),
            CameraCommandMode.FullAuto,
            null!,
            settings,
            _ =>
            {
                captured++;
                return Task.FromResult<byte[]?>([0xFF, 0xD8]);
            },
            _ => Task.FromResult<byte[]?>([0xFF, 0xD8]));

        var result = await command.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.TranscriptText.Should().Contain("does not support image input");
        captured.Should().Be(0);
        analytics.HasEvent(
            "ai.command.capability",
            ("command", "read"),
            ("error.category", "unsupported_capability")).Should().BeTrue();
    }

    private static ReadCommand CreateCommand(
        out IChatClient chatClient,
        IAiProviderRegistry? registry = null,
        IAnalyticsService? analytics = null)
    {
        chatClient = Substitute.For<IChatClient>();
        return new ReadCommand(
            new VisionAgent(chatClient, new AppSettings()),
            registry ?? AiProviderRegistry.Default,
            analytics ?? new NullAnalyticsService());
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
        settings.DefaultLookDetailLevel.Returns(LookDetailLevel.Overview);
        settings.DefaultReadDetailLevel.Returns(ReadDetailLevel.Full);
        settings.ConfirmExternalScanActions.Returns(true);
        return settings;
    }
}
