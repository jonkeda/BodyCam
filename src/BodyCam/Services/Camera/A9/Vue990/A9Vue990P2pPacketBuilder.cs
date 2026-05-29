namespace BodyCam.Services.Camera.A9.Vue990;

public static class A9Vue990P2pPacketBuilder
{
    public const ushort TcpHello = 0xf100;
    public const ushort LanSearch = 0xf130;
    public const ushort RelayHello = 0xf170;
    public const ushort RelayHelloAck = 0xf171;
    public const ushort ServerRequest = 0xf210;

    private const string TcpSendRlyReqOracleHex =
        "00386800FF4A81098AFBD7FC10B9AC58F88BFB0E502C933ACCA49F4373E35E905B7F5F9FAE89E783C6D3C825A82544AD780282477960DD03ED1BADDC35D6B2B3";

    private const string TcpSendRsLgnOracleHex =
        "003C6800EC294C34003E3364A0270856599C9319361CA5159328E0CE2F1BC4DBE223080EA7179378EAFEFE31E23F984E12B291B1BE4BA15DCB7C604C98DEA2405298FE7D";

    public static ReadOnlySpan<byte> TcpSendHelloOracle =>
    [
        0x00, 0x04, 0x68, 0x00, 0x67, 0xc6, 0xfe, 0x15, 0x8f, 0x32, 0xc2, 0x84,
    ];

    public static ReadOnlySpan<byte> TcpSendHelloOraclePrevious =>
    [
        0x00, 0x04, 0x68, 0x00, 0x73, 0x51, 0x67, 0x3d, 0x7c, 0x58, 0x97, 0xf9,
    ];

    public static byte[] BuildHeader(ushort command, ushort length = 0)
    {
        return
        [
            (byte)(command >> 8),
            (byte)command,
            (byte)(length >> 8),
            (byte)length,
        ];
    }

    public static byte[] BuildTcpHello()
    {
        return BuildHeader(TcpHello);
    }

    public static byte[] BuildRelayHello()
    {
        return BuildHeader(RelayHello);
    }

    public static byte[] BuildServerRequest()
    {
        return BuildHeader(ServerRequest);
    }

    public static byte[] BuildTcpSendHelloOracle()
    {
        return TcpSendHelloOracle.ToArray();
    }

    public static byte[] BuildTcpSendHelloOraclePrevious()
    {
        return TcpSendHelloOraclePrevious.ToArray();
    }

    public static byte[] BuildTcpSendRlyReqOracle()
    {
        return Convert.FromHexString(TcpSendRlyReqOracleHex);
    }

    public static byte[] BuildTcpSendRsLgnOracle()
    {
        return Convert.FromHexString(TcpSendRsLgnOracleHex);
    }

    public static byte[] BuildSequence(params byte[][] packets)
    {
        var totalLength = packets.Sum(packet => packet.Length);
        var bytes = new byte[totalLength];
        var offset = 0;

        foreach (var packet in packets)
        {
            packet.CopyTo(bytes.AsSpan(offset));
            offset += packet.Length;
        }

        return bytes;
    }
}
