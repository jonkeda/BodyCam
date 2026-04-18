namespace BodyCam.Services.Input;

/// <summary>
/// Aggregates all button input providers, wires gesture recognition, and dispatches actions.
/// Multiple providers can be active simultaneously.
/// </summary>
public sealed class ButtonInputManager : IDisposable
{
    private readonly IReadOnlyList<IButtonInputProvider> _providers;
    private readonly GestureRecognizer _gestureRecognizer;
    private readonly ActionMap _actionMap;

    public event EventHandler<ButtonActionEvent>? ActionTriggered;

    public ButtonInputManager(IEnumerable<IButtonInputProvider> providers)
    {
        _providers = providers.ToList();
        _gestureRecognizer = new GestureRecognizer();
        _actionMap = new ActionMap();

        _gestureRecognizer.GestureRecognized += OnGestureRecognized;
    }

    public IReadOnlyList<IButtonInputProvider> Providers => _providers;
    public ActionMap ActionMap => _actionMap;
    public GestureRecognizer GestureRecognizer => _gestureRecognizer;

    public async Task StartAsync(CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            if (!provider.IsAvailable) continue;
            provider.RawButtonEvent += OnRawButtonEvent;
            provider.PreRecognizedGesture += OnPreRecognizedGesture;
            provider.Disconnected += OnProviderDisconnected;
            await provider.StartAsync(ct);
        }
    }

    public async Task StopAsync()
    {
        foreach (var provider in _providers)
        {
            provider.RawButtonEvent -= OnRawButtonEvent;
            provider.PreRecognizedGesture -= OnPreRecognizedGesture;
            provider.Disconnected -= OnProviderDisconnected;
            await provider.StopAsync();
        }
    }

    private void OnRawButtonEvent(object? sender, RawButtonEvent evt)
        => _gestureRecognizer.ProcessEvent(evt);

    private void OnPreRecognizedGesture(object? sender, ButtonGestureEvent gesture)
        => DispatchAction(gesture);

    private void OnGestureRecognized(object? sender, ButtonGestureEvent gesture)
        => DispatchAction(gesture);

    private void DispatchAction(ButtonGestureEvent gesture)
    {
        var action = _actionMap.GetAction(
            $"{gesture.ProviderId}:{gesture.ButtonId}", gesture.Gesture);
        if (action == ButtonAction.None) return;

        ActionTriggered?.Invoke(this, new ButtonActionEvent
        {
            Action = action,
            SourceProviderId = gesture.ProviderId,
            SourceGesture = gesture.Gesture,
            TimestampMs = gesture.TimestampMs,
        });
    }

    private void OnProviderDisconnected(object? sender, EventArgs e)
    {
        // Provider disconnected — no action needed, just stop receiving events
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.RawButtonEvent -= OnRawButtonEvent;
            provider.PreRecognizedGesture -= OnPreRecognizedGesture;
            provider.Disconnected -= OnProviderDisconnected;
            provider.Dispose();
        }
        _gestureRecognizer.Dispose();
    }
}
