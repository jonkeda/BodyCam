#if ANDROID
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Android implementation of IHeyCyanGlassesSession, wrapping HeyCyanGlassesSessionCore
/// with Android-specific permission checks. Thin shim to keep the core testable.
/// </summary>
internal sealed class AndroidHeyCyanGlassesSession : IHeyCyanGlassesSession
{
    private readonly HeyCyanGlassesSessionCore _core;

    public HeyCyanState State => _core.State;
    public HeyCyanDeviceInfo? Device => _core.Device;
    public HeyCyanMediaCount? LastMediaCount => _core.LastMediaCount;

    public event EventHandler<HeyCyanState>? StateChanged
    {
        add => _core.StateChanged += value;
        remove => _core.StateChanged -= value;
    }

    public event EventHandler<HeyCyanBattery>? BatteryUpdated
    {
        add => _core.BatteryUpdated += value;
        remove => _core.BatteryUpdated -= value;
    }

    public event EventHandler<HeyCyanButtonEvent>? ButtonPressed
    {
        add => _core.ButtonPressed += value;
        remove => _core.ButtonPressed -= value;
    }

    public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated
    {
        add => _core.MediaCountUpdated += value;
        remove => _core.MediaCountUpdated -= value;
    }

    public event EventHandler<byte[]>? AiPhotoReceived
    {
        add => _core.AiPhotoReceived += value;
        remove => _core.AiPhotoReceived -= value;
    }

    public AndroidHeyCyanGlassesSession(
        IHeyCyanSdkBridge bridge,
        ILogger<AndroidHeyCyanGlassesSession> log)
    {
        _core = new HeyCyanGlassesSessionCore(bridge, log);
    }

    public async Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct)
    {
        // Request runtime permissions (BLUETOOTH_SCAN + ACCESS_FINE_LOCATION on Android < 12)
        await BodyCam.Platforms.Android.HeyCyan.HeyCyanPermissions.RequestAsync().ConfigureAwait(false);
        return await _core.ScanAsync(timeout, ct).ConfigureAwait(false);
    }

    public Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct)
        => _core.ConnectAsync(device, ct);

    public Task DisconnectAsync(CancellationToken ct)
        => _core.DisconnectAsync(ct);

    public Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken ct)
        => _core.GetVersionAsync(ct);

    public Task<HeyCyanBattery> GetBatteryAsync(CancellationToken ct)
        => _core.GetBatteryAsync(ct);

    public Task SyncTimeAsync(CancellationToken ct)
        => _core.SyncTimeAsync(ct);

    public Task TakePhotoAsync(CancellationToken ct)
        => _core.TakePhotoAsync(ct);

    public Task StartVideoAsync(CancellationToken ct)
        => _core.StartVideoAsync(ct);

    public Task StopVideoAsync(CancellationToken ct)
        => _core.StopVideoAsync(ct);

    public Task StartAudioAsync(CancellationToken ct)
        => _core.StartAudioAsync(ct);

    public Task StopAudioAsync(CancellationToken ct)
        => _core.StopAudioAsync(ct);

    public Task TakeAiPhotoAsync(CancellationToken ct)
        => _core.TakeAiPhotoAsync(ct);

    public Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)
        => _core.EnterTransferModeAsync(ct);

    public Task ExitTransferModeAsync(CancellationToken ct)
        => _core.ExitTransferModeAsync(ct);

    public ValueTask DisposeAsync()
        => _core.DisposeAsync();
}
#endif
