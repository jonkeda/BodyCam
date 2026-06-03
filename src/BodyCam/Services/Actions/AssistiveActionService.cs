using BodyCam.Services.Camera.Commands;
using BodyCam.Services.Input;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Actions;

public sealed class AssistiveActionService : IAssistiveActionService
{
    private readonly IAssistiveActionRegistry _registry;
    private readonly ILogger<AssistiveActionService> _logger;

    public AssistiveActionService(
        IAssistiveActionRegistry registry,
        ILogger<AssistiveActionService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<AssistiveActionResult> ExecuteAsync(
        AssistiveActionRequest request,
        AssistiveActionContext context,
        CancellationToken ct = default)
    {
        try
        {
            return await _registry
                .GetRequired(request.ActionId)
                .ExecuteAsync(request, context, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Assistive action {ActionId} failed", request.ActionId);
            return new AssistiveActionResult(
                request.ActionId,
                Success: false,
                AssistiveActionResultKind.None,
                Error: ex.Message);
        }
    }

    public Task<AssistiveActionResult> ExecuteButtonActionAsync(
        ButtonActionEvent buttonAction,
        AssistiveActionContext context,
        CancellationToken ct = default)
    {
        var request = CreateButtonRequest(buttonAction);
        if (request is null)
        {
            return Task.FromResult(new AssistiveActionResult(
                buttonAction.Action.ToString(),
                Success: false,
                AssistiveActionResultKind.None,
                Error: $"Unsupported button action '{buttonAction.Action}'."));
        }

        return ExecuteAsync(request, context, ct);
    }

    private static AssistiveActionRequest? CreateButtonRequest(ButtonActionEvent buttonAction) =>
        buttonAction.Action switch
        {
            ButtonAction.Look => new(
                AssistiveActionIds.Look,
                ActionTriggerOrigin.PhysicalButton,
                CameraMode: CameraCommandMode.FullAuto,
                ButtonAction: buttonAction),
            ButtonAction.Read => new(
                AssistiveActionIds.Read,
                ActionTriggerOrigin.PhysicalButton,
                CameraMode: CameraCommandMode.FullAuto,
                ButtonAction: buttonAction),
            ButtonAction.Find => new(
                AssistiveActionIds.Find,
                ActionTriggerOrigin.PhysicalButton,
                CameraMode: CameraCommandMode.FullAuto,
                ButtonAction: buttonAction),
            ButtonAction.Photo => new(
                AssistiveActionIds.Photo,
                ActionTriggerOrigin.PhysicalButton,
                ButtonAction: buttonAction),
            ButtonAction.ToggleSession or ButtonAction.ToggleConversation or ButtonAction.ToggleSleepActive => new(
                AssistiveActionIds.ToggleSession,
                ActionTriggerOrigin.PhysicalButton,
                ButtonAction: buttonAction),
            ButtonAction.EndSession => new(
                AssistiveActionIds.EndSession,
                ActionTriggerOrigin.PhysicalButton,
                ButtonAction: buttonAction),
            _ => null,
        };
}
