namespace BodyCam.Services.Camera.A9.Vue990;

public sealed record A9Vue990Hlp2pLanHoleSeed(
    byte[] Nonce0,
    byte[] Nonce1,
    byte[] Nonce2,
    byte[] Nonce3,
    byte[] SessionToken,
    uint UidLittleEndian);

public sealed record A9Vue990Hlp2pLanHoleResponse(
    uint AidLittleEndian,
    byte[] SessionToken,
    byte Status,
    byte StatusDetail,
    byte[] Raw);

public sealed record A9Vue990Hlp2pLanHoleReady(
    uint AidLittleEndian,
    byte[] Raw);

public sealed record A9Vue990Hlp2pDirectDataPacket(
    ushort Sequence,
    byte Operation,
    byte Flags,
    ushort MessageId,
    ushort TailLength,
    ushort FragmentIndex,
    ushort Kind,
    byte Channel,
    byte[] Payload,
    byte[] Raw);

public static class A9Vue990Hlp2pDirectPacket
{
    public const byte LanHoleProbeType = 0x02;
    public const byte LanHoleResponseType = 0x11;
    public const byte LanHoleReadyType = 0x15;
    public const byte DirectPacketType = 0x0d;
    public const byte DirectDataOperation = 0x01;
    public const byte DirectCommandOperation = 0x00;
    public const byte DirectAckOperation = 0x08;

    public static ReadOnlySpan<byte> LanHoleProbeTail => [0xd6, 0x25, 0x35, 0x01];
    public static ReadOnlySpan<byte> LanHoleReadyTail => [0x6b, 0x25, 0x35, 0x01];
    public static ReadOnlySpan<byte> AliveProbe => [0x0b, 0x00, 0x00];
    public static ReadOnlySpan<byte> AliveAck => [0x0c];

    public static byte[] BuildLanHoleProbe(A9Vue990Hlp2pLanHoleSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        EnsureFourBytes(seed.Nonce0, nameof(seed.Nonce0));
        EnsureFourBytes(seed.Nonce1, nameof(seed.Nonce1));
        EnsureFourBytes(seed.Nonce2, nameof(seed.Nonce2));
        EnsureFourBytes(seed.Nonce3, nameof(seed.Nonce3));
        EnsureFourBytes(seed.SessionToken, nameof(seed.SessionToken));

        var bytes = new byte[25];
        bytes[0] = LanHoleProbeType;
        seed.Nonce0.CopyTo(bytes.AsSpan(1));
        seed.Nonce1.CopyTo(bytes.AsSpan(5));
        seed.Nonce2.CopyTo(bytes.AsSpan(9));
        seed.Nonce3.CopyTo(bytes.AsSpan(13));
        seed.SessionToken.CopyTo(bytes.AsSpan(17));
        LanHoleProbeTail.CopyTo(bytes.AsSpan(21));
        return bytes;
    }

    public static byte[] BuildLanHoleAck(A9Vue990Hlp2pLanHoleResponse response, uint uidLittleEndian)
    {
        ArgumentNullException.ThrowIfNull(response);
        EnsureFourBytes(response.SessionToken, nameof(response.SessionToken));

        var bytes = new byte[18];
        bytes[0] = LanHoleResponseType;
        bytes[1] = response.Status;
        WriteUInt32LittleEndian(bytes, 2, response.AidLittleEndian);
        response.SessionToken.CopyTo(bytes.AsSpan(6));
        LanHoleProbeTail.CopyTo(bytes.AsSpan(10));
        WriteUInt32LittleEndian(bytes, 14, uidLittleEndian);
        return bytes;
    }

    public static bool TryParseLanHoleResponse(
        ReadOnlySpan<byte> bytes,
        out A9Vue990Hlp2pLanHoleResponse response)
    {
        response = new A9Vue990Hlp2pLanHoleResponse(0, [], 0, 0, []);
        if (bytes.Length != 15 ||
            bytes[0] != LanHoleResponseType ||
            !bytes[11..15].SequenceEqual(LanHoleReadyTail))
        {
            return false;
        }

        response = new A9Vue990Hlp2pLanHoleResponse(
            ReadUInt32LittleEndian(bytes, 1),
            bytes[5..9].ToArray(),
            bytes[9],
            bytes[10],
            bytes.ToArray());
        return true;
    }

    public static bool TryParseLanHoleReady(
        ReadOnlySpan<byte> bytes,
        out A9Vue990Hlp2pLanHoleReady ready)
    {
        ready = new A9Vue990Hlp2pLanHoleReady(0, []);
        if (bytes.Length != 9 ||
            bytes[0] != LanHoleReadyType ||
            !bytes[5..9].SequenceEqual(LanHoleReadyTail))
        {
            return false;
        }

        ready = new A9Vue990Hlp2pLanHoleReady(ReadUInt32LittleEndian(bytes, 1), bytes.ToArray());
        return true;
    }

    public static bool TryParseDirectDataPacket(
        ReadOnlySpan<byte> bytes,
        out A9Vue990Hlp2pDirectDataPacket packet)
    {
        packet = new A9Vue990Hlp2pDirectDataPacket(0, 0, 0, 0, 0, 0, 0, 0, [], []);
        if (bytes.Length < 14 || bytes[0] != DirectPacketType)
            return false;

        var tailLength = ReadUInt16BigEndian(bytes, 7);
        if (tailLength < 9 || bytes.Length != 5 + tailLength)
            return false;

        packet = new A9Vue990Hlp2pDirectDataPacket(
            ReadUInt16BigEndian(bytes, 1),
            bytes[3],
            bytes[4],
            ReadUInt16BigEndian(bytes, 5),
            tailLength,
            ReadUInt16BigEndian(bytes, 9),
            ReadUInt16BigEndian(bytes, 11),
            bytes[13],
            bytes[14..].ToArray(),
            bytes.ToArray());
        return true;
    }

    public static byte[] BuildDirectAck(ushort ackSequence, A9Vue990Hlp2pDirectDataPacket received)
    {
        ArgumentNullException.ThrowIfNull(received);

        var bytes = new byte[11];
        bytes[0] = DirectPacketType;
        WriteUInt16BigEndian(bytes, 1, ackSequence);
        bytes[3] = DirectAckOperation;
        bytes[4] = 0x00;
        WriteUInt16BigEndian(bytes, 5, received.MessageId);
        WriteUInt16BigEndian(bytes, 7, received.Sequence);
        WriteUInt16BigEndian(bytes, 9, (ushort)Math.Max(0, received.TailLength - 8));
        return bytes;
    }

    public static bool IsAliveProbe(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length == 3 && bytes[0] == AliveProbe[0];
    }

    public static bool IsAliveAck(ReadOnlySpan<byte> bytes)
    {
        return bytes.SequenceEqual(AliveAck);
    }

    public static A9Vue990Hlp2pLanHoleSeed CreateObservedShapeSeed()
    {
        var seedBytes = new byte[24];
        System.Security.Cryptography.RandomNumberGenerator.Fill(seedBytes);
        seedBytes[19] = 0x01;

        return new A9Vue990Hlp2pLanHoleSeed(
            seedBytes[0..4],
            seedBytes[4..8],
            seedBytes[8..12],
            seedBytes[12..16],
            seedBytes[16..20],
            ReadUInt32LittleEndian(seedBytes, 20));
    }

    private static void EnsureFourBytes(byte[] bytes, string name)
    {
        if (bytes.Length != 4)
            throw new ArgumentException("Value must be exactly 4 bytes.", name);
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

    private static void WriteUInt32LittleEndian(Span<byte> bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return (uint)(bytes[offset] |
                      (bytes[offset + 1] << 8) |
                      (bytes[offset + 2] << 16) |
                      (bytes[offset + 3] << 24));
    }
}
