namespace BodyCam.Services.Actions;

public sealed class PhotoAssistiveAction : IAssistiveAction
{
    public AssistiveActionDescriptor Descriptor { get; } = new(
        AssistiveActionIds.Photo,
        "Photo",
        RequiresCamera: true,
        StartsOrStopsSession: false);

    public Task<AssistiveActionResult> ExecuteAsync(
        AssistiveActionRequest request,
        AssistiveActionContext context,
        CancellationToken ct = default)
    {
        return Task.FromResult(new AssistiveActionResult(
            Descriptor.Id,
            Success: true,
            AssistiveActionResultKind.Photo));
    }
}
