using System.Buffers.Binary;
using System.Text;

namespace BodyCam.Services.Camera.A9.Vue990;

public static class A9Vue990CgiCommandBuilder
{
    public const byte FrameMarker = 0xd1;
    public const byte FrameVersion = 0x00;
    public const byte CgiRequestType = 0x01;
    public const byte CgiCommandId = 0x0a;
    public const ushort NativeCgiCommandId = 0x0a01;

    public const string LiveStreamCgi = "livestream.cgi?streamid=10&substream=0&";
    public const string LoginStatusCgi = "get_status.cgi?name=admin&";

    public static byte[] BuildLiveStreamRequest(ushort sequence = 1)
    {
        return BuildGetRequest(LiveStreamCgi, sequence, leadingSlash: true);
    }

    public static byte[] BuildGetRequest(string cgiPath, ushort sequence)
    {
        return BuildGetRequest(cgiPath, sequence, leadingSlash: true);
    }

    public static byte[] BuildGetRequest(string cgiPath, ushort sequence, bool leadingSlash)
    {
        if (string.IsNullOrWhiteSpace(cgiPath))
            throw new ArgumentException("CGI path is required.", nameof(cgiPath));

        var payload = BuildHttpGetPayload(cgiPath, leadingSlash);
        if (payload.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(cgiPath), "CGI request is too large.");

        var frame = new byte[12 + payload.Length];
        frame[0] = FrameMarker;
        frame[1] = FrameVersion;
        WriteUInt16BigEndian(frame, 2, sequence);
        frame[4] = CgiRequestType;
        frame[5] = CgiCommandId;
        WriteUInt16BigEndian(frame, 6, (ushort)payload.Length);
        // Bytes 8-11 are reserved/zero in the VStarcam CGI-over-PPCS examples.
        Buffer.BlockCopy(payload, 0, frame, 12, payload.Length);
        return frame;
    }

    public static byte[] BuildHttpGetPayload(string cgiPath, bool leadingSlash)
    {
        if (string.IsNullOrWhiteSpace(cgiPath))
            throw new ArgumentException("CGI path is required.", nameof(cgiPath));

        var trimmed = cgiPath.Trim();
        var path = leadingSlash
            ? trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed
            : trimmed.TrimStart('/');

        return Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\n\r\n");
    }

    public static byte[] BuildRawCgiPathPayload(string cgiPath, bool nullTerminated = false)
    {
        if (string.IsNullOrWhiteSpace(cgiPath))
            throw new ArgumentException("CGI path is required.", nameof(cgiPath));

        var bytes = Encoding.ASCII.GetBytes(cgiPath.Trim().TrimStart('/'));
        if (!nullTerminated)
            return bytes;

        var payload = new byte[bytes.Length + 1];
        Buffer.BlockCopy(bytes, 0, payload, 0, bytes.Length);
        return payload;
    }

    public static byte[] BuildNativeCgiCommandHeader(int payloadLength)
    {
        if (payloadLength is < 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(payloadLength), "Native CGI command payload is too large.");

        var header = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0, 2), NativeCgiCommandId);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), (ushort)payloadLength);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), 0);
        return header;
    }

    public static byte[] BuildNativeLiveStreamCgiCommandBody(string loginUser, string loginPassword)
    {
        return BuildNativeCgiCommandBody(LiveStreamCgi, loginUser, loginPassword);
    }

    public static byte[] BuildNativeLoginStatusCgiCommandBody(string loginUser, string loginPassword)
    {
        return BuildNativeCgiCommandBody(LoginStatusCgi, loginUser, loginPassword);
    }

    public static byte[] BuildNativeCgiCommandBody(
        string cgiPath,
        string loginUser,
        string loginPassword)
    {
        if (string.IsNullOrWhiteSpace(cgiPath))
            throw new ArgumentException("CGI path is required.", nameof(cgiPath));

        var path = cgiPath.Trim().TrimStart('/');
        var user = string.IsNullOrWhiteSpace(loginUser) ? "admin" : loginUser.Trim();
        var password = string.IsNullOrWhiteSpace(loginPassword) ? "888888" : loginPassword.Trim();
        return Encoding.ASCII.GetBytes(
            $"GET /{path}loginuse={user}&loginpas={password}&user=admin&pwd=888888&");
    }

    public static bool TryReadHeader(
        ReadOnlySpan<byte> frame,
        out ushort sequence,
        out ushort payloadLength)
    {
        sequence = 0;
        payloadLength = 0;

        if (frame.Length < 12 ||
            frame[0] != FrameMarker ||
            frame[1] != FrameVersion ||
            frame[4] != CgiRequestType ||
            frame[5] != CgiCommandId)
        {
            return false;
        }

        sequence = ReadUInt16BigEndian(frame, 2);
        payloadLength = ReadUInt16BigEndian(frame, 6);
        return frame.Length >= 12 + payloadLength;
    }

    public static bool TryReadNativeCgiCommandHeader(
        ReadOnlySpan<byte> header,
        out ushort commandId,
        out ushort payloadLength)
    {
        commandId = 0;
        payloadLength = 0;

        if (header.Length < 8)
            return false;

        commandId = BinaryPrimitives.ReadUInt16LittleEndian(header[..2]);
        var reserved0 = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(2, 2));
        payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4, 2));
        var reserved1 = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6, 2));
        return reserved0 == 0 && reserved1 == 0;
    }

    private static void WriteUInt16BigEndian(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }

    private static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
    }
}
