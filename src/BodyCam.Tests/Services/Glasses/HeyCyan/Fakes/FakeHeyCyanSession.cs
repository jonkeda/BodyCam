using BodyCam.Services.Glasses.HeyCyan;

namespace BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;

/// <summary>
/// Fake HeyCyan session for unit testing Phase 3 audio providers and router.
/// Allows tests to drive StateChanged events programmatically.
/// </summary>
public sealed class FakeHeyCyanSession : IHeyCyanGlassesSession
{
    public HeyCyanState State { get; private set; } = HeyCyanState.Disconnected;
    public HeyCyanDeviceInfo? Device { get; private set; }
    public HeyCyanMediaCount? LastMediaCount => null;

    public event EventHandler<HeyCyanState>? StateChanged;
    public event EventHandler<HeyCyanBattery>? BatteryUpdated;
    public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
    public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
    public event EventHandler<byte[]>? AiPhotoReceived;

    /// <summary>
    /// Simulate connecting to glasses with the specified MAC address.
    /// </summary>
    public void RaiseConnected(string mac)
    {
        Device = new HeyCyanDeviceInfo("Glasses", mac, -50);
        State  = HeyCyanState.Connected;
        StateChanged?.Invoke(this, State);
    }

    /// <summary>
    /// Simulate glasses disconnecting.
    /// </summary>
    public void RaiseDisconnected()
    {
        State  = HeyCyanState.Disconnected;
        Device = null;
        StateChanged?.Invoke(this, State);
    }

    /// <summary>
    /// Simulate entering transfer mode (for Wi-Fi media transfer).
    /// </summary>
    public void RaiseTransferMode()
    {
        State = HeyCyanState.TransferMode;
        StateChanged?.Invoke(this, State);
    }

    /// <summary>
    /// Simulate a button press event from the glasses.
    /// </summary>
    public void RaiseButtonPressed(HeyCyanButtonGesture gesture)
    {
        ButtonPressed?.Invoke(this, new HeyCyanButtonEvent(gesture, DateTimeOffset.UtcNow));
    }

    // Remaining members throw NotSupportedException — Phase 3 does not
    // exercise scan/connect/photo/etc.
    public Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan t, CancellationToken ct)
        => throw new NotSupportedException();

    public Task ConnectAsync(HeyCyanDeviceInfo d, CancellationToken ct)
        => throw new NotSupportedException();

    public Task DisconnectAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public Task<HeyCyanBattery> GetBatteryAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public Task SyncTimeAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public Task TakePhotoAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public Task StartVideoAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public Task StopVideoAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public Task StartAudioAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public Task StopAudioAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public Task TakeAiPhotoAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public Task ExitTransferModeAsync(CancellationToken ct)
        => throw new NotSupportedException();

    public ValueTask DisposeAsync() => default;
}
