namespace BodyCam.Platforms.Windows.HeyCyan;

internal interface IWindowsWiFiDirectConnector : IDisposable
{
    string? RemoteIp { get; }
    bool IsConnected { get; }
    string? GroupPassword { get; set; }
    string? MatchedPeerName { get; }
    string? MatchedPeerId { get; }
    IReadOnlyList<WindowsWiFiDirectEndpointPair> ConnectionEndpointPairs { get; }
    IReadOnlyList<string> DiscoveryEvents { get; }

    void StartDiscovery();
    Task<string> WaitForPeerAndConnectAsync(CancellationToken ct);
    Task<string> ConnectAsync(CancellationToken ct);
    void Disconnect();
}

internal sealed record WindowsWiFiDirectEndpointPair(
    string? LocalHost,
    string? LocalService,
    string? RemoteHost,
    string? RemoteService);
