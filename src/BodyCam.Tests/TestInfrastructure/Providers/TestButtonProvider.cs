using BodyCam.Services.Input;

namespace BodyCam.Tests.TestInfrastructure.Providers;

public class TestButtonProvider : IButtonInputProvider
{
    public string DisplayName => "Test Buttons";
    public string ProviderId => "test-buttons";
    public bool IsAvailable => true;
    public bool IsActive { get; private set; }

    public event EventHandler<RawButtonEvent>? RawButtonEvent;
    public event EventHandler<ButtonGestureEvent>? PreRecognizedGesture;
    public event EventHandler? Disconnected;

    public int ClickCount { get; private set; }
    public int GestureCount { get; private set; }
    public ButtonGesture? LastGesture { get; private set; }

    public Task StartAsync(CancellationToken ct = default)
    {
        IsActive = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsActive = false;
        return Task.CompletedTask;
    }

    public void SimulateClick(string buttonId = "main")
    {
        var ts = Environment.TickCount64;
        RawButtonEvent?.Invoke(this, new RawButtonEvent
        {
            ProviderId = ProviderId,
            ButtonId = buttonId,
            EventType = RawButtonEventType.ButtonDown,
            TimestampMs = ts,
        });
        RawButtonEvent?.Invoke(this, new RawButtonEvent
        {
            ProviderId = ProviderId,
            ButtonId = buttonId,
            EventType = RawButtonEventType.ButtonUp,
            TimestampMs = ts + 50,
        });
        ClickCount++;
    }

    public void SimulateGesture(ButtonGesture gesture, string buttonId = "main")
    {
        PreRecognizedGesture?.Invoke(this, new ButtonGestureEvent
        {
            ProviderId = ProviderId,
            ButtonId = buttonId,
            Gesture = gesture,
            TimestampMs = Environment.TickCount64,
        });
        GestureCount++;
        LastGesture = gesture;
    }

    public void SimulateDisconnect()
    {
        IsActive = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        ClickCount = 0;
        GestureCount = 0;
        LastGesture = null;
    }

    public void Dispose() { }
}
