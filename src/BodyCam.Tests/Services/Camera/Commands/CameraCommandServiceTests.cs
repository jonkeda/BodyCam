using BodyCam.Services;
using BodyCam.Services.Camera;
using BodyCam.Services.Camera.Commands;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BodyCam.Tests.Services.Camera.Commands;

public class CameraCommandServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ActionsDrawer_UsesSettingsDefaultMode()
    {
        var settings = CreateSettings();
        settings.DefaultTouchCommandMode.Returns(CameraCommandMode.ManualAim);
        var command = new ProbeCommand();
        var service = CreateService(command, settings);

        var result = await service.ExecuteAsync(new CameraCommandRequest(
            "probe", null, CommandTriggerOrigin.ActionsDrawer, null, null));

        result.Success.Should().BeTrue();
        command.LastMode.Should().Be(CameraCommandMode.ManualAim);
    }

    [Theory]
    [InlineData(CommandTriggerOrigin.LlmToolCall)]
    [InlineData(CommandTriggerOrigin.PhysicalButton)]
    [InlineData(CommandTriggerOrigin.WakeWord)]
    [InlineData(CommandTriggerOrigin.KeyboardShortcut)]
    public async Task ExecuteAsync_NonTouchOrigins_DefaultToFullAuto(CommandTriggerOrigin origin)
    {
        var command = new ProbeCommand();
        var service = CreateService(command, CreateSettings());

        var result = await service.ExecuteAsync(new CameraCommandRequest(
            "probe", null, origin, null, null));

        result.Success.Should().BeTrue();
        command.LastMode.Should().Be(CameraCommandMode.FullAuto);
    }

    [Fact]
    public async Task ExecuteAsync_AutomationWithoutMode_Fails()
    {
        var service = CreateService(new ProbeCommand(), CreateSettings());

        var result = await service.ExecuteAsync(new CameraCommandRequest(
            "probe", null, CommandTriggerOrigin.Automation, null, null));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Automation");
    }

    [Fact]
    public void Registry_ResolvesByIdAndToolName()
    {
        var command = new ProbeCommand();
        var registry = new CameraCommandRegistry([command]);

        registry.TryGet("probe", out var byId).Should().BeTrue();
        registry.TryGetTool("probe_tool", out var byTool).Should().BeTrue();
        byId.Should().BeSameAs(command);
        byTool.Should().BeSameAs(command);
    }

    private static CameraCommandService CreateService(
        ICameraCommand command,
        ISettingsService settings)
    {
        var cameras = new CameraManager(
            [],
            settings,
            new DefaultCameraSelector(),
            NullLogger<CameraManager>.Instance);

        return new CameraCommandService(
            new CameraCommandRegistry([command]),
            cameras,
            settings,
            new ManualCameraCaptureCoordinator(),
            NullLogger<CameraCommandService>.Instance);
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

    private sealed class ProbeCommand : CameraCommandBase<ProbeOptions>
    {
        public override string Id => "probe";
        public override string DisplayName => "Probe";
        public override string? ToolName => "probe_tool";

        public override CameraCommandCapabilities Capabilities { get; } = new(
            true,
            true,
            true,
            false,
            false);

        public CameraCommandMode? LastMode { get; private set; }

        public override Task<CameraCommandResult> ExecuteAsync(
            CameraCommandContext context,
            CancellationToken ct)
        {
            LastMode = context.ResolvedMode;
            return Task.FromResult(new CameraCommandResult(
                Id,
                true,
                context.ResolvedMode.ToString(),
                new Dictionary<string, object?> { ["mode"] = context.ResolvedMode.ToString() },
                null));
        }
    }

    private sealed class ProbeOptions
    {
    }
}
