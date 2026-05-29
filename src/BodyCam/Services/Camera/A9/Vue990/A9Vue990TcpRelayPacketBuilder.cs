using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace BodyCam.Services.Camera.A9.Vue990;

public static class A9Vue990TcpRelayPacketBuilder
{
    private const byte TcpRelayCommand = 0x68;
    private const byte PpcsMagic = 0xf1;
    private const byte TcpRsLgn = 0x50;
    private const byte TcpRlyReq = 0x53;
    private const int TcpHeaderLength = 8;
    private const int CopiedTextLength = 7;

    public static byte[] BuildTcpRlyReq(
        string clientId,
        string vuid,
        string relayName,
        ReadOnlySpan<byte> sockaddrCs2Network,
        byte mode = 0,
        ReadOnlySpan<byte> sessionKey = default,
        byte flag = 0,
        ReadOnlySpan<byte> seed = default)
    {
        var plain = BuildTcpRlyReqPlain(vuid, relayName, sockaddrCs2Network, mode, sessionKey, flag);
        return BuildTcpRelayMessage(clientId, plain, seed);
    }

    public static byte[] BuildTcpRsLgn(
        string clientId,
        string vuid,
        string relayName,
        ReadOnlySpan<byte> sockaddrCs2Network,
        ushort value1 = 0,
        ushort value2 = 0,
        ushort value3 = 0,
        ushort value4 = 0,
        uint value5 = 0,
        ReadOnlySpan<byte> seed = default)
    {
        var plain = BuildTcpRsLgnPlain(vuid, relayName, sockaddrCs2Network, value1, value2, value3, value4, value5);
        return BuildTcpRelayMessage(clientId, plain, seed);
    }

    public static byte[] BuildTcpRlyReqPlain(
        string vuid,
        string relayName,
        ReadOnlySpan<byte> sockaddrCs2Network,
        byte mode = 0,
        ReadOnlySpan<byte> sessionKey = default,
        byte flag = 0)
    {
        if (sockaddrCs2Network.Length < 8)
            throw new ArgumentException("A network-order sockaddr_cs2 prefix of at least 8 bytes is required.", nameof(sockaddrCs2Network));

        var plain = new byte[0x38];
        plain[0] = PpcsMagic;
        plain[1] = TcpRlyReq;
        BinaryPrimitives.WriteUInt16BigEndian(plain.AsSpan(2), 0x34);
        WriteFixedAscii(plain, 4, vuid, CopiedTextLength);
        BinaryPrimitives.WriteUInt32BigEndian(plain.AsSpan(12), (uint)AsciiLength(vuid));
        WriteFixedAscii(plain, 16, relayName, CopiedTextLength);
        sockaddrCs2Network[..8].CopyTo(plain.AsSpan(24));
        plain[48] = mode;

        if (!sessionKey.IsEmpty)
            sessionKey[..Math.Min(3, sessionKey.Length)].CopyTo(plain.AsSpan(49));

        plain[52] = flag;
        return plain;
    }

    public static byte[] BuildTcpRsLgnPlain(
        string vuid,
        string relayName,
        ReadOnlySpan<byte> sockaddrCs2Network,
        ushort value1 = 0,
        ushort value2 = 0,
        ushort value3 = 0,
        ushort value4 = 0,
        uint value5 = 0)
    {
        if (sockaddrCs2Network.Length < 8)
            throw new ArgumentException("A network-order sockaddr_cs2 prefix of at least 8 bytes is required.", nameof(sockaddrCs2Network));

        var plain = new byte[0x3c];
        plain[0] = PpcsMagic;
        plain[1] = TcpRsLgn;
        BinaryPrimitives.WriteUInt16BigEndian(plain.AsSpan(2), 0x38);
        WriteFixedAscii(plain, 4, vuid, CopiedTextLength);
        BinaryPrimitives.WriteUInt32BigEndian(plain.AsSpan(12), (uint)AsciiLength(vuid));
        WriteFixedAscii(plain, 16, relayName, CopiedTextLength);
        BinaryPrimitives.WriteUInt32BigEndian(plain.AsSpan(24), value5);
        BinaryPrimitives.WriteUInt16BigEndian(plain.AsSpan(28), value1);
        BinaryPrimitives.WriteUInt16BigEndian(plain.AsSpan(30), value2);
        BinaryPrimitives.WriteUInt16BigEndian(plain.AsSpan(32), value3);
        BinaryPrimitives.WriteUInt16BigEndian(plain.AsSpan(34), value4);
        sockaddrCs2Network[..8].CopyTo(plain.AsSpan(36));
        return plain;
    }

    public static byte[] BuildTcpRelayMessage(
        string clientId,
        ReadOnlySpan<byte> plainPayload,
        ReadOnlySpan<byte> seed = default)
    {
        if (plainPayload.Length > ushort.MaxValue)
            throw new ArgumentException("The TCP relay payload is too large.", nameof(plainPayload));

        Span<byte> actualSeed = stackalloc byte[2];
        if (seed.IsEmpty)
        {
            RandomNumberGenerator.Fill(actualSeed);
        }
        else
        {
            if (seed.Length < 2)
                throw new ArgumentException("A TCP relay seed must contain two bytes.", nameof(seed));

            seed[..2].CopyTo(actualSeed);
        }

        var inner = A9Vue990PpcsEncryptionCodec.ProprietaryEncode(clientId, plainPayload);
        var outer = A9Vue990PpcsEncryptionCodec.TcpRelayEncode(actualSeed, inner);
        var crc = CalculateTcpRelayCrc(outer);

        var packet = new byte[TcpHeaderLength + outer.Length];
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(0), (ushort)plainPayload.Length);
        packet[2] = TcpRelayCommand;
        packet[3] = 0;
        packet[4] = actualSeed[0];
        packet[5] = actualSeed[1];
        packet[6] = crc[0];
        packet[7] = crc[1];
        outer.CopyTo(packet.AsSpan(TcpHeaderLength));
        return packet;
    }

    public static byte[] BuildSockaddrCs2Network(IPAddress address, ushort port)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
            throw new ArgumentException("Only IPv4 relay addresses are supported for this Vue990 TCP relay packet.", nameof(address));

        return
        [
            0x00,
            0x02,
            (byte)port,
            (byte)(port >> 8),
            bytes[3],
            bytes[2],
            bytes[1],
            bytes[0],
        ];
    }

    public static byte[] CalculateTcpRelayCrc(ReadOnlySpan<byte> bytes)
    {
        byte first = 0x43;
        byte second = 0x53;

        if (bytes.IsEmpty)
            return [first, second];

        var last = bytes.Length - 1;
        for (var i = 0; ; i++)
        {
            var mixed = (byte)(first ^ bytes[i]);
            first = (byte)(mixed ^ second);

            if ((i & 1) == 0)
                second = (byte)(bytes[last] ^ mixed);

            last--;
            if (last == -1)
                break;
        }

        return [first, second];
    }

    private static void WriteFixedAscii(byte[] bytes, int offset, string value, int maxLength)
    {
        var valueBytes = Encoding.ASCII.GetBytes(value);
        valueBytes.AsSpan(0, Math.Min(valueBytes.Length, maxLength))
            .CopyTo(bytes.AsSpan(offset, maxLength));
    }

    private static int AsciiLength(string value)
    {
        return Encoding.ASCII.GetByteCount(value);
    }
}
