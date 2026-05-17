using BodyCam.Services.Glasses.HeyCyan;

namespace BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;

/// <summary>
/// Extended fake HeyCyan session for testing HeyCyanGlassesDeviceManager (Phase 7).
/// Adds support for version/battery queries and scan operations.
/// </summary>
public sealed class FakeHeyCyanSessionWithVersion : IHeyCyanGlassesSession
{
    public HeyCyanState State { get; private set; } = HeyCyanState.Disconnected;
    public HeyCyanDeviceInfo? Device { get; private set; }
    public HeyCyanMediaCount? LastMediaCount { get; private set; }

    public event EventHandler<HeyCyanState>? StateChanged;
    public event EventHandler<HeyCyanBattery>? BatteryUpdated;
    public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
    public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
    public event EventHandler<byte[]>? AiPhotoReceived;

    private HeyCyanVersionInfo? _version;
    private HeyCyanBattery? _battery;

    public void RaiseStateChanged(HeyCyanState newState)
    {
        State = newState;
        StateChanged?.Invoke(this, State);
    }

    public void RaiseBatteryUpdated(HeyCyanBattery battery)
    {
        _battery = battery;
        BatteryUpdated?.Invoke(this, battery);
    }

    public void RaiseMediaCountUpdated(HeyCyanMediaCount count)
    {
        LastMediaCount = count;
        MediaCountUpdated?.Invoke(this, count);
    }

    public void RaiseButtonPressed(HeyCyanButtonGesture gesture)
    {
        ButtonPressed?.Invoke(this, new HeyCyanButtonEvent(gesture, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// When set, ScanAsync awaits this TCS instead of returning immediately.
    /// Use <c>ct.Register(() => ScanCompletionSource.TrySetCanceled(ct))</c>
    /// in tests to simulate cancellation-aware scans.
    /// </summary>
    public TaskCompletionSource<IReadOnlyList<HeyCyanDeviceInfo>>? ScanCompletionSource { get; set; }

    public async Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (ScanCompletionSource is not null)
        {
            using var reg = ct.Register(() => ScanCompletionSource.TrySetCanceled(ct));
            return await ScanCompletionSource.Task;
        }

        // Return fake device list for testing
        var devices = new List<HeyCyanDeviceInfo>
        {
            new("TestGlasses1", "AA:BB:CC:DD:EE:01", -45),
            new("TestGlasses2", "AA:BB:CC:DD:EE:02", -60)
        };
        return devices;
    }

    public Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct)
    {
        Device = device;
        State = HeyCyanState.Connected;
        StateChanged?.Invoke(this, State);

        // Populate version info
        _version = new HeyCyanVersionInfo(
            Hardware: "HW1.0",
            Firmware: "FW2.3",
            WifiHardware: "WIFI1.0",
            WifiFirmware: "WIFI1.5",
            MacAddress: device.Address);

        // Populate battery info
        _battery = new HeyCyanBattery(Percentage: 85, IsCharging: false);

        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct)
    {
        State = HeyCyanState.Disconnected;
        Device = null;
        StateChanged?.Invoke(this, State);
        return Task.CompletedTask;
    }

    public Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken ct)
    {
        return Task.FromResult(_version ?? new HeyCyanVersionInfo(
            "HW1.0", "FW1.0", "WIFI1.0", "WIFI1.0", "00:00:00:00:00:00"));
    }

    public Task<HeyCyanBattery> GetBatteryAsync(CancellationToken ct)
    {
        return Task.FromResult(_battery ?? new HeyCyanBattery(100, false));
    }

    public Task SyncTimeAsync(CancellationToken ct)
    {
        // No-op for testing
        return Task.CompletedTask;
    }

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
