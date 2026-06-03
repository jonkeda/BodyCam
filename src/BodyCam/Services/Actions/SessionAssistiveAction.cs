using BodyCam.Services.Session;

namespace BodyCam.Services.Actions;

public sealed class SessionAssistiveAction : IAssistiveAction
{
    private readonly ISessionCoordinator _sessions;
    private readonly SessionLayer? _targetLayer;

    public SessionAssistiveAction(
        string id,
        string displayName,
        ISessionCoordinator sessions,
        SessionLayer? targetLayer)
    {
        Descriptor = new AssistiveActionDescriptor(
            id,
            displayName,
            RequiresCamera: targetLayer == SessionLayer.ActiveSession || targetLayer is null,
            StartsOrStopsSession: true);
        _sessions = sessions;
        _targetLayer = targetLayer;
    }

    public AssistiveActionDescriptor Descriptor { get; }

    public async Task<AssistiveActionResult> ExecuteAsync(
        AssistiveActionRequest request,
        AssistiveActionContext context,
        CancellationToken ct = default)
    {
        var options = new SessionTransitionOptions(
            context.PromptForApiKeyAsync,
            context.FrameCaptureFunc);

        var transition = _targetLayer.HasValue
            ? await _sessions.SetLayerAsync(_targetLayer.Value, options, ct).ConfigureAwait(false)
            : await _sessions.ToggleAsync(options, ct).ConfigureAwait(false);

        return new AssistiveActionResult(
            Descriptor.Id,
            transition.Success,
            AssistiveActionResultKind.Session,
            transition.StatusText,
            transition,
            transition.Error);
    }
}
