namespace BodyCam.Services.Glasses.HeyCyan;

using BodyCam.Services.Input;
using Microsoft.Extensions.Logging;

/// <summary>
/// Button input provider for HeyCyan glasses — thin adapter over IHeyCyanGlassesSession.ButtonPressed.
/// The QCSDK firmware performs on-device gesture recognition (tap / double-tap / long-press)
/// and publishes the result via BLE notify frames. This provider raises PreRecognizedGesture only,
/// bypassing the central GestureRecognizer.
/// </summary>
public sealed class HeyCyanButtonProvider : IButtonInputProvider
{
    public const string ProviderIdConst = "heycyan-glasses";
    internal const string ButtonIdConst = "glasses-button";

    private readonly IHeyCyanGlassesSession _session;
    private readonly ILogger<HeyCyanButtonProvider> _log;
    private bool _started;

    public HeyCyanButtonProvider(
        IHeyCyanGlassesSession session,
        ILogger<HeyCyanButtonProvider> log)
    {
        _session = session;
        _log = log;
    }

    public string ProviderId => ProviderIdConst;
    public string DisplayName => "HeyCyan Glasses Button";

    public bool IsAvailable =>
        _session.State == HeyCyanState.Connected ||
        _session.State == HeyCyanState.TransferMode;

    public bool IsActive => _started;

    public event EventHandler<RawButtonEvent>? RawButtonEvent;          // never raised
    public event EventHandler<ButtonGestureEvent>? PreRecognizedGesture;
    public event EventHandler? Disconnected;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_started) return Task.CompletedTask;
        _session.ButtonPressed += OnButtonPressed;
        _started = true;
        _log.LogInformation("HeyCyanButtonProvider started");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!_started) return Task.CompletedTask;
        _session.ButtonPressed -= OnButtonPressed;
        _started = false;
        return Task.CompletedTask;
    }

    private void OnButtonPressed(object? sender, HeyCyanButtonEvent evt)
    {
        var gesture = MapGesture(evt.Gesture);
        PreRecognizedGesture?.Invoke(this, new ButtonGestureEvent
        {
            ProviderId = ProviderIdConst,
            ButtonId = ButtonIdConst,
            Gesture = gesture,
            TimestampMs = evt.Timestamp.ToUnixTimeMilliseconds(),
        });
    }

    internal static ButtonGesture MapGesture(HeyCyanButtonGesture g) => g switch
    {
        HeyCyanButtonGesture.Tap => ButtonGesture.SingleTap,
        HeyCyanButtonGesture.DoubleTap => ButtonGesture.DoubleTap,
        HeyCyanButtonGesture.LongPress => ButtonGesture.LongPress,
        _ => ButtonGesture.SingleTap,
    };

    public void Dispose() => _ = StopAsync();
}
