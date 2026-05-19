using System.Buffers.Binary;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Builds Serial Port protocol frames for the HeyCyan glasses.
/// Frame format: [0xBC][action][len_lo][len_hi][crc16_lo][crc16_hi][payload...]
/// All multi-byte fields are little-endian. CRC-16/ARC over payload only.
/// </summary>
internal static class HeyCyanCommands
{
    // Serial Port protocol action IDs
    private const byte ActionSyncTime = 0x40;
    private const byte ActionGlassesControl = 0x41;
    private const byte ActionBattery = 0x42;
    private const byte ActionDeviceInfo = 0x43;
    private const byte ActionHeartbeat = 0x45;
    private const byte ActionDeviceConfig = 0x47;

    /// <summary>
    /// Take photo. CyanBridge: binding.btnCamera → glassesControl(byteArrayOf(0x02, 0x01, 0x01)).
    /// Atomically enters camera mode and captures. Cannot be used while in transfer mode.
    /// </summary>
    public static byte[] StartPhotoMode() => BuildFrame(ActionGlassesControl, [0x02, 0x01, 0x01]);

    /// <summary>
    /// AI photo trigger. CyanBridge: AutoLoopVisualNoteGenerator → glassesControl(byteArrayOf(0x02, 0x01, 0x06, 0x02, 0x02)).
    /// Captures photo AND streams thumbnail back over BLE. Follow with StartPhotoMode() after 250ms.
    /// </summary>
    public static byte[] TakeAiPhoto() => BuildFrame(ActionGlassesControl, [0x02, 0x01, 0x06, 0x02, 0x02]);

    /// <summary>
    /// Stop photo/video mode. CyanBridge: controlVideoRecording(false).
    /// </summary>
    public static byte[] StopMode() => BuildFrame(ActionGlassesControl, [0x02, 0x01, 0x0b]);

    /// <summary>
    /// Get media count (photo/video/audio). CyanBridge: binding.btnMediaCount click handler.
    /// </summary>
    public static byte[] GetMediaCounts() => BuildFrame(ActionGlassesControl, [0x02, 0x04]);

    /// <summary>
    /// Enter transfer mode (enable Wi-Fi for HTTP media download).
    /// CyanBridge: startDataDownload() → glassesControl(byteArrayOf(0x02, 0x01, 0x04)).
    /// The 0x02 prefix triggers the AP/hotspot broadcast on the glasses.
    /// </summary>
    public static byte[] EnterTransferMode() => BuildFrame(ActionGlassesControl, [0x02, 0x01, 0x04]);

    /// <summary>
    /// Query glasses WiFi AP IP address (iOS SDK: getDeviceWifiIPSuccess).
    /// CmdTypeIP = 0x03. The glasses respond with the AP's IP once the hotspot is ready.
    /// This command MUST be polled after EnterTransferMode to trigger/confirm AP startup.
    /// </summary>
    public static byte[] GetWifiIP() => BuildFrame(ActionGlassesControl, [0x02, 0x03]);

    /// <summary>
    /// Exit transfer mode. CyanBridge: cancelDataDownloadAttempt().
    /// </summary>
    public static byte[] ExitTransferMode() => BuildFrame(ActionGlassesControl, [0x02, 0x01, 0x09]);

    /// <summary>
    /// Reset P2P (Wi-Fi Direct) state machine. CyanBridge: WifiP2pManagerSingleton.resetP2p().
    /// </summary>
    public static byte[] ResetP2p() => BuildFrame(ActionGlassesControl, [0x02, 0x01, 0x0F]);

    /// <summary>
    /// Start video recording.
    /// </summary>
    public static byte[] StartVideoRecording() => BuildFrame(ActionGlassesControl, [0x02, 0x01, 0x02]);

    /// <summary>
    /// Start audio recording.
    /// </summary>
    public static byte[] StartAudioRecording() => BuildFrame(ActionGlassesControl, [0x02, 0x01, 0x08]);

    /// <summary>
    /// Sync time to glasses. Payload: unix timestamp (4 bytes, little-endian).
    /// </summary>
    public static byte[] SyncTime(DateTimeOffset now)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)now.ToUnixTimeSeconds());
        return BuildFrame(ActionSyncTime, payload);
    }

    /// <summary>
    /// Get battery level. Response on notify: [percentage, charging_flag].
    /// </summary>
    public static byte[] GetBattery() => BuildFrame(ActionBattery, []);

    /// <summary>
    /// Get device version/info. Response on notify: version strings.
    /// </summary>
    public static byte[] GetVersion() => BuildFrame(ActionDeviceInfo, []);

    /// <summary>
    /// Heartbeat / keepalive. Glasses respond with status byte.
    /// </summary>
    public static byte[] Heartbeat() => BuildFrame(ActionHeartbeat, []);

    /// <summary>
    /// Get device config (iOS SDK: getDeviceConfigWithFinished, opcode 0x47).
    /// Called after GetWifiIP to verify the glasses are in WiFi hotspot mode
    /// and signal them to start broadcasting. CRITICAL for AP activation.
    /// </summary>
    public static byte[] GetDeviceConfig() => BuildFrame(ActionDeviceConfig, []);

    /// <summary>
    /// Builds a Serial Port protocol frame.
    /// Format: [0xBC][action][len_lo][len_hi][crc16_lo][crc16_hi][payload...]
    /// </summary>
    public static byte[] BuildFrame(byte action, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[6 + payload.Length];
        frame[0] = 0xBC;
        frame[1] = action;
        frame[2] = (byte)(payload.Length & 0xFF);
        frame[3] = (byte)((payload.Length >> 8) & 0xFF);
        if (payload.Length > 0)
        {
            var crc = Crc16(payload);
            frame[4] = (byte)(crc & 0xFF);
            frame[5] = (byte)((crc >> 8) & 0xFF);
            payload.CopyTo(frame.AsSpan(6));
        }
        else
        {
            frame[4] = 0xFF;
            frame[5] = 0xFF;
        }
        return frame;
    }

    /// <summary>
    /// CRC-16/ARC: initial=0xFFFF, polynomial=0xA001 (reflected).
    /// </summary>
    private static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 1) != 0)
                    crc = (ushort)((crc >> 1) ^ 0xA001);
                else
                    crc >>= 1;
            }
        }
        return crc;
    }
}
