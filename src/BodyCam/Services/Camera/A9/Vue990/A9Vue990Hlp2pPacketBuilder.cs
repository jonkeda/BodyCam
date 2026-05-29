using System.Net;
using System.Text;

namespace BodyCam.Services.Camera.A9.Vue990;

public static class A9Vue990Hlp2pPacketBuilder
{
    public const ushort Hello = 0xf100;
    public const ushort P2pRequest = 0xf120;
    public const ushort LanSearch = 0xf130;
    public const ushort LanSearchExtended = 0xf132;
    public const ushort PunchPacket = 0xf141;
    public const ushort P2pReady = 0xf142;
    public const ushort ListRequest = 0xf167;
    public const ushort P2pAlive = 0xf1e0;
    public const ushort P2pAliveAck = 0xf1e1;

    private const int P2pIdLength = 20;
    private const int P2pIdPrefixLength = 8;
    private const int P2pIdSuffixLength = 8;
    private const int ReverseAddressLength = 16;

    public static byte[] BuildHeader(ushort command, ushort payloadLength = 0)
    {
        return
        [
            (byte)(command >> 8),
            (byte)command,
            (byte)(payloadLength >> 8),
            (byte)payloadLength,
        ];
    }

    public static byte[] BuildLanSearch()
    {
        return BuildHeader(LanSearch);
    }

    public static byte[] BuildLanSearchExtended()
    {
        return BuildHeader(LanSearchExtended);
    }

    public static byte[] BuildListRequest(ReadOnlySpan<byte> p2pId)
    {
        return BuildP2pIdPacket(ListRequest, p2pId);
    }

    public static byte[] BuildPunchPacket(ReadOnlySpan<byte> p2pId)
    {
        return BuildP2pIdPacket(PunchPacket, p2pId);
    }

    public static byte[] BuildP2pReady(ReadOnlySpan<byte> p2pId)
    {
        return BuildP2pIdPacket(P2pReady, p2pId);
    }

    public static byte[] BuildP2pAlive()
    {
        return BuildHeader(P2pAlive);
    }

    public static byte[] BuildP2pAliveAck()
    {
        return BuildHeader(P2pAliveAck);
    }

    public static byte[] BuildP2pRequest4(ReadOnlySpan<byte> p2pId, IPAddress localAddress, ushort localPort)
    {
        return BuildP2pRequest4(p2pId, BuildReverseAddress4(localAddress, localPort));
    }

    public static byte[] BuildP2pRequest4Readable(ReadOnlySpan<byte> p2pId, IPAddress localAddress, ushort localPort)
    {
        return BuildP2pRequest4(p2pId, BuildReadableReverseAddress4(localAddress, localPort));
    }

    public static byte[] BuildReverseAddress4(IPAddress address, ushort port)
    {
        var addressBytes = address.GetAddressBytes();
        if (addressBytes.Length != 4)
            throw new ArgumentException("HLP2P IPv4 reverse addresses require an IPv4 address.", nameof(address));

        var bytes = new byte[ReverseAddressLength];
        bytes[0] = 0x00;
        bytes[1] = 0x02;
        bytes[2] = (byte)port;
        bytes[3] = (byte)(port >> 8);
        bytes[4] = addressBytes[3];
        bytes[5] = addressBytes[2];
        bytes[6] = addressBytes[1];
        bytes[7] = addressBytes[0];
        return bytes;
    }

    public static byte[] BuildReadableReverseAddress4(IPAddress address, ushort port)
    {
        var addressBytes = address.GetAddressBytes();
        if (addressBytes.Length != 4)
            throw new ArgumentException("HLP2P IPv4 reverse addresses require an IPv4 address.", nameof(address));

        var bytes = new byte[ReverseAddressLength];
        bytes[0] = 0x00;
        bytes[1] = 0x02;
        bytes[2] = (byte)(port >> 8);
        bytes[3] = (byte)port;
        addressBytes.CopyTo(bytes.AsSpan(4, 4));
        return bytes;
    }

    public static byte[] BuildCompactP2pId(string value)
    {
        var compact = value.Trim();
        var prefixLength = 0;
        while (prefixLength < compact.Length && char.IsLetter(compact[prefixLength]))
            prefixLength++;

        var digitsOffset = prefixLength;
        var digitsLength = 0;
        while (digitsOffset + digitsLength < compact.Length && char.IsDigit(compact[digitsOffset + digitsLength]))
            digitsLength++;

        if (prefixLength == 0 || digitsLength == 0 || digitsOffset + digitsLength >= compact.Length)
            return BuildAsciiPaddedP2pId(compact);

        var prefix = compact[..prefixLength];
        var numberText = compact.Substring(digitsOffset, digitsLength);
        var suffix = compact[(digitsOffset + digitsLength)..];
        if (!uint.TryParse(numberText, out var number))
            return BuildAsciiPaddedP2pId(compact);

        return BuildStructuredP2pId(prefix, number, suffix);
    }

    public static byte[] BuildDelimitedP2pId(string value)
    {
        var parts = value.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !uint.TryParse(parts[1], out var number))
            return BuildAsciiPaddedP2pId(value);

        return BuildStructuredP2pId(parts[0], number, parts[2]);
    }

    public static byte[] BuildAsciiPaddedP2pId(string value)
    {
        var bytes = new byte[P2pIdLength];
        var ascii = Encoding.ASCII.GetBytes(value.Trim());
        ascii.AsSpan(0, Math.Min(ascii.Length, bytes.Length)).CopyTo(bytes);
        return bytes;
    }

    public static IReadOnlyList<(string Name, byte[] P2pId)> BuildP2pIdCandidates(string compactOrDelimitedId)
    {
        var compact = compactOrDelimitedId.Trim();
        var candidates = new List<(string Name, byte[] P2pId)>
        {
            ("structured", compact.Contains('-', StringComparison.Ordinal)
                ? BuildDelimitedP2pId(compact)
                : BuildCompactP2pId(compact)),
            ("ascii", BuildAsciiPaddedP2pId(compact)),
        };

        if (!compact.Contains('-', StringComparison.Ordinal) && TryFormatDelimited(compact, out var delimited))
            candidates.Add(("delimited-structured", BuildDelimitedP2pId(delimited)));

        return candidates
            .GroupBy(candidate => Convert.ToHexString(candidate.P2pId), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static byte[] BuildP2pIdPacket(ushort command, ReadOnlySpan<byte> p2pId)
    {
        var id = NormalizeP2pId(p2pId);
        var packet = new byte[4 + P2pIdLength];
        BuildHeader(command, P2pIdLength).CopyTo(packet.AsSpan(0, 4));
        id.CopyTo(packet.AsSpan(4, P2pIdLength));
        return packet;
    }

    private static byte[] BuildP2pRequest4(ReadOnlySpan<byte> p2pId, ReadOnlySpan<byte> reverseAddress)
    {
        var id = NormalizeP2pId(p2pId);
        if (reverseAddress.Length != ReverseAddressLength)
            throw new ArgumentException("HLP2P IPv4 reverse address must be 16 bytes.", nameof(reverseAddress));

        var packet = new byte[4 + P2pIdLength + ReverseAddressLength];
        BuildHeader(P2pRequest, P2pIdLength + ReverseAddressLength).CopyTo(packet.AsSpan(0, 4));
        id.CopyTo(packet.AsSpan(4, P2pIdLength));
        reverseAddress.CopyTo(packet.AsSpan(4 + P2pIdLength, ReverseAddressLength));
        return packet;
    }

    private static byte[] NormalizeP2pId(ReadOnlySpan<byte> p2pId)
    {
        var id = new byte[P2pIdLength];
        p2pId[..Math.Min(p2pId.Length, id.Length)].CopyTo(id);
        return id;
    }

    private static byte[] BuildStructuredP2pId(string prefix, uint number, string suffix)
    {
        var bytes = new byte[P2pIdLength];
        WriteAscii(bytes.AsSpan(0, P2pIdPrefixLength), prefix);
        bytes[8] = (byte)(number >> 24);
        bytes[9] = (byte)(number >> 16);
        bytes[10] = (byte)(number >> 8);
        bytes[11] = (byte)number;
        WriteAscii(bytes.AsSpan(12, P2pIdSuffixLength), suffix);
        return bytes;
    }

    private static void WriteAscii(Span<byte> destination, string value)
    {
        var ascii = Encoding.ASCII.GetBytes(value);
        ascii.AsSpan(0, Math.Min(ascii.Length, destination.Length)).CopyTo(destination);
    }

    private static bool TryFormatDelimited(string compact, out string delimited)
    {
        delimited = string.Empty;
        var prefixLength = 0;
        while (prefixLength < compact.Length && char.IsLetter(compact[prefixLength]))
            prefixLength++;

        var digitsOffset = prefixLength;
        var digitsLength = 0;
        while (digitsOffset + digitsLength < compact.Length && char.IsDigit(compact[digitsOffset + digitsLength]))
            digitsLength++;

        if (prefixLength == 0 || digitsLength == 0 || digitsOffset + digitsLength >= compact.Length)
            return false;

        if (!uint.TryParse(compact.Substring(digitsOffset, digitsLength), out var number))
            return false;

        delimited = $"{compact[..prefixLength]}-{number:000000}-{compact[(digitsOffset + digitsLength)..]}";
        return true;
    }
}
