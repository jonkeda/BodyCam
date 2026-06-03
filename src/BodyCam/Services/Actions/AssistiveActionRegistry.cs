namespace BodyCam.Services.Actions;

public sealed class AssistiveActionRegistry : IAssistiveActionRegistry
{
    private readonly IReadOnlyDictionary<string, IAssistiveAction> _actions;

    public AssistiveActionRegistry(IEnumerable<IAssistiveAction> actions)
    {
        _actions = actions.ToDictionary(
            action => action.Descriptor.Id,
            StringComparer.OrdinalIgnoreCase);
        Actions = _actions.Values
            .Select(action => action.Descriptor)
            .OrderBy(action => action.DisplayName)
            .ToArray();
    }

    public IReadOnlyList<AssistiveActionDescriptor> Actions { get; }

    public bool TryGet(string id, out IAssistiveAction action) =>
        _actions.TryGetValue(id, out action!);

    public IAssistiveAction GetRequired(string id) =>
        TryGet(id, out var action)
            ? action
            : throw new InvalidOperationException($"Unknown assistive action '{id}'.");
}
