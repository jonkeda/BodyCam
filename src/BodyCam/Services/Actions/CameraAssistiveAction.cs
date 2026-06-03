using BodyCam.Services.Camera.Commands;

namespace BodyCam.Services.Actions;

public sealed class CameraAssistiveAction : IAssistiveAction
{
    private readonly string _commandId;
    private readonly ICameraCommandService _cameraCommands;
    private readonly object? _defaultOptions;
    private readonly string? _defaultQuery;

    public CameraAssistiveAction(
        string id,
        string displayName,
        string commandId,
        ICameraCommandService cameraCommands,
        object? defaultOptions = null,
        string? defaultQuery = null)
    {
        Descriptor = new AssistiveActionDescriptor(
            id,
            displayName,
            RequiresCamera: true,
            StartsOrStopsSession: false);
        _commandId = commandId;
        _cameraCommands = cameraCommands;
        _defaultOptions = defaultOptions;
        _defaultQuery = defaultQuery;
    }

    public AssistiveActionDescriptor Descriptor { get; }

    public async Task<AssistiveActionResult> ExecuteAsync(
        AssistiveActionRequest request,
        AssistiveActionContext context,
        CancellationToken ct = default)
    {
        var result = await _cameraCommands.ExecuteAsync(
            new CameraCommandRequest(
                _commandId,
                request.CameraMode,
                ToCameraOrigin(request.Origin),
                request.Options ?? _defaultOptions,
                request.Query ?? _defaultQuery),
            ct)
            .ConfigureAwait(false);

        return new AssistiveActionResult(
            Descriptor.Id,
            result.Success,
            AssistiveActionResultKind.CameraCommand,
            result.TranscriptText,
            result.Data,
            result.Error,
            result);
    }

    private static CommandTriggerOrigin ToCameraOrigin(ActionTriggerOrigin origin) =>
        origin switch
        {
            ActionTriggerOrigin.ActionsDrawer => CommandTriggerOrigin.ActionsDrawer,
            ActionTriggerOrigin.PhysicalButton => CommandTriggerOrigin.PhysicalButton,
            ActionTriggerOrigin.WakeWord => CommandTriggerOrigin.WakeWord,
            ActionTriggerOrigin.KeyboardShortcut => CommandTriggerOrigin.KeyboardShortcut,
            ActionTriggerOrigin.LlmToolCall => CommandTriggerOrigin.LlmToolCall,
            ActionTriggerOrigin.Automation => CommandTriggerOrigin.Automation,
            ActionTriggerOrigin.ExplicitManual => CommandTriggerOrigin.ExplicitManual,
            _ => CommandTriggerOrigin.ActionsDrawer,
        };
}
