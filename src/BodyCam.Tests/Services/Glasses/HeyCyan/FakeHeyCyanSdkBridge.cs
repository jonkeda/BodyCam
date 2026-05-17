using BodyCam.Services.Glasses.HeyCyan;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

/// <summary>
/// Fake implementation of IHeyCyanSdkBridge for testing HeyCyanGlassesSessionCore
/// without Android dependencies. Allows tests to script scan results, command responses,
/// and synthetic events (button presses, disconnects).
/// </summary>
internal sealed class FakeHeyCyanSdkBridge : IHeyCyanSdkBridge
{
    public List<HeyCyanScanResult> ScriptedScan { get; } = new();
    public Func<byte[], HeyCyanResponse>? OnSend { get; set; }
    public TaskCompletionSource ConnectGate { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool ScanStarted { get; private set; }
    public bool ScanStopped { get; private set; }
    public string? ConnectedMac { get; private set; }
    public bool Disposed { get; private set; }

    public event EventHandler<HeyCyanScanResult>? DeviceDiscovered;
    public event EventHandler<HeyCyanConnectionState>? ConnectionStateChanged;
    public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
    public event EventHandler<HeyCyanRawNotify>? RawNotify;

    public Task StartScanAsync(TimeSpan _, CancellationToken __)
    {
        ScanStarted = true;
        foreach (var r in ScriptedScan)
            DeviceDiscovered?.Invoke(this, r);
        return Task.CompletedTask;
    }

    public Task StopScanAsync()
    {
        ScanStopped = true;
        return Task.CompletedTask;
    }

    public async Task ConnectAsync(string mac, CancellationToken ct)
    {
        ConnectionStateChanged?.Invoke(this, HeyCyanConnectionState.Connecting);
        await ConnectGate.Task.WaitAsync(ct);
        ConnectedMac = mac;
        ConnectionStateChanged?.Invoke(this, HeyCyanConnectionState.Connected);
    }

    public Task DisconnectAsync()
    {
        ConnectionStateChanged?.Invoke(this, HeyCyanConnectionState.Disconnected);
        ConnectedMac = null;
        return Task.CompletedTask;
    }

    public Task<HeyCyanResponse> SendAsync(byte[] payload, CancellationToken ct)
    {
        if (OnSend is null)
            throw new InvalidOperationException("OnSend not scripted");
        return Task.FromResult(OnSend(payload));
    }

    public void RaiseButton(HeyCyanButtonGesture g) =>
        ButtonPressed?.Invoke(this, new(g, DateTimeOffset.UtcNow));

    public void RaiseDisconnect() =>
        ConnectionStateChanged?.Invoke(this, HeyCyanConnectionState.Disconnected);

    public void RaiseRawNotify(byte[] frame) =>
        RawNotify?.Invoke(this, new(frame));

    public void Dispose()
    {
        Disposed = true;
    }
}
