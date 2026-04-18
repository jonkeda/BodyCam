namespace BodyCam.Services.Input;

public sealed class GestureRecognizer : IDisposable
{
    public int DoubleTapWindowMs { get; set; } = 300;
    public int LongPressThresholdMs { get; set; } = 500;

    public event EventHandler<ButtonGestureEvent>? GestureRecognized;

    private readonly Dictionary<string, ButtonState> _states = new();
    private readonly object _lock = new();

    public void ProcessEvent(RawButtonEvent evt)
    {
        var key = $"{evt.ProviderId}:{evt.ButtonId}";

        if (evt.EventType == RawButtonEventType.Click)
        {
            // Click = synthetic down + up
            ProcessEvent(new RawButtonEvent
            {
                ProviderId = evt.ProviderId,
                ButtonId = evt.ButtonId,
                EventType = RawButtonEventType.ButtonDown,
                TimestampMs = evt.TimestampMs,
            });
            ProcessEvent(new RawButtonEvent
            {
                ProviderId = evt.ProviderId,
                ButtonId = evt.ButtonId,
                EventType = RawButtonEventType.ButtonUp,
                TimestampMs = evt.TimestampMs,
            });
            return;
        }

        lock (_lock)
        {
            if (!_states.TryGetValue(key, out var state))
            {
                state = new ButtonState(evt.ProviderId, evt.ButtonId, this);
                _states[key] = state;
            }

            if (evt.EventType == RawButtonEventType.ButtonDown)
                state.OnButtonDown(evt.TimestampMs);
            else if (evt.EventType == RawButtonEventType.ButtonUp)
                state.OnButtonUp(evt.TimestampMs);
        }
    }

    internal void RaiseGesture(ButtonGestureEvent gesture)
    {
        GestureRecognized?.Invoke(this, gesture);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var state in _states.Values)
                state.Dispose();
            _states.Clear();
        }
    }

    private sealed class ButtonState : IDisposable
    {
        private readonly string _providerId;
        private readonly string _buttonId;
        private readonly GestureRecognizer _owner;

        private int _tapCount;
        private bool _isDown;
        private bool _longPressEmitted;
        private CancellationTokenSource? _longPressCts;
        private CancellationTokenSource? _doubleTapCts;

        public ButtonState(string providerId, string buttonId, GestureRecognizer owner)
        {
            _providerId = providerId;
            _buttonId = buttonId;
            _owner = owner;
        }

        public void OnButtonDown(long timestampMs)
        {
            _isDown = true;
            _longPressEmitted = false;

            // Cancel pending double-tap timer (user is pressing again)
            _doubleTapCts?.Cancel();
            _doubleTapCts = null;

            // Start long-press timer
            _longPressCts?.Cancel();
            _longPressCts = new CancellationTokenSource();
            var cts = _longPressCts;
            var threshold = _owner.LongPressThresholdMs;

            _ = Task.Delay(threshold, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                lock (_owner._lock)
                {
                    if (!_isDown) return;
                    _longPressEmitted = true;
                    _tapCount = 0;
                }
                _owner.RaiseGesture(new ButtonGestureEvent
                {
                    ProviderId = _providerId,
                    ButtonId = _buttonId,
                    Gesture = ButtonGesture.LongPress,
                    TimestampMs = timestampMs + threshold,
                });
            }, TaskScheduler.Default);
        }

        public void OnButtonUp(long timestampMs)
        {
            _isDown = false;

            // Cancel long-press timer
            _longPressCts?.Cancel();
            _longPressCts = null;

            if (_longPressEmitted)
            {
                _longPressEmitted = false;
                _owner.RaiseGesture(new ButtonGestureEvent
                {
                    ProviderId = _providerId,
                    ButtonId = _buttonId,
                    Gesture = ButtonGesture.LongPressRelease,
                    TimestampMs = timestampMs,
                });
                return;
            }

            _tapCount++;

            if (_tapCount >= 2)
            {
                // Double tap — emit immediately
                _tapCount = 0;
                _doubleTapCts?.Cancel();
                _doubleTapCts = null;
                _owner.RaiseGesture(new ButtonGestureEvent
                {
                    ProviderId = _providerId,
                    ButtonId = _buttonId,
                    Gesture = ButtonGesture.DoubleTap,
                    TimestampMs = timestampMs,
                });
                return;
            }

            // Start double-tap window timer
            _doubleTapCts?.Cancel();
            _doubleTapCts = new CancellationTokenSource();
            var cts = _doubleTapCts;
            var window = _owner.DoubleTapWindowMs;

            _ = Task.Delay(window, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                lock (_owner._lock)
                {
                    _tapCount = 0;
                }
                _owner.RaiseGesture(new ButtonGestureEvent
                {
                    ProviderId = _providerId,
                    ButtonId = _buttonId,
                    Gesture = ButtonGesture.SingleTap,
                    TimestampMs = timestampMs + window,
                });
            }, TaskScheduler.Default);
        }

        public void Dispose()
        {
            _longPressCts?.Cancel();
            _doubleTapCts?.Cancel();
        }
    }
}
