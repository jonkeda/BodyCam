namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Cross-platform abstraction for a HeyCyan glasses connection session.
/// Implemented by AndroidHeyCyanGlassesSession (wrapping IHeyCyanSdkBridge) and
/// IosHeyCyanGlassesSession (wrapping QCCentralManager).
/// </summary>
public interface IHeyCyanGlassesSession : IAsyncDisposable
{
    HeyCyanState State { get; }
    HeyCyanDeviceInfo? Device { get; }
    HeyCyanMediaCount? LastMediaCount { get; }

    event EventHandler<HeyCyanState>? StateChanged;
    event EventHandler<HeyCyanBattery>? BatteryUpdated;
    event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
    event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
    event EventHandler<byte[]>? AiPhotoReceived;

    Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct);
    Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);

    Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken ct);
    Task<HeyCyanBattery> GetBatteryAsync(CancellationToken ct);
    Task SyncTimeAsync(CancellationToken ct);

    Task TakePhotoAsync(CancellationToken ct);
    Task StartVideoAsync(CancellationToken ct);
    Task StopVideoAsync(CancellationToken ct);
    Task StartAudioAsync(CancellationToken ct);
    Task StopAudioAsync(CancellationToken ct);
    Task TakeAiPhotoAsync(CancellationToken ct);

    /// <summary>
    /// Switch to transfer mode and return a working HTTP base URL
    /// (e.g. http://192.168.49.x). Caller is responsible for downloading
    /// /files/media.config and /files/&lt;name&gt; entries.
    /// </summary>
    Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct);

    /// <summary>
    /// Exit transfer mode and tear down the P2P group. Sends BLE exit command
    /// (LargeDataHandler.GlassesControl(new byte[] { 0x02, 0x01, 0x09 }, cb)).
    /// </summary>
    Task ExitTransferModeAsync(CancellationToken ct);
}

public enum HeyCyanState
{
    Disconnected,
    Scanning,
    Connecting,
    Connected,
    TransferMode,
    Disconnecting
}

public sealed record HeyCyanDeviceInfo(string Name, string Address, int Rssi)
{
    public string? Firmware { get; init; }
    public string? Hardware { get; init; }
    public string? MacAddress { get; init; }
}

public sealed record HeyCyanBattery(int Percentage, bool IsCharging);

public sealed record HeyCyanVersionInfo(
    string Hardware,
    string Firmware,
    string WifiHardware,
    string WifiFirmware,
    string MacAddress);

public sealed record HeyCyanMediaCount(int Photos, int Videos, int AudioFiles);

public sealed record HeyCyanTransferSession(string BaseUrl, IReadOnlyList<string> FileNames) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => default; // exits transfer mode
}
