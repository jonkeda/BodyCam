using BodyCam.Services.Camera.Commands;
using BodyCam.Services.Input;

namespace BodyCam.Services.Actions;

public enum ActionTriggerOrigin
{
    ActionsDrawer,
    PhysicalButton,
    WakeWord,
    KeyboardShortcut,
    LlmToolCall,
    Automation,
    ExplicitManual,
}

public enum AssistiveActionResultKind
{
    None,
    CameraCommand,
    Session,
    Photo,
}

public sealed record AssistiveActionDescriptor(
    string Id,
    string DisplayName,
    bool RequiresCamera,
    bool StartsOrStopsSession);

public sealed record AssistiveActionContext(
    Func<Task<string?>>? PromptForApiKeyAsync = null,
    Func<CancellationToken, Task<byte[]?>>? FrameCaptureFunc = null);

public sealed record AssistiveActionRequest(
    string ActionId,
    ActionTriggerOrigin Origin,
    object? Options = null,
    string? Query = null,
    CameraCommandMode? CameraMode = null,
    ButtonActionEvent? ButtonAction = null);

public sealed record AssistiveActionResult(
    string ActionId,
    bool Success,
    AssistiveActionResultKind Kind,
    string? TranscriptText = null,
    object? Data = null,
    string? Error = null,
    CameraCommandResult? CameraCommandResult = null);

public interface IAssistiveAction
{
    AssistiveActionDescriptor Descriptor { get; }

    Task<AssistiveActionResult> ExecuteAsync(
        AssistiveActionRequest request,
        AssistiveActionContext context,
        CancellationToken ct = default);
}

public interface IAssistiveActionRegistry
{
    IReadOnlyList<AssistiveActionDescriptor> Actions { get; }
    bool TryGet(string id, out IAssistiveAction action);
    IAssistiveAction GetRequired(string id);
}

public interface IAssistiveActionService
{
    Task<AssistiveActionResult> ExecuteAsync(
        AssistiveActionRequest request,
        AssistiveActionContext context,
        CancellationToken ct = default);

    Task<AssistiveActionResult> ExecuteButtonActionAsync(
        ButtonActionEvent buttonAction,
        AssistiveActionContext context,
        CancellationToken ct = default);
}
