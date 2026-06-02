namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Cross-platform abstraction over the HeyCyan control transport.
/// Android uses direct C# BLE over the serial-port GATT service.
/// iOS implementation wraps QCCentralManager / QCSDKManager.
/// </summary>
internal interface IHeyCyanSdkBridge : IDisposable
{
    event EventHandler<HeyCyanScanResult>? DeviceDiscovered;
    event EventHandler<HeyCyanConnectionState>? ConnectionStateChanged;
    event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
    event EventHandler<HeyCyanRawNotify>? RawNotify;

    Task StartScanAsync(TimeSpan timeout, CancellationToken ct);
    Task StopScanAsync();
    Task ConnectAsync(string macAddress, CancellationToken ct);
    Task DisconnectAsync();

    /// <summary>
    /// Send a raw control frame and await the matching response.
    /// The cmdType (first byte of payload) is used for request/response correlation.
    /// </summary>
    Task<HeyCyanResponse> SendAsync(byte[] payload, CancellationToken ct);
}
