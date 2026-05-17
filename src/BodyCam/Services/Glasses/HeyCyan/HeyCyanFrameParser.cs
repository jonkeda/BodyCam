using System.Buffers.Binary;
using System.Text;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Stateless parsers for GlassModelControlResponse payloads returned by
/// LargeDataHandler.GlassesControl. Separated into a static class so Wave 5 unit tests
/// can validate parsing without the bridge. Payload byte layouts derived from CyanBridge
/// reverse-engineering and sdk-api-reference.md.
/// </summary>
internal static class HeyCyanFrameParser
{
    /// <summary>
    /// Parse version/device info response.
    /// CyanBridge: LargeDataHandler.SyncDeviceInfo callback → DeviceInfoResponse.
    /// Payload layout (provisional, deferred to hardware):
    ///   [0..n] ASCII or UTF-8 strings, null-terminated or length-prefixed.
    /// </summary>
    public static HeyCyanVersionInfo ParseVersion(byte[] payload)
    {
        // Placeholder parsing — real format TBD with hardware.
        // CyanBridge logs show multi-field responses but no definitive struct.
        if (payload.Length < 20)
        {
            return new HeyCyanVersionInfo("unknown", "unknown", "unknown", "unknown", "00:00:00:00:00:00");
        }

        // Provisional: assume fixed offsets or comma-separated ASCII.
        // This will be corrected in hardware testing (M33 Phase 1 verification).
        var text = Encoding.UTF8.GetString(payload).TrimEnd('\0');
        var parts = text.Split(',', StringSplitOptions.TrimEntries);

        return new HeyCyanVersionInfo(
            Hardware: parts.Length > 0 ? parts[0] : "unknown",
            Firmware: parts.Length > 1 ? parts[1] : "unknown",
            WifiHardware: parts.Length > 2 ? parts[2] : "unknown",
            WifiFirmware: parts.Length > 3 ? parts[3] : "unknown",
            MacAddress: parts.Length > 4 ? parts[4] : "00:00:00:00:00:00");
    }

    /// <summary>
    /// Parse battery response.
    /// CyanBridge: LargeDataHandler.SyncBattery → async notify, type 0x05.
    /// Payload: loadData[7] = battery %, loadData[8] = charging (0/1).
    /// </summary>
    public static HeyCyanBattery ParseBattery(byte[] payload)
    {
        if (payload.Length < 2)
            return new HeyCyanBattery(0, false);

        return new HeyCyanBattery(payload[0], payload[1] != 0);
    }

    /// <summary>
    /// Parse media counts response (photo/video/audio file counts).
    /// CyanBridge: 0x02 0x04 command → GlassModelControlResponse with imageCount, videoCount, recordCount.
    /// Payload layout (provisional):
    ///   [0..3] imageCount (uint32 LE)
    ///   [4..7] videoCount (uint32 LE)
    ///   [8..11] recordCount (uint32 LE)
    /// </summary>
    public static HeyCyanMediaCount ParseMediaCounts(byte[] payload)
    {
        if (payload.Length < 12)
            return new HeyCyanMediaCount(0, 0, 0);

        var photos = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4));
        var videos = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4, 4));
        var audio = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(8, 4));

        return new HeyCyanMediaCount(photos, videos, audio);
    }

    /// <summary>
    /// Parse button gesture from GlassesDeviceNotifyRsp frame.
    /// CyanBridge: loadData[6] == 0x02 (AI-photo) or 0x03 (AI-voice).
    /// Frame layout: loadData[0..5] header, loadData[6] notify type, loadData[7..] payload.
    /// </summary>
    public static bool TryParseButton(byte[] frame, out HeyCyanButtonGesture gesture)
    {
        gesture = default;
        if (frame.Length < 8) return false;

        var notifyType = frame[6];
        switch (notifyType)
        {
            case 0x02: // AI-photo button
                gesture = HeyCyanButtonGesture.Tap;
                return true;
            case 0x03: // AI-voice button
                gesture = HeyCyanButtonGesture.DoubleTap;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Parse transfer-mode IP address from notify frame.
    /// CyanBridge: loadData[6] == 0x08, loadData[7..10] = IPv4 bytes.
    /// </summary>
    public static bool TryParseTransferIp(byte[] frame, out System.Net.IPAddress? ip)
    {
        ip = null;
        if (frame.Length < 11 || frame[6] != 0x08) return false;

        ip = new System.Net.IPAddress(frame.AsSpan(7, 4));
        return true;
    }

    /// <summary>
    /// Classify P2P/Wi-Fi error severity.
    /// CyanBridge: loadData[6] == 0x09, loadData[7] = error code.
    /// Code 0xFF is transient noise; other codes need user-facing handling.
    /// </summary>
    public static HeyCyanP2pErrorKind ClassifyP2pError(byte[] frame)
    {
        if (frame.Length < 8 || frame[6] != 0x09)
            return HeyCyanP2pErrorKind.None;

        return frame[7] == 0xFF
            ? HeyCyanP2pErrorKind.Noisy
            : HeyCyanP2pErrorKind.Fatal;
    }
}

public enum HeyCyanP2pErrorKind
{
    None,
    Noisy,   // 0xFF — transient, ignore
    Fatal    // other codes — user-facing
}
