namespace BodyCam.Services.Camera.A9.Vue990;

public enum A9Vue990PpcsEncryption
{
    None = 0,
    Xor1 = 1,
}

public enum A9Vue990PpcsPacketType : byte
{
    Hello = 0x00,
    HelloAck = 0x01,
    RelayTo = 0x02,
    DeviceLoginAck = 0x11,
    P2pRequest = 0x20,
    P2pRequestAck = 0x21,
    LanSearch = 0x30,
    LanSearchExtended = 0x32,
    PunchTo = 0x40,
    PunchPacket = 0x41,
    P2pReady = 0x42,
    ListRequest = 0x67,
    ListenRequestAck = 0x69,
    RelayHelloAck = 0x70,
    RelayHelloAck2 = 0x71,
    Drw = 0xd0,
    DrwAck = 0xd1,
    P2pAlive = 0xe0,
    P2pAliveAck = 0xe1,
    Close = 0xf0,
}

public sealed class A9Vue990PpcsPacket
{
    public const byte Magic = 0xf1;
    public const byte DrwMarker = 0xd1;
    public const byte DrwAckMarker = 0xd2;
    public const byte CommandChannel = 0;
    public const byte VideoChannel = 1;
    public const byte AudioChannel = 2;

    public A9Vue990PpcsPacket(A9Vue990PpcsPacketType type, byte[] payload)
    {
        Type = type;
        Payload = payload;
    }

    public A9Vue990PpcsPacketType Type { get; }

    public byte[] Payload { get; }

    public static A9Vue990PpcsPacket Build(A9Vue990PpcsPacketType type, ReadOnlySpan<byte> payload = default)
    {
        return new A9Vue990PpcsPacket(type, payload.ToArray());
    }

    public static A9Vue990PpcsPacket BuildLanSearch()
    {
        return Build(A9Vue990PpcsPacketType.LanSearch);
    }

    public static A9Vue990PpcsPacket BuildLanSearchExtended(string clientId, string deviceId)
    {
        return Build(A9Vue990PpcsPacketType.LanSearchExtended, BuildIdentityPayload(clientId, deviceId));
    }

    public static A9Vue990PpcsPacket BuildP2pReady(ReadOnlySpan<byte> punchPayload)
    {
        const int p2pReadyPayloadLength = 0x14;
        var payload = new byte[p2pReadyPayloadLength];
        punchPayload[..Math.Min(punchPayload.Length, payload.Length)].CopyTo(payload);
        return Build(A9Vue990PpcsPacketType.P2pReady, payload);
    }

    public static byte[] BuildIdentityPayload(string clientId, string deviceId)
    {
        var clientBytes = System.Text.Encoding.ASCII.GetBytes(clientId);
        var deviceBytes = System.Text.Encoding.ASCII.GetBytes(deviceId);
        var payload = new byte[2 + clientBytes.Length + 2 + deviceBytes.Length];
        WriteUInt16BigEndian(payload, 0, (ushort)clientBytes.Length);
        clientBytes.CopyTo(payload.AsSpan(2));
        var deviceOffset = 2 + clientBytes.Length;
        WriteUInt16BigEndian(payload, deviceOffset, (ushort)deviceBytes.Length);
        deviceBytes.CopyTo(payload.AsSpan(deviceOffset + 2));
        return payload;
    }

    public static A9Vue990PpcsPacket BuildDrw(byte channel, ushort commandIndex, ReadOnlySpan<byte> drwPayload)
    {
        var payload = new byte[4 + drwPayload.Length];
        payload[0] = DrwMarker;
        payload[1] = channel;
        WriteUInt16LittleEndian(payload, 2, commandIndex);
        drwPayload.CopyTo(payload.AsSpan(4));
        return new A9Vue990PpcsPacket(A9Vue990PpcsPacketType.Drw, payload);
    }

    public static A9Vue990PpcsPacket BuildDrwAck(byte channel, ushort commandIndex)
    {
        var payload = new byte[6];
        payload[0] = DrwAckMarker;
        payload[1] = channel;
        WriteUInt16BigEndian(payload, 2, 1);
        WriteUInt16LittleEndian(payload, 4, commandIndex);
        return new A9Vue990PpcsPacket(A9Vue990PpcsPacketType.DrwAck, payload);
    }

    public byte[] ToArray()
    {
        if (Payload.Length > ushort.MaxValue)
            throw new InvalidOperationException("PPCS packet payload is too large.");

        var bytes = new byte[4 + Payload.Length];
        bytes[0] = Magic;
        bytes[1] = (byte)Type;
        WriteUInt16BigEndian(bytes, 2, (ushort)Payload.Length);
        Payload.CopyTo(bytes.AsSpan(4));
        return bytes;
    }

    public bool TryReadDrw(out byte channel, out ushort commandIndex, out ReadOnlyMemory<byte> drwPayload)
    {
        channel = 0;
        commandIndex = 0;
        drwPayload = default;

        if (Type != A9Vue990PpcsPacketType.Drw || Payload.Length < 4 || Payload[0] != DrwMarker)
            return false;

        channel = Payload[1];
        commandIndex = ReadUInt16LittleEndian(Payload, 2);
        drwPayload = Payload.AsMemory(4);
        return true;
    }

    public bool TryReadDrwAck(out byte channel, out ushort commandIndex)
    {
        channel = 0;
        commandIndex = 0;

        if (Type != A9Vue990PpcsPacketType.DrwAck || Payload.Length < 6 || Payload[0] != DrwAckMarker)
            return false;

        channel = Payload[1];
        commandIndex = ReadUInt16LittleEndian(Payload, 4);
        return true;
    }

    public bool TryReadPunchDeviceId(out string deviceId)
    {
        deviceId = string.Empty;

        if (Type != A9Vue990PpcsPacketType.PunchPacket &&
            Type != A9Vue990PpcsPacketType.P2pReady)
        {
            return false;
        }

        if (Payload.Length < 12)
            return false;

        var prefix = ReadAscii(Payload, 0, 4);
        var serial = ReadUInt64BigEndian(Payload, 4).ToString();
        var suffix = Payload.Length > 12 ? ReadAscii(Payload, 12, Payload.Length - 12) : string.Empty;
        deviceId = string.IsNullOrWhiteSpace(suffix) ? $"{prefix}-{serial}" : $"{prefix}-{serial}-{suffix}";
        return true;
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, out A9Vue990PpcsPacket packet)
    {
        packet = new A9Vue990PpcsPacket(A9Vue990PpcsPacketType.Hello, []);
        if (bytes.Length < 4 || bytes[0] != Magic)
            return false;

        var type = (A9Vue990PpcsPacketType)bytes[1];
        var length = ReadUInt16BigEndian(bytes, 2);
        if (bytes.Length < 4 + length)
            return false;

        packet = new A9Vue990PpcsPacket(type, bytes.Slice(4, length).ToArray());
        return true;
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> bytes,
        out A9Vue990PpcsEncryption encryption,
        out A9Vue990PpcsPacket packet)
    {
        if (TryParse(bytes, out packet))
        {
            encryption = A9Vue990PpcsEncryption.None;
            return true;
        }

        var decoded = A9Vue990PpcsEncryptionCodec.Xor1Decode(bytes);
        if (TryParse(decoded, out packet))
        {
            encryption = A9Vue990PpcsEncryption.Xor1;
            return true;
        }

        encryption = A9Vue990PpcsEncryption.None;
        packet = new A9Vue990PpcsPacket(A9Vue990PpcsPacketType.Hello, []);
        return false;
    }

    private static void WriteUInt16BigEndian(Span<byte> bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }

    private static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
    }

    private static void WriteUInt16LittleEndian(Span<byte> bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
    }

    private static ulong ReadUInt64BigEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        ulong value = 0;
        for (var i = 0; i < 8; i++)
            value = (value << 8) | bytes[offset + i];
        return value;
    }

    private static string ReadAscii(ReadOnlySpan<byte> bytes, int offset, int maxLength)
    {
        var length = 0;
        var limit = Math.Min(bytes.Length, offset + maxLength);
        while (offset + length < limit && bytes[offset + length] != 0)
            length++;

        return System.Text.Encoding.ASCII.GetString(bytes.Slice(offset, length));
    }
}

public static class A9Vue990PpcsEncryptionCodec
{
    private static readonly byte[] Xor1KeyTable =
    [
        0x7c, 0x9c, 0xe8, 0x4a, 0x13, 0xde, 0xdc, 0xb2, 0x2f, 0x21, 0x23, 0xe4, 0x30, 0x7b, 0x3d, 0x8c,
        0xbc, 0x0b, 0x27, 0x0c, 0x3c, 0xf7, 0x9a, 0xe7, 0x08, 0x71, 0x96, 0x00, 0x97, 0x85, 0xef, 0xc1,
        0x1f, 0xc4, 0xdb, 0xa1, 0xc2, 0xeb, 0xd9, 0x01, 0xfa, 0xba, 0x3b, 0x05, 0xb8, 0x15, 0x87, 0x83,
        0x28, 0x72, 0xd1, 0x8b, 0x5a, 0xd6, 0xda, 0x93, 0x58, 0xfe, 0xaa, 0xcc, 0x6e, 0x1b, 0xf0, 0xa3,
        0x88, 0xab, 0x43, 0xc0, 0x0d, 0xb5, 0x45, 0x38, 0x4f, 0x50, 0x22, 0x66, 0x20, 0x7f, 0x07, 0x5b,
        0x14, 0x98, 0x1d, 0x9b, 0xa7, 0x2a, 0xb9, 0xa8, 0xcb, 0xf1, 0xfc, 0x49, 0x47, 0x06, 0x3e, 0xb1,
        0x0e, 0x04, 0x3a, 0x94, 0x5e, 0xee, 0x54, 0x11, 0x34, 0xdd, 0x4d, 0xf9, 0xec, 0xc7, 0xc9, 0xe3,
        0x78, 0x1a, 0x6f, 0x70, 0x6b, 0xa4, 0xbd, 0xa9, 0x5d, 0xd5, 0xf8, 0xe5, 0xbb, 0x26, 0xaf, 0x42,
        0x37, 0xd8, 0xe1, 0x02, 0x0a, 0xae, 0x5f, 0x1c, 0xc5, 0x73, 0x09, 0x4e, 0x69, 0x24, 0x90, 0x6d,
        0x12, 0xb3, 0x19, 0xad, 0x74, 0x8a, 0x29, 0x40, 0xf5, 0x2d, 0xbe, 0xa5, 0x59, 0xe0, 0xf4, 0x79,
        0xd2, 0x4b, 0xce, 0x89, 0x82, 0x48, 0x84, 0x25, 0xc6, 0x91, 0x2b, 0xa2, 0xfb, 0x8f, 0xe9, 0xa6,
        0xb0, 0x9e, 0x3f, 0x65, 0xf6, 0x03, 0x31, 0x2e, 0xac, 0x0f, 0x95, 0x2c, 0x5c, 0xed, 0x39, 0xb7,
        0x33, 0x6c, 0x56, 0x7e, 0xb4, 0xa0, 0xfd, 0x7a, 0x81, 0x53, 0x51, 0x86, 0x8d, 0x9f, 0x77, 0xff,
        0x6a, 0x80, 0xdf, 0xe2, 0xbf, 0x10, 0xd7, 0x75, 0x64, 0x57, 0x76, 0xf3, 0x55, 0xcd, 0xd0, 0xc8,
        0x18, 0xe6, 0x36, 0x41, 0x62, 0xcf, 0x99, 0xf2, 0x32, 0x4c, 0x67, 0x60, 0x61, 0x92, 0xca, 0xd3,
        0xea, 0x63, 0x7d, 0x16, 0xb6, 0x8e, 0xd4, 0x68, 0x35, 0xc3, 0x52, 0x9d, 0x46, 0x44, 0x1e, 0x17,
    ];

    private static readonly byte[] Xor1EncodingKey = [0x69, 0x97, 0xcc, 0x19];

    public static byte[] Xor1Encode(ReadOnlySpan<byte> bytes)
    {
        return ProprietaryEncodeWithKeyBytes(Xor1EncodingKey, bytes);
    }

    public static byte[] Xor1Decode(ReadOnlySpan<byte> bytes)
    {
        return ProprietaryDecodeWithKeyBytes(Xor1EncodingKey, bytes);
    }

    public static byte[] ProprietaryEncode(string? key, ReadOnlySpan<byte> bytes)
    {
        if (string.IsNullOrEmpty(key))
            return bytes.ToArray();

        return ProprietaryEncodeWithKeyBytes(DeriveProprietaryKeyBytes(key), bytes);
    }

    public static byte[] ProprietaryDecode(string? key, ReadOnlySpan<byte> bytes)
    {
        if (string.IsNullOrEmpty(key))
            return bytes.ToArray();

        return ProprietaryDecodeWithKeyBytes(DeriveProprietaryKeyBytes(key), bytes);
    }

    public static byte[] TcpRelayEncode(ReadOnlySpan<byte> keySeed, ReadOnlySpan<byte> bytes)
    {
        return ProprietaryEncode(BuildTcpRelayKey(keySeed), bytes);
    }

    public static byte[] TcpRelayDecode(ReadOnlySpan<byte> keySeed, ReadOnlySpan<byte> bytes)
    {
        return ProprietaryDecode(BuildTcpRelayKey(keySeed), bytes);
    }

    public static byte[] ProprietaryEncodeWithKeyBytes(ReadOnlySpan<byte> keyBytes, ReadOnlySpan<byte> bytes)
    {
        if (keyBytes.Length < 4)
            throw new ArgumentException("A 4-byte proprietary key is required.", nameof(keyBytes));

        if (bytes.Length == 0)
            return [];

        var encoded = new byte[bytes.Length];
        encoded[0] = (byte)(bytes[0] ^ Xor1KeyTable[keyBytes[0]]);
        for (var i = 1; i < bytes.Length; i++)
        {
            var previous = encoded[i - 1];
            var index = (keyBytes[previous & 0x03] + previous) & 0xff;
            encoded[i] = (byte)(bytes[i] ^ Xor1KeyTable[index]);
        }

        return encoded;
    }

    public static byte[] ProprietaryDecodeWithKeyBytes(ReadOnlySpan<byte> keyBytes, ReadOnlySpan<byte> bytes)
    {
        if (keyBytes.Length < 4)
            throw new ArgumentException("A 4-byte proprietary key is required.", nameof(keyBytes));

        if (bytes.Length == 0)
            return [];

        var decoded = new byte[bytes.Length];
        decoded[0] = (byte)(bytes[0] ^ Xor1KeyTable[keyBytes[0]]);
        for (var i = 1; i < bytes.Length; i++)
        {
            var previous = bytes[i - 1];
            var index = (keyBytes[previous & 0x03] + previous) & 0xff;
            decoded[i] = (byte)(bytes[i] ^ Xor1KeyTable[index]);
        }

        return decoded;
    }

    public static byte[] DeriveProprietaryKeyBytes(string key)
    {
        var keyBytes = System.Text.Encoding.ASCII.GetBytes(key);
        var length = Math.Min(keyBytes.Length, 20);
        if (length == 0)
            return [0, 0, 0, 0];

        var sum = 0;
        var negativeSum = 0;
        var weighted = 0;
        var xor = 0;
        for (var i = 0; i < length; i++)
        {
            var value = keyBytes[i];
            sum += value;
            negativeSum -= value;
            weighted += (value * 0xab) >> 9;
            xor ^= value;
        }

        return
        [
            (byte)sum,
            (byte)negativeSum,
            (byte)weighted,
            (byte)xor,
        ];
    }

    private static string BuildTcpRelayKey(ReadOnlySpan<byte> keySeed)
    {
        if (keySeed.Length < 2)
            throw new ArgumentException("TCP relay encryption needs at least two seed bytes.", nameof(keySeed));

        return $"{keySeed[0]:X2}{keySeed[1]:X2}";
    }
}

public sealed class A9Vue990VideoFrameAssembler
{
    public static ReadOnlySpan<byte> VideoChunkMarker => [0x55, 0xaa, 0x15, 0xa8];

    private readonly Dictionary<long, byte[]> _chunks = [];
    private readonly SortedSet<long> _boundaries = [];
    private ushort? _lastChunkIndex;
    private long _epoch;
    private long _lastPublishedBoundary = -1;

    public IReadOnlyList<byte[]> AddVideoDrwChunk(ushort chunkIndex, ReadOnlySpan<byte> drwPayload)
    {
        var absoluteIndex = ToAbsoluteIndex(chunkIndex);
        if (drwPayload.StartsWith(VideoChunkMarker))
        {
            _boundaries.Add(absoluteIndex);
            _chunks[absoluteIndex] = drwPayload.Length >= 0x20
                ? drwPayload[0x20..].ToArray()
                : [];
        }
        else
        {
            _chunks[absoluteIndex] = drwPayload.ToArray();
        }

        return ExtractCompletedFrames();
    }

    private long ToAbsoluteIndex(ushort chunkIndex)
    {
        if (_lastChunkIndex is > 0xff00 && chunkIndex < 0x0100)
            _epoch++;
        else if (_lastChunkIndex is < 0x0100 && chunkIndex > 0xff00 && _epoch > 0)
            _epoch--;

        _lastChunkIndex = chunkIndex;
        return chunkIndex + (_epoch * 0x10000L);
    }

    private IReadOnlyList<byte[]> ExtractCompletedFrames()
    {
        if (_boundaries.Count < 2)
            return [];

        var frames = new List<byte[]>();
        var boundaries = _boundaries.ToArray();
        for (var i = 0; i < boundaries.Length - 1; i++)
        {
            var start = boundaries[i];
            var end = boundaries[i + 1];
            if (start <= _lastPublishedBoundary)
                continue;

            var buffers = new List<byte[]>();
            var complete = true;
            for (var chunk = start; chunk < end; chunk++)
            {
                if (_chunks.TryGetValue(chunk, out var value))
                {
                    buffers.Add(value);
                    continue;
                }

                complete = false;
                break;
            }

            if (!complete)
                continue;

            var frame = new byte[buffers.Sum(buffer => buffer.Length)];
            var offset = 0;
            foreach (var buffer in buffers)
            {
                buffer.CopyTo(frame.AsSpan(offset));
                offset += buffer.Length;
            }

            frames.Add(frame);
            _lastPublishedBoundary = start;
        }

        PruneBefore(_lastPublishedBoundary);
        return frames;
    }

    private void PruneBefore(long boundary)
    {
        if (boundary < 0)
            return;

        foreach (var key in _chunks.Keys.Where(key => key < boundary).ToArray())
            _chunks.Remove(key);

        foreach (var item in _boundaries.Where(item => item < boundary).ToArray())
            _boundaries.Remove(item);
    }
}
