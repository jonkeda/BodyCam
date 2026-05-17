namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// No-op implementation of IHeyCyanGlassesSession for platforms that don't support HeyCyan
/// glasses (Windows, etc.). All operations throw NotSupportedException or return empty results.
/// </summary>
internal sealed class NullHeyCyanGlassesSession : IHeyCyanGlassesSession
{
    public HeyCyanState State => HeyCyanState.Disconnected;
    public HeyCyanDeviceInfo? Device => null;
    public HeyCyanMediaCount? LastMediaCount => null;

#pragma warning disable CS0067
    public event EventHandler<HeyCyanState>? StateChanged;
    public event EventHandler<HeyCyanBattery>? BatteryUpdated;
    public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
    public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
    public event EventHandler<byte[]>? AiPhotoReceived;
#pragma warning restore CS0067

    public Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<HeyCyanDeviceInfo>>(Array.Empty<HeyCyanDeviceInfo>());

    public Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct)
        => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken ct)
        => Task.CompletedTask;

    public Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken ct)
        => Task.FromResult(new HeyCyanVersionInfo("0.0.0", "0.0.0", "0.0.0", "0.0.0", string.Empty));

    public Task<HeyCyanBattery> GetBatteryAsync(CancellationToken ct)
        => Task.FromResult(new HeyCyanBattery(0, false));

    public Task SyncTimeAsync(CancellationToken ct) => Task.CompletedTask;
    public Task TakePhotoAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartVideoAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopVideoAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartAudioAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAudioAsync(CancellationToken ct) => Task.CompletedTask;
    public Task TakeAiPhotoAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)
        => throw new NotSupportedException("HeyCyan glasses are not supported on this platform.");

    public Task ExitTransferModeAsync(CancellationToken ct) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
