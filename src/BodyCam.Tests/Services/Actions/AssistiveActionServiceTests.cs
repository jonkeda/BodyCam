using BodyCam.Services.Actions;
using BodyCam.Services.Camera.Commands;
using BodyCam.Services.Input;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Actions;

public sealed class AssistiveActionServiceTests
{
    [Fact]
    public async Task ExecuteButtonActionAsync_Look_RoutesToPhysicalButtonCameraCommand()
    {
        var cameras = new RecordingCameraCommandService();
        var service = CreateService(
            new CameraAssistiveAction(
                AssistiveActionIds.Look,
                "Look",
                "look",
                cameras));
        var button = CreateButton(ButtonAction.Look);

        var result = await service.ExecuteButtonActionAsync(button, new AssistiveActionContext());

        result.Success.Should().BeTrue();
        result.Kind.Should().Be(AssistiveActionResultKind.CameraCommand);
        result.CameraCommandResult.Should().NotBeNull();
        cameras.LastRequest.Should().NotBeNull();
        cameras.LastRequest!.CommandId.Should().Be("look");
        cameras.LastRequest.Origin.Should().Be(CommandTriggerOrigin.PhysicalButton);
        cameras.LastRequest.Mode.Should().Be(CameraCommandMode.FullAuto);
    }

    [Fact]
    public async Task ExecuteButtonActionAsync_Photo_ReturnsPhotoMarker()
    {
        var service = CreateService(new PhotoAssistiveAction());

        var result = await service.ExecuteButtonActionAsync(
            CreateButton(ButtonAction.Photo),
            new AssistiveActionContext());

        result.Success.Should().BeTrue();
        result.Kind.Should().Be(AssistiveActionResultKind.Photo);
        result.ActionId.Should().Be(AssistiveActionIds.Photo);
    }

    [Fact]
    public async Task ExecuteButtonActionAsync_UnsupportedButtonAction_ReturnsFailure()
    {
        var service = CreateService();

        var result = await service.ExecuteButtonActionAsync(
            CreateButton(ButtonAction.VolumeUp),
            new AssistiveActionContext());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain(nameof(ButtonAction.VolumeUp));
    }

    private static AssistiveActionService CreateService(params IAssistiveAction[] actions) =>
        new(
            new AssistiveActionRegistry(actions),
            NullLogger<AssistiveActionService>.Instance);

    private static ButtonActionEvent CreateButton(ButtonAction action) =>
        new()
        {
            Action = action,
            SourceProviderId = "test-buttons",
            SourceGesture = ButtonGesture.SingleTap,
            TimestampMs = 123,
        };

    private sealed class RecordingCameraCommandService : ICameraCommandService
    {
        public CameraCommandRequest? LastRequest { get; private set; }

        public Task<CameraCommandResult> ExecuteAsync(
            CameraCommandRequest request,
            CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new CameraCommandResult(
                request.CommandId,
                Success: true,
                TranscriptText: "done",
                Data: null,
                Error: null));
        }
    }
}
