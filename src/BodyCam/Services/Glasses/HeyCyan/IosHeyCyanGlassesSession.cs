#if IOS
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// iOS implementation of IHeyCyanGlassesSession, thin wrapper around the platform-specific
/// IosHeyCyanGlassesSession. iOS does not need explicit permission checks (CoreBluetooth
/// handles permissions automatically), so this is a direct pass-through.
/// </summary>
internal sealed class IosHeyCyanGlassesSession : IHeyCyanGlassesSession
{
    private readonly Platforms.iOS.HeyCyan.IosHeyCyanGlassesSession _impl;

    public HeyCyanState State => _impl.State;
    public HeyCyanDeviceInfo? Device => _impl.Device;
    public HeyCyanMediaCount? LastMediaCount => _impl.LastMediaCount;

    public event EventHandler<HeyCyanState>? StateChanged
    {
        add => _impl.StateChanged += value;
        remove => _impl.StateChanged -= value;
    }

    public event EventHandler<HeyCyanBattery>? BatteryUpdated
    {
        add => _impl.BatteryUpdated += value;
        remove => _impl.BatteryUpdated -= value;
    }

    public event EventHandler<HeyCyanButtonEvent>? ButtonPressed
    {
        add => _impl.ButtonPressed += value;
        remove => _impl.ButtonPressed -= value;
    }

    public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated
    {
        add => _impl.MediaCountUpdated += value;
        remove => _impl.MediaCountUpdated -= value;
    }

    public event EventHandler<byte[]>? AiPhotoReceived
    {
        add => _impl.AiPhotoReceived += value;
        remove => _impl.AiPhotoReceived -= value;
    }

    public IosHeyCyanGlassesSession(
        Platforms.iOS.HeyCyan.IosHeyCyanGlassesSession impl)
    {
        _impl = impl;
    }

    public Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct)
        => _impl.ScanAsync(timeout, ct);

    public Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct)
        => _impl.ConnectAsync(device, ct);

    public Task DisconnectAsync(CancellationToken ct)
        => _impl.DisconnectAsync(ct);

    public Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken ct)
        => _impl.GetVersionAsync(ct);

    public Task<HeyCyanBattery> GetBatteryAsync(CancellationToken ct)
        => _impl.GetBatteryAsync(ct);

    public Task SyncTimeAsync(CancellationToken ct)
        => _impl.SyncTimeAsync(ct);

    public Task TakePhotoAsync(CancellationToken ct)
        => _impl.TakePhotoAsync(ct);

    public Task StartVideoAsync(CancellationToken ct)
        => _impl.StartVideoAsync(ct);

    public Task StopVideoAsync(CancellationToken ct)
        => _impl.StopVideoAsync(ct);

    public Task StartAudioAsync(CancellationToken ct)
        => _impl.StartAudioAsync(ct);

    public Task StopAudioAsync(CancellationToken ct)
        => _impl.StopAudioAsync(ct);

    public Task TakeAiPhotoAsync(CancellationToken ct)
        => _impl.TakeAiPhotoAsync(ct);

    public Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)
        => _impl.EnterTransferModeAsync(ct);

    public Task ExitTransferModeAsync(CancellationToken ct)
        => _impl.ExitTransferModeAsync(ct);

    public ValueTask DisposeAsync()
        => _impl.DisposeAsync();
}
#endif
