using System.Text;

namespace BodyCam.Services.Camera.A9.Vue990;

public static class A9Vue990PpcsControlCommandBuilder
{
    public const ushort DrwStartMarker = 0x110a;

    public const ushort ConnectUser = 0x2010;
    public const ushort ConnectUserAck = 0x2011;
    public const ushort DeviceStatus = 0x0810;
    public const ushort DeviceStatusAck = 0x0811;
    public const ushort StartVideo = 0x1030;
    public const ushort StartVideoAck = 0x1031;
    public const ushort StopVideo = 0x1130;
    public const ushort VideoParamSet = 0x1830;
    public const ushort VideoParamSetAck = 0x1831;

    private const int TicketLength = 4;
    private const int RotateKey = 4;

    public static byte[] BuildConnectUser(ref ushort sequence, ReadOnlySpan<byte> ticket, string username, string password)
    {
        var payload = new byte[0x20 + 0x80];
        WriteAscii(payload.AsSpan(0, 0x20), username);
        WriteAscii(payload.AsSpan(0x20, 0x80), password);

        return BuildControl(ref sequence, ticket, ConnectUser, payload);
    }

    public static byte[] BuildStartVideo(ref ushort sequence, ReadOnlySpan<byte> ticket)
    {
        return BuildControl(ref sequence, ticket, StartVideo);
    }

    public static byte[] BuildStopVideo(ref ushort sequence, ReadOnlySpan<byte> ticket)
    {
        return BuildControl(ref sequence, ticket, StopVideo);
    }

    public static byte[] BuildDeviceStatus(ref ushort sequence, ReadOnlySpan<byte> ticket)
    {
        return BuildControl(ref sequence, ticket, DeviceStatus);
    }

    public static byte[] BuildVideoResolution(ref ushort sequence, ReadOnlySpan<byte> ticket, byte resolutionId)
    {
        var payload = new byte[8];
        payload[0] = 0x01;
        payload[4] = resolutionId;
        return BuildControl(ref sequence, ticket, VideoParamSet, payload);
    }

    public static byte[] BuildControl(
        ref ushort sequence,
        ReadOnlySpan<byte> ticket,
        ushort command,
        ReadOnlySpan<byte> payload = default)
    {
        var safeTicket = new byte[TicketLength];
        ticket[..Math.Min(ticket.Length, TicketLength)].CopyTo(safeTicket);

        var encryptedPayload = payload.ToArray();
        if (encryptedPayload.Length > RotateKey)
            XqBytesEnc(encryptedPayload, RotateKey);

        var controlPayloadLength = checked((ushort)(TicketLength + encryptedPayload.Length));
        var drwPayload = new byte[8 + controlPayloadLength];
        WriteUInt16BigEndian(drwPayload, 0, DrwStartMarker);
        WriteUInt16BigEndian(drwPayload, 2, command);
        WriteUInt16LittleEndian(drwPayload, 4, controlPayloadLength);
        WriteUInt16BigEndian(drwPayload, 6, GetControlDestination(command));
        safeTicket.CopyTo(drwPayload.AsSpan(8, TicketLength));
        encryptedPayload.CopyTo(drwPayload.AsSpan(12));

        var packet = A9Vue990PpcsPacket
            .BuildDrw(A9Vue990PpcsPacket.CommandChannel, sequence, drwPayload)
            .ToArray();

        sequence++;
        return packet;
    }

    public static bool TryReadControlHeader(
        ReadOnlySpan<byte> drwPayload,
        out ushort command,
        out ushort payloadLength,
        out ushort destination)
    {
        command = 0;
        payloadLength = 0;
        destination = 0;

        if (drwPayload.Length < 8 || ReadUInt16BigEndian(drwPayload, 0) != DrwStartMarker)
            return false;

        command = ReadUInt16BigEndian(drwPayload, 2);
        payloadLength = ReadUInt16LittleEndian(drwPayload, 4);
        destination = ReadUInt16BigEndian(drwPayload, 6);
        return drwPayload.Length >= 8 + payloadLength;
    }

    public static void XqBytesEnc(Span<byte> bytes, int rotate)
    {
        if (bytes.Length == 0)
            return;

        rotate = Math.Clamp(rotate, 0, bytes.Length);
        var transformed = new byte[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
            transformed[i] = FlipLeastSignificantBit(bytes[i]);

        transformed.AsSpan(rotate).CopyTo(bytes);
        transformed.AsSpan(0, rotate).CopyTo(bytes[(bytes.Length - rotate)..]);
    }

    public static void XqBytesDec(Span<byte> bytes, int rotate)
    {
        if (bytes.Length == 0)
            return;

        rotate = Math.Clamp(rotate, 0, bytes.Length);
        var transformed = new byte[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
            transformed[i] = FlipLeastSignificantBit(bytes[i]);

        transformed.AsSpan(0, bytes.Length - rotate).CopyTo(bytes[rotate..]);
        transformed.AsSpan(bytes.Length - rotate).CopyTo(bytes[..rotate]);
    }

    public static ushort GetControlDestination(ushort command)
    {
        return command switch
        {
            ConnectUser => 0xff00,
            ConnectUserAck or DeviceStatusAck or StartVideoAck or VideoParamSetAck => 0xaa55,
            _ => 0x0000,
        };
    }

    private static void WriteAscii(Span<byte> destination, string value)
    {
        Encoding.ASCII.GetBytes(value.AsSpan(0, Math.Min(value.Length, destination.Length)), destination);
    }

    private static byte FlipLeastSignificantBit(byte value)
    {
        return (byte)((value & 1) == 0 ? value + 1 : value - 1);
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
}
