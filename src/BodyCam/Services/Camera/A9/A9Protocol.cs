namespace BodyCam.Services.Camera.A9;

/// <summary>
/// Constants and helpers for the iLnkP2P/PPPP protocol used by A9/X5 cameras.
/// Protocol reference: https://github.com/DavidVentura/cam-reverse
/// </summary>
internal static class A9Protocol
{
    // ── Top-level command IDs (first 2 bytes of every packet) ──
    public const ushort CmdLanSearch = 0xf130;
    public const ushort CmdPunchPkt = 0xf141;
    public const ushort CmdP2pRdy = 0xf142;
    public const ushort CmdP2pAlive = 0xf1e0;
    public const ushort CmdP2pAliveAck = 0xf1e1;
    public const ushort CmdDrw = 0xf1d0;
    public const ushort CmdDrwAck = 0xf1d1;
    public const ushort CmdClose = 0xf1f0;

    // ── Control sub-command IDs (inside Drw payloads) ──
    public const ushort CtrlConnectUser = 0x2010;
    public const ushort CtrlConnectUserAck = 0x2011;
    public const ushort CtrlStartVideo = 0x1030;
    public const ushort CtrlStartVideoAck = 0x1031;
    public const ushort CtrlStopVideo = 0x1130;
    public const ushort CtrlVideoParamSet = 0x1830;
    public const ushort CtrlVideoParamSetAck = 0x1831;
    public const ushort CtrlDevStatus = 0x0810;
    public const ushort CtrlDevStatusAck = 0x0811;

    // ── Drw framing ──
    public const ushort DrwStartMarker = 0x110a;

    // ── Stream type discriminators ──
    public const byte StreamTypeJpeg = 0x03;
    public const byte StreamTypeAudio = 0x06;

    // ── Well-known frame header for audio data ──
    public static ReadOnlySpan<byte> AudioFrameHeader => [0x55, 0xaa, 0x15, 0xa8];

    // ── JPEG start-of-image marker ──
    public static ReadOnlySpan<byte> JpegHeader => [0xff, 0xd8, 0xff, 0xdb];

    // ── Default UDP port for camera discovery / session ──
    public const int DefaultPort = 32108;

    // ── Control-command destination tags ──
    public static ushort GetControlDest(ushort controlCmd) => controlCmd switch
    {
        CtrlConnectUser => 0xff00,
        CtrlStartVideo or CtrlStopVideo or CtrlDevStatus or CtrlVideoParamSet => 0x0000,
        CtrlConnectUserAck or CtrlDevStatusAck or CtrlStartVideoAck or CtrlVideoParamSetAck => 0xaa55,
        _ => 0x0000,
    };

    // ── Encryption used on control payloads > 5 bytes ──

    /// <summary>
    /// XqBytesEnc: flip the LSB of each byte, then rotate the buffer left by <paramref name="rotate"/> positions.
    /// Used to "encrypt" outgoing control payloads.
    /// </summary>
    public static void XqBytesEnc(Span<byte> buf, int rotate)
    {
        var tmp = new byte[buf.Length];
        for (int i = 0; i < buf.Length; i++)
            tmp[i] = (byte)((buf[i] & 1) != 0 ? buf[i] - 1 : buf[i] + 1);

        // Rotate left by `rotate`
        for (int i = 0; i < buf.Length - rotate; i++)
            buf[i] = tmp[i + rotate];
        for (int i = 0; i < rotate; i++)
            buf[buf.Length - rotate + i] = tmp[i];
    }

    /// <summary>
    /// XqBytesDec: flip the LSB of each byte, then rotate the buffer right by <paramref name="rotate"/> positions.
    /// Used to "decrypt" incoming control payloads.
    /// </summary>
    public static void XqBytesDec(Span<byte> buf, int rotate)
    {
        var tmp = new byte[buf.Length];
        for (int i = 0; i < buf.Length; i++)
            tmp[i] = (byte)((buf[i] & 1) != 0 ? buf[i] - 1 : buf[i] + 1);

        // Rotate right by `rotate`
        for (int i = rotate; i < buf.Length; i++)
            buf[i] = tmp[i - rotate];
        for (int i = 0; i < rotate; i++)
            buf[i] = tmp[buf.Length - rotate + i];
    }

    // ── Packet builders ──

    /// <summary>
    /// Build a 4-byte LanSearch packet: [0xf1, 0x30, 0x00, 0x00].
    /// Broadcast this on UDP port 32108 to discover cameras on the LAN.
    /// </summary>
    public static byte[] BuildLanSearch()
    {
        var buf = new byte[4];
        WriteU16BE(buf, 0, CmdLanSearch);
        WriteU16BE(buf, 2, 0);
        return buf;
    }

    /// <summary>
    /// Build a P2pRdy packet echoing back the device serial from a PunchPkt response.
    /// </summary>
    public static byte[] BuildP2pRdy(ReadOnlySpan<byte> punchPayload)
    {
        const int P2pRdySize = 0x14; // 20 bytes of serial info
        var buf = new byte[4 + P2pRdySize];
        WriteU16BE(buf, 0, CmdP2pRdy);
        WriteU16BE(buf, 2, P2pRdySize);
        punchPayload[..P2pRdySize].CopyTo(buf.AsSpan(4));
        return buf;
    }

    /// <summary>
    /// Build a 4-byte P2PAliveAck response to a keepalive ping.
    /// </summary>
    public static byte[] BuildP2pAliveAck()
    {
        var buf = new byte[4];
        WriteU16BE(buf, 0, CmdP2pAliveAck);
        WriteU16BE(buf, 2, 0);
        return buf;
    }

    /// <summary>
    /// Build a 4-byte P2PAlive keepalive ping.
    /// </summary>
    public static byte[] BuildP2pAlive()
    {
        var buf = new byte[4];
        WriteU16BE(buf, 0, CmdP2pAlive);
        WriteU16BE(buf, 2, 0);
        return buf;
    }

    /// <summary>
    /// Build a ConnectUser (login) Drw control packet.
    /// The payload is: char account[0x20] + char password[0x80] = 160 bytes.
    /// </summary>
    public static byte[] BuildConnectUser(ref int outgoingCmdId, byte[] ticket, string username, string password)
    {
        var payload = new byte[0x20 + 0x80]; // 160 bytes
        WriteAscii(payload, 0, username);
        WriteAscii(payload, 0x20, password);

        return BuildDrwControl(ref outgoingCmdId, ticket, CtrlConnectUser, payload);
    }

    /// <summary>
    /// Build a StartVideo Drw control packet (no extra payload).
    /// </summary>
    public static byte[] BuildStartVideo(ref int outgoingCmdId, byte[] ticket)
    {
        return BuildDrwControl(ref outgoingCmdId, ticket, CtrlStartVideo, null);
    }

    /// <summary>
    /// Build a VideoParamSet Drw control packet to set resolution.
    /// Resolution IDs: 1 = 320x240, 2 = 640x480.
    /// </summary>
    public static byte[] BuildVideoResolution(ref int outgoingCmdId, byte[] ticket, int resolutionId)
    {
        var payload = new byte[8];
        payload[0] = 0x01; // param: resolution
        payload[4] = (byte)resolutionId;
        return BuildDrwControl(ref outgoingCmdId, ticket, CtrlVideoParamSet, payload);
    }

    /// <summary>
    /// Build a DrwAck response for a received Drw data packet.
    /// </summary>
    public static byte[] BuildDrwAck(byte streamId, ushort packetId)
    {
        const int itemCount = 1;
        int replyLen = itemCount * 2 + 4;
        var buf = new byte[8 + itemCount * 2];
        WriteU16BE(buf, 0, CmdDrwAck);
        WriteU16BE(buf, 2, (ushort)replyLen);
        buf[4] = 0xd2;
        buf[5] = streamId;
        WriteU16BE(buf, 6, itemCount);
        WriteU16BE(buf, 8, packetId);
        return buf;
    }

    // ── Packet parsers ──

    /// <summary>
    /// Parse a PunchPkt to extract the device serial string (prefix + serial number + suffix).
    /// </summary>
    public static string ParsePunchPktDeviceId(ReadOnlySpan<byte> packet)
    {
        // Bytes 4..8: prefix (4 ASCII chars), Bytes 8..16: serial as u64, Bytes 16..: suffix
        var prefix = ReadAscii(packet, 4, 4);
        var serialU64 = ReadU64BE(packet, 8);
        var serial = serialU64.ToString();
        int len = ReadU16BE(packet, 2);
        var suffix = ReadAscii(packet, 16, len - 16 + 4);
        return prefix + serial + suffix;
    }

    /// <summary>
    /// Read the 2-byte big-endian command ID from a raw packet.
    /// </summary>
    public static ushort ReadCommandId(ReadOnlySpan<byte> packet)
    {
        return ReadU16BE(packet, 0);
    }

    // ── Internal helpers ──

    /// <summary>
    /// Build a Drw control packet wrapping a sub-command and optional payload.
    /// The payload is XqBytesEnc-encrypted when longer than 4 bytes.
    /// </summary>
    private static byte[] BuildDrwControl(ref int outgoingCmdId, byte[] ticket, ushort controlCmd, byte[]? payload)
    {
        const int drwHeaderLen = 0x10; // 16 bytes
        const int tokenLen = 4;
        const int rotateKey = 4;

        int payloadLen = tokenLen + (payload?.Length ?? 0);
        int pktLen = drwHeaderLen + payloadLen;
        var buf = new byte[pktLen];

        // Drw outer header: [cmd(2)] [pktLen-4(2)] [0xd1(1)] [channel=0(1)] [seqId(2)]
        WriteU16BE(buf, 0, CmdDrw);
        WriteU16BE(buf, 2, (ushort)(pktLen - 4));
        buf[4] = 0xd1; // control direction
        buf[5] = 0;    // channel
        WriteU16BE(buf, 6, (ushort)outgoingCmdId);

        // Control header: [startMarker(2)] [controlCmd(2)] [payloadLen LE(2)] [dest(2)]
        WriteU16BE(buf, 8, DrwStartMarker);
        WriteU16BE(buf, 10, controlCmd);
        WriteU16LE(buf, 12, (ushort)payloadLen);
        WriteU16BE(buf, 14, GetControlDest(controlCmd));

        // Ticket (4 bytes)
        ticket.AsSpan(0, 4).CopyTo(buf.AsSpan(16));

        // Optional payload — encrypt if > 4 bytes
        if (payload is { Length: > 0 })
        {
            var encPayload = (byte[])payload.Clone();
            if (encPayload.Length > rotateKey)
                XqBytesEnc(encPayload, rotateKey);
            encPayload.CopyTo(buf, 20);
        }

        outgoingCmdId++;
        return buf;
    }

    public static void WriteU16BE(Span<byte> buf, int offset, ushort value)
    {
        buf[offset] = (byte)(value >> 8);
        buf[offset + 1] = (byte)(value & 0xff);
    }

    public static void WriteU16LE(Span<byte> buf, int offset, ushort value)
    {
        buf[offset] = (byte)(value & 0xff);
        buf[offset + 1] = (byte)(value >> 8);
    }

    public static ushort ReadU16BE(ReadOnlySpan<byte> buf, int offset)
    {
        return (ushort)((buf[offset] << 8) | buf[offset + 1]);
    }

    public static ushort ReadU16LE(ReadOnlySpan<byte> buf, int offset)
    {
        return (ushort)(buf[offset] | (buf[offset + 1] << 8));
    }

    public static ulong ReadU64BE(ReadOnlySpan<byte> buf, int offset)
    {
        ulong val = 0;
        for (int i = 0; i < 8; i++)
            val = (val << 8) | buf[offset + i];
        return val;
    }

    private static void WriteAscii(Span<byte> buf, int offset, string value)
    {
        for (int i = 0; i < value.Length && offset + i < buf.Length; i++)
            buf[offset + i] = (byte)value[i];
    }

    private static string ReadAscii(ReadOnlySpan<byte> buf, int offset, int maxLen)
    {
        int end = Math.Min(offset + maxLen, buf.Length);
        int len = 0;
        for (int i = offset; i < end && buf[i] != 0; i++)
            len++;
        return System.Text.Encoding.ASCII.GetString(buf.Slice(offset, len));
    }
}
