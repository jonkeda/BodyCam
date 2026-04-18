namespace BodyCam.Services.Input;

public sealed class ButtonMapping
{
    public required string ProviderId { get; init; }
    public required ButtonGesture Gesture { get; init; }
    public required ButtonAction Action { get; init; }
}

public sealed class ActionMap
{
    private readonly Dictionary<(string ProviderId, ButtonGesture Gesture), ButtonAction> _map = new();

    public ButtonAction GetAction(string providerId, ButtonGesture gesture)
    {
        if (_map.TryGetValue((providerId, gesture), out var action))
            return action;
        return GetDefaultAction(gesture);
    }

    public void SetAction(string providerId, ButtonGesture gesture, ButtonAction action)
        => _map[(providerId, gesture)] = action;

    public void LoadMappings(IEnumerable<ButtonMapping> mappings)
    {
        _map.Clear();
        foreach (var m in mappings)
            _map[(m.ProviderId, m.Gesture)] = m.Action;
    }

    public IReadOnlyList<ButtonMapping> ExportMappings()
    {
        return _map.Select(kvp => new ButtonMapping
        {
            ProviderId = kvp.Key.ProviderId,
            Gesture = kvp.Key.Gesture,
            Action = kvp.Value,
        }).ToList();
    }

    private static ButtonAction GetDefaultAction(ButtonGesture gesture) => gesture switch
    {
        ButtonGesture.SingleTap => ButtonAction.Look,
        ButtonGesture.DoubleTap => ButtonAction.Photo,
        ButtonGesture.LongPress => ButtonAction.ToggleSession,
        _ => ButtonAction.None,
    };
}
