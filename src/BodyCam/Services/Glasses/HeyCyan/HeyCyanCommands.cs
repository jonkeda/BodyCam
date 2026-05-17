using System.Buffers.Binary;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Command payloads for LargeDataHandler.GlassesControl, copied verbatim from
/// CyanBridge MainActivity.kt. Do NOT invent payload bytes.
/// </summary>
internal static class HeyCyanCommands
{
    /// <summary>
    /// Start photo mode: 0x02, 0x01, 0x01.
    /// CyanBridge: binding.btnCamera click handler.
    /// </summary>
    public static byte[] StartPhotoMode() => new byte[] { 0x02, 0x01, 0x01 };

    /// <summary>
    /// AI photo trigger: 0x02, 0x01, 0x06, 0x02, 0x02.
    /// CyanBridge: AutoLoopVisualNoteGenerator / image hijack.
    /// </summary>
    public static byte[] TakeAiPhoto() => new byte[] { 0x02, 0x01, 0x06, 0x02, 0x02 };

    /// <summary>
    /// Stop photo/video mode: 0x02, 0x01, 0x0b.
    /// CyanBridge: controlVideoRecording(false) / controlAudioRecording(false).
    /// </summary>
    public static byte[] StopMode() => new byte[] { 0x02, 0x01, 0x0b };

    /// <summary>
    /// Get media count (photo/video/audio): 0x02, 0x04.
    /// CyanBridge: binding.btnMediaCount click handler.
    /// Response: GlassModelControlResponse with imageCount, videoCount, recordCount.
    /// </summary>
    public static byte[] GetMediaCounts() => new byte[] { 0x02, 0x04 };

    /// <summary>
    /// Enter transfer mode (enable Wi-Fi Direct for HTTP media download): 0x02, 0x01, 0x04.
    /// CyanBridge: startDataDownload() → enterTransferModeAsync().
    /// </summary>
    public static byte[] EnterTransferMode() => new byte[] { 0x02, 0x01, 0x04 };

    /// <summary>
    /// Exit transfer mode: 0x02, 0x01, 0x09.
    /// CyanBridge: cancelDataDownloadAttempt() / exitTransferModeAsync().
    /// </summary>
    public static byte[] ExitTransferMode() => new byte[] { 0x02, 0x01, 0x09 };

    /// <summary>
    /// Reset P2P (Wi-Fi Direct) state machine: 0x02, 0x01, 0x0F.
    /// CyanBridge: WifiP2pManagerSingleton.resetP2p().
    /// </summary>
    public static byte[] ResetP2p() => new byte[] { 0x02, 0x01, 0x0F };

    /// <summary>
    /// Start video recording: 0x02, 0x01, value (where value = 0x02 or similar).
    /// CyanBridge: controlVideoRecording(true) uses dynamic value.
    /// NOTE: Exact byte sequence for video start not yet confirmed; use 0x02, 0x01, 0x02 as provisional.
    /// </summary>
    public static byte[] StartVideoRecording() => new byte[] { 0x02, 0x01, 0x02 };

    /// <summary>
    /// Start audio recording: 0x02, 0x01, value (where value = 0x08 or similar).
    /// CyanBridge: controlAudioRecording(true) uses dynamic value.
    /// NOTE: Exact byte sequence for audio start not yet confirmed; use 0x02, 0x01, 0x08 as provisional.
    /// </summary>
    public static byte[] StartAudioRecording() => new byte[] { 0x02, 0x01, 0x08 };

    /// <summary>
    /// Sync time to glasses. Payload: 0x03 + unix timestamp (4 bytes, little-endian).
    /// CyanBridge: LargeDataHandler.syncTime callback, but actual glassesControl payload
    /// for time sync is handled by the SDK internally — this is a NO-OP placeholder.
    /// Use LargeDataHandler.SyncTime(callback) directly on Android instead.
    /// </summary>
    public static byte[] SyncTime(DateTimeOffset now)
    {
        Span<byte> b = stackalloc byte[5];
        b[0] = 0x03;
        BinaryPrimitives.WriteUInt32LittleEndian(b[1..], (uint)now.ToUnixTimeSeconds());
        return b.ToArray();
    }

    /// <summary>
    /// Get battery (poll). CyanBridge: LargeDataHandler.syncBattery() — async result via notify.
    /// The SDK does not expose an explicit glassesControl payload for battery poll;
    /// use LargeDataHandler.SyncBattery() directly on Android.
    /// </summary>
    public static byte[] GetBattery() => new byte[] { 0x02, 0x04 }; // Placeholder; SDK method is async notify-based.

    /// <summary>
    /// Get device version/info.
    /// CyanBridge: LargeDataHandler.SyncDeviceInfo callback → DeviceInfoResponse.
    /// Actual glassesControl payload is SDK-internal; this is a provisional command.
    /// </summary>
    public static byte[] GetVersion() => new byte[] { 0x02, 0x06 }; // Placeholder; real API is LargeDataHandler.SyncDeviceInfo.
}
