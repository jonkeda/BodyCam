namespace BodyCam.Services.Glasses.HeyCyan;

internal enum HeyCyanConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting
}

internal sealed record HeyCyanScanResult(string Name, string MacAddress, int Rssi);

internal sealed record HeyCyanRawNotify(byte[] LoadData);

internal sealed record HeyCyanResponse(int CmdType, byte[] Payload);

public sealed record HeyCyanButtonEvent(HeyCyanButtonGesture Gesture, DateTimeOffset Timestamp);

public enum HeyCyanButtonGesture
{
    /// <summary>
    /// Single tap (AI-photo button: loadData[6]==0x02).
    /// </summary>
    Tap,

    /// <summary>
    /// Double tap (AI-voice button: loadData[6]==0x03).
    /// </summary>
    DoubleTap,

    /// <summary>
    /// Long press (not yet observed in CyanBridge; reserved).
    /// </summary>
    LongPress
}
