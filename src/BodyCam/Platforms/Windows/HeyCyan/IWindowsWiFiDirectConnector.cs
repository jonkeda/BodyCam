namespace BodyCam.Platforms.Windows.HeyCyan;

internal interface IWindowsWiFiDirectConnector : IDisposable
{
    string? RemoteIp { get; }
    bool IsConnected { get; }
    string? GroupPassword { get; set; }

    void StartDiscovery();
    Task<string> WaitForPeerAndConnectAsync(CancellationToken ct);
    Task<string> ConnectAsync(CancellationToken ct);
    void Disconnect();
}
