using System.Runtime.InteropServices;
using System.Net;
using System.Net.Sockets;
using Android.Util;
using BodyCam.Services.Camera.A9.Vue990;

namespace BodyCam.A9PhoneProbe;

internal static class Vue990NativePacketOracle
{
    private const string LogTag = "A9PPCSOracle";

    public static string Run(bool includeSocketOracle, bool includeVariants, string? variantCase)
    {
        var lines = new List<string>
        {
            "Vue990 native packet oracle:",
        };

        Load(lines, "c++_shared");
        Load(lines, "vp_log");
        Load(lines, "OKSMARTJIAMI");
        Load(lines, "OKSMARTPPCS");

        TryCreate(lines, "create_Hello", CreateHello, "F1000000");
        TryCreate(lines, "create_RlyHello", CreateRlyHello, "F1700000");
        TryCreate(lines, "create_SvrReq", CreateSvrReq, "F2100000");
        TryCreateHlp2pSessionPackets(lines);
        var isolatedVariant = !string.IsNullOrWhiteSpace(variantCase);
        if (!isolatedVariant)
        {
            TryWriteTcpRlyReq(lines);
            TryWriteTcpRsLgn(lines);
        }

        if (includeVariants)
            TryWriteVariants(lines, variantCase);

        if (includeSocketOracle)
        {
            if (!isolatedVariant)
            {
                TryTcpSendHello(lines);
                TryTcpSendRlyReq(lines);
                TryTcpSendRsLgn(lines);
            }

            if (includeVariants)
                TryTcpSendVariants(lines, variantCase);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void TryCreate(
        ICollection<string> lines,
        string name,
        NativePacketCreator creator,
        string expectedPrefix)
    {
        var buffer = new byte[256];
        try
        {
            var returned = creator(buffer);
            var nonZeroLength = LastNonZeroIndex(buffer) + 1;
            var candidateLength = returned is > 0 and <= 256 ? returned : nonZeroLength;
            if (candidateLength <= 0)
                candidateLength = 4;

            var prefixLength = Math.Min(candidateLength, 64);
            var hex = Convert.ToHexString(buffer.AsSpan(0, prefixLength));
            lines.Add(
                $"- {name}: return={returned} nonZeroLength={nonZeroLength} " +
                $"candidateLength={candidateLength} hex={hex} expectedPrefix={expectedPrefix} " +
                $"matchesExpected={hex.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)}");
        }
        catch (Exception ex)
        {
            lines.Add($"- {name}: exception={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryCreateHlp2pSessionPackets(ICollection<string> lines)
    {
        lines.Add("- HLP2P session packet creators:");

        var ids = new (string Name, byte[] Bytes)[]
        {
            ("vuid-structured", A9Vue990Hlp2pPacketBuilder.BuildCompactP2pId("BK0025644WBPD")),
            ("client-structured", A9Vue990Hlp2pPacketBuilder.BuildCompactP2pId("BKGD00000100FMQLN")),
        };
        var addresses = new (string Name, byte[] Bytes)[]
        {
            ("loopback-65527", BuildSockaddrCs2Loopback()),
            ("phone-192-168-168-101-65529", BuildSockaddrCs2(192, 168, 168, 101, 65529)),
        };

        foreach (var id in ids)
        {
            TryCreateP2pIdPacket(lines, $"create_LstReq[{id.Name}]", CreateLstReq, id.Bytes);
            TryCreateP2pIdPacket(lines, $"create_PunchPkt[{id.Name}]", CreatePunchPkt, id.Bytes);
            TryCreateP2pIdPacket(lines, $"create_P2pRdy[{id.Name}]", CreateP2pRdy, id.Bytes);

            foreach (var address in addresses)
            {
                TryCreateP2pReq(
                    lines,
                    $"create_P2pReq[{id.Name},{address.Name},af2]",
                    id.Bytes,
                    address.Bytes,
                    2);
            }
        }
    }

    private static void TryCreateP2pIdPacket(
        ICollection<string> lines,
        string name,
        NativeP2pIdPacketCreator creator,
        byte[] p2pId)
    {
        var output = new byte[256];
        try
        {
            var returned = creator(output, p2pId);
            lines.Add(BuildOutputLine(name, returned, output, previewLength: 32));
        }
        catch (Exception ex)
        {
            lines.Add($"- {name}: exception={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryCreateP2pReq(
        ICollection<string> lines,
        string name,
        byte[] p2pId,
        byte[] reverseAddress,
        int addressFamily)
    {
        var output = new byte[256];
        try
        {
            var returned = CreateP2pReq(output, p2pId, reverseAddress, addressFamily);
            lines.Add(BuildOutputLine(name, returned, output, previewLength: 48));
        }
        catch (Exception ex)
        {
            lines.Add($"- {name}: exception={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int LastNonZeroIndex(ReadOnlySpan<byte> bytes)
    {
        for (var i = bytes.Length - 1; i >= 0; i--)
        {
            if (bytes[i] != 0)
                return i;
        }

        return -1;
    }

    private static void Load(ICollection<string> lines, string name)
    {
        try
        {
            Java.Lang.JavaSystem.LoadLibrary(name);
            lines.Add($"- load {name}=ok");
            Log.Info(LogTag, $"load {name}=ok");
        }
        catch (Exception ex)
        {
            lines.Add($"- load {name}={ex.GetType().Name}: {ex.Message}");
            Log.Info(LogTag, $"load {name}={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryTcpSendHello(ICollection<string> lines)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            client.Connect(IPAddress.Loopback, port);
            using var accepted = listener.AcceptSocket();
            accepted.ReceiveTimeout = 1500;

            var fd = client.Handle.ToInt32();
            var arg = NullTerminatedAscii("BKGD00000100FMQLN");
            var scratch = new byte[512];
            var returned = TcpSendHello(arg, fd, 0, scratch);

            var buffer = new byte[512];
            var received = accepted.Poll(1_500_000, SelectMode.SelectRead)
                ? accepted.Receive(buffer)
                : 0;
            var hex = received > 0
                ? Convert.ToHexString(buffer.AsSpan(0, received))
                : string.Empty;

            lines.Add(
                $"- TCPSend_Hello loopback: return={returned} fd={fd} received={received} " +
                $"hex={hex} scratchPrefix={Convert.ToHexString(scratch.AsSpan(0, 32))}");
        }
        catch (Exception ex)
        {
            lines.Add($"- TCPSend_Hello loopback: exception={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryTcpSendRlyReq(ICollection<string> lines)
    {
        try
        {
            using var loopback = LoopbackSocketPair.Create();
            var clientId = NullTerminatedAscii("BKGD00000100FMQLN");
            var vuid = NullTerminatedAscii("BK0025644WBPD");
            var relayName = NullTerminatedAscii("BKGD");
            var sessionKey = new byte[16];
            var address = BuildSockaddrCs2Loopback();
            var scratch = new byte[512];

            var returned = TcpSendRlyReq(
                clientId,
                loopback.SocketFd,
                vuid,
                (uint)(vuid.Length - 1),
                relayName,
                0,
                sessionKey,
                0,
                address,
                0,
                scratch);

            lines.Add(BuildSocketLine("TCPSend_TCPRlyReq", returned, loopback, scratch));
        }
        catch (Exception ex)
        {
            lines.Add($"- TCPSend_TCPRlyReq loopback: exception={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryTcpSendRsLgn(ICollection<string> lines)
    {
        try
        {
            using var loopback = LoopbackSocketPair.Create();
            var clientId = NullTerminatedAscii("BKGD00000100FMQLN");
            var vuid = NullTerminatedAscii("BK0025644WBPD");
            var relayName = NullTerminatedAscii("BKGD");
            var address = BuildSockaddrCs2Loopback();
            var scratch = new byte[512];

            var returned = TcpSendRsLgn(
                clientId,
                loopback.SocketFd,
                vuid,
                (uint)(vuid.Length - 1),
                relayName,
                0,
                0,
                0,
                0,
                0,
                address,
                0,
                scratch);

            lines.Add(BuildSocketLine("TCPSend_TCPRSLgn", returned, loopback, scratch));
        }
        catch (Exception ex)
        {
            lines.Add($"- TCPSend_TCPRSLgn loopback: exception={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryWriteTcpRlyReq(ICollection<string> lines)
    {
        var output = new byte[512];
        try
        {
            var clientId = NullTerminatedAscii("BKGD00000100FMQLN");
            var vuid = NullTerminatedAscii("BK0025644WBPD");
            var sessionKey = new byte[16];
            var address = BuildSockaddrCs2Loopback();

            var returned = WriteTcpRlyReq(
                output,
                clientId,
                (uint)(clientId.Length - 1),
                vuid,
                0,
                sessionKey,
                0,
                address);
            lines.Add(BuildOutputLine("Write_TCPRlyReq", returned, output));
        }
        catch (Exception ex)
        {
            lines.Add($"- Write_TCPRlyReq: exception={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryWriteTcpRsLgn(ICollection<string> lines)
    {
        var output = new byte[512];
        try
        {
            var clientId = NullTerminatedAscii("BKGD00000100FMQLN");
            var vuid = NullTerminatedAscii("BK0025644WBPD");
            var address = BuildSockaddrCs2Loopback();

            var returned = WriteTcpRsLgn(
                output,
                clientId,
                (uint)(clientId.Length - 1),
                vuid,
                0,
                0,
                0,
                0,
                0,
                address);
            lines.Add(BuildOutputLine("Write_TCPRSLgn", returned, output));
        }
        catch (Exception ex)
        {
            lines.Add($"- Write_TCPRSLgn: exception={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryWriteVariants(ICollection<string> lines, string? variantCase)
    {
        if (!ShouldRunVariantGroup(variantCase, "write"))
            return;

        lines.Add("- Write variant oracle:");

        var baseClientId = NullTerminatedAscii("BKGD00000100FMQLN");
        var baseVuid = NullTerminatedAscii("BK0025644WBPD");
        var baseRelayName = NullTerminatedAscii("BKGD");
        var zeroKey = new byte[16];
        var nonZeroKey = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();
        var loopback = BuildSockaddrCs2Loopback();
        var otherAddress = BuildSockaddrCs2(192, 168, 168, 101, 65527);

        if (ShouldRunVariant(variantCase, "write-rlyreq-baseline"))
            WriteRlyReqVariant(lines, "baseline", baseClientId, baseVuid, baseRelayName, 0, zeroKey, 0, loopback);
        if (ShouldRunVariant(variantCase, "write-rlyreq-client-id"))
            WriteRlyReqVariant(lines, "client-id", NullTerminatedAscii("ABCDEFGH12345678"), baseVuid, baseRelayName, 0, zeroKey, 0, loopback);
        if (ShouldRunVariant(variantCase, "write-rlyreq-vuid"))
            WriteRlyReqVariant(lines, "vuid", baseClientId, NullTerminatedAscii("XY1234567890"), baseRelayName, 0, zeroKey, 0, loopback);
        if (ShouldRunVariant(variantCase, "write-rlyreq-relay-token"))
            WriteRlyReqVariant(lines, "relay-token", baseClientId, baseVuid, NullTerminatedAscii("9047F8F88"), 0, zeroKey, 0, loopback);
        if (ShouldRunVariant(variantCase, "write-rlyreq-mode-1"))
            WriteRlyReqVariant(lines, "mode-1", baseClientId, baseVuid, baseRelayName, 1, zeroKey, 0, loopback);
        if (ShouldRunVariant(variantCase, "write-rlyreq-key-1-16"))
            WriteRlyReqVariant(lines, "key-1-16", baseClientId, baseVuid, baseRelayName, 0, nonZeroKey, 0, loopback);
        if (ShouldRunVariant(variantCase, "write-rlyreq-flag-1"))
            WriteRlyReqVariant(lines, "flag-1", baseClientId, baseVuid, baseRelayName, 0, zeroKey, 1, loopback);
        if (ShouldRunVariant(variantCase, "write-rlyreq-address-phone"))
            WriteRlyReqVariant(lines, "address-phone", baseClientId, baseVuid, baseRelayName, 0, zeroKey, 0, otherAddress);

        if (ShouldRunVariant(variantCase, "write-rslgn-baseline"))
            WriteRsLgnVariant(lines, "baseline", baseClientId, baseVuid, 0, 0, 0, 0, 0, loopback);
        if (ShouldRunVariant(variantCase, "write-rslgn-client-id"))
            WriteRsLgnVariant(lines, "client-id", NullTerminatedAscii("ABCDEFGH12345678"), baseVuid, 0, 0, 0, 0, 0, loopback);
        if (ShouldRunVariant(variantCase, "write-rslgn-vuid"))
            WriteRsLgnVariant(lines, "vuid", baseClientId, NullTerminatedAscii("XY1234567890"), 0, 0, 0, 0, 0, loopback);
        if (ShouldRunVariant(variantCase, "write-rslgn-t1-1"))
            WriteRsLgnVariant(lines, "t1-1", baseClientId, baseVuid, 1, 0, 0, 0, 0, loopback);
        if (ShouldRunVariant(variantCase, "write-rslgn-t2-2"))
            WriteRsLgnVariant(lines, "t2-2", baseClientId, baseVuid, 0, 2, 0, 0, 0, loopback);
        if (ShouldRunVariant(variantCase, "write-rslgn-t3-3"))
            WriteRsLgnVariant(lines, "t3-3", baseClientId, baseVuid, 0, 0, 3, 0, 0, loopback);
        if (ShouldRunVariant(variantCase, "write-rslgn-t4-4"))
            WriteRsLgnVariant(lines, "t4-4", baseClientId, baseVuid, 0, 0, 0, 4, 0, loopback);
        if (ShouldRunVariant(variantCase, "write-rslgn-value-5"))
            WriteRsLgnVariant(lines, "value-5", baseClientId, baseVuid, 0, 0, 0, 0, 5, loopback);
        if (ShouldRunVariant(variantCase, "write-rslgn-address-phone"))
            WriteRsLgnVariant(lines, "address-phone", baseClientId, baseVuid, 0, 0, 0, 0, 0, otherAddress);
    }

    private static void TryTcpSendVariants(ICollection<string> lines, string? variantCase)
    {
        if (!ShouldRunVariantGroup(variantCase, "tcpsend"))
            return;

        lines.Add("- TCPSend variant oracle:");

        var baseClientId = NullTerminatedAscii("BKGD00000100FMQLN");
        var baseVuid = NullTerminatedAscii("BK0025644WBPD");
        var baseRelayName = NullTerminatedAscii("BKGD");
        var liveRelayToken = NullTerminatedAscii("9047F8F88");
        var relayModeToken = NullTerminatedAscii("a+a+a");
        var zeroKey = new byte[16];
        var nonZeroKey = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();
        var loopback = BuildSockaddrCs2Loopback();
        var phoneAddress = BuildSockaddrCs2(192, 168, 168, 101, 65527);

        if (ShouldRunVariant(variantCase, "tcpsend-rlyreq-baseline"))
            TcpSendRlyReqVariant(lines, "baseline", baseClientId, baseVuid, baseRelayName, 0, zeroKey, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rlyreq-client-id"))
            TcpSendRlyReqVariant(lines, "client-id", NullTerminatedAscii("ABCDEFGH12345678"), baseVuid, baseRelayName, 0, zeroKey, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rlyreq-vuid"))
            TcpSendRlyReqVariant(lines, "vuid", baseClientId, NullTerminatedAscii("XY1234567890"), baseRelayName, 0, zeroKey, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rlyreq-relay-token"))
            TcpSendRlyReqVariant(lines, "relay-token", baseClientId, baseVuid, liveRelayToken, 0, zeroKey, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rlyreq-relay-mode-token"))
            TcpSendRlyReqVariant(lines, "relay-mode-token", baseClientId, baseVuid, relayModeToken, 0, zeroKey, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rlyreq-mode-1"))
            TcpSendRlyReqVariant(lines, "mode-1", baseClientId, baseVuid, baseRelayName, 1, zeroKey, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rlyreq-key-1-16"))
            TcpSendRlyReqVariant(lines, "key-1-16", baseClientId, baseVuid, baseRelayName, 0, nonZeroKey, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rlyreq-flag-1"))
            TcpSendRlyReqVariant(lines, "flag-1", baseClientId, baseVuid, baseRelayName, 0, zeroKey, 1, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rlyreq-address-phone"))
            TcpSendRlyReqVariant(lines, "address-phone", baseClientId, baseVuid, baseRelayName, 0, zeroKey, 0, phoneAddress, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rlyreq-value-1"))
            TcpSendRlyReqVariant(lines, "value-1", baseClientId, baseVuid, baseRelayName, 0, zeroKey, 0, loopback, 1);

        if (ShouldRunVariant(variantCase, "tcpsend-rslgn-baseline"))
            TcpSendRsLgnVariant(lines, "baseline", baseClientId, baseVuid, baseRelayName, 0, 0, 0, 0, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rslgn-client-id"))
            TcpSendRsLgnVariant(lines, "client-id", NullTerminatedAscii("ABCDEFGH12345678"), baseVuid, baseRelayName, 0, 0, 0, 0, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rslgn-vuid"))
            TcpSendRsLgnVariant(lines, "vuid", baseClientId, NullTerminatedAscii("XY1234567890"), baseRelayName, 0, 0, 0, 0, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rslgn-relay-token"))
            TcpSendRsLgnVariant(lines, "relay-token", baseClientId, baseVuid, liveRelayToken, 0, 0, 0, 0, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rslgn-relay-mode-token"))
            TcpSendRsLgnVariant(lines, "relay-mode-token", baseClientId, baseVuid, relayModeToken, 0, 0, 0, 0, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rslgn-t1-1"))
            TcpSendRsLgnVariant(lines, "t1-1", baseClientId, baseVuid, baseRelayName, 1, 0, 0, 0, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rslgn-t2-2"))
            TcpSendRsLgnVariant(lines, "t2-2", baseClientId, baseVuid, baseRelayName, 0, 2, 0, 0, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rslgn-t3-3"))
            TcpSendRsLgnVariant(lines, "t3-3", baseClientId, baseVuid, baseRelayName, 0, 0, 3, 0, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rslgn-t4-4"))
            TcpSendRsLgnVariant(lines, "t4-4", baseClientId, baseVuid, baseRelayName, 0, 0, 0, 4, 0, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rslgn-value-5"))
            TcpSendRsLgnVariant(lines, "value-5", baseClientId, baseVuid, baseRelayName, 0, 0, 0, 0, 5, loopback, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rslgn-address-phone"))
            TcpSendRsLgnVariant(lines, "address-phone", baseClientId, baseVuid, baseRelayName, 0, 0, 0, 0, 0, phoneAddress, 0);
        if (ShouldRunVariant(variantCase, "tcpsend-rslgn-final-value-6"))
            TcpSendRsLgnVariant(lines, "final-value-6", baseClientId, baseVuid, baseRelayName, 0, 0, 0, 0, 0, loopback, 6);
    }

    private static bool ShouldRunVariantGroup(string? variantCase, string group)
    {
        return string.IsNullOrWhiteSpace(variantCase) ||
               variantCase.Equals(group, StringComparison.OrdinalIgnoreCase) ||
               variantCase.StartsWith(group + "-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldRunVariant(string? variantCase, string name)
    {
        return string.IsNullOrWhiteSpace(variantCase) ||
               variantCase.Equals("all", StringComparison.OrdinalIgnoreCase) ||
               variantCase.Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    private static void TcpSendRlyReqVariant(
        ICollection<string> lines,
        string name,
        byte[] clientId,
        byte[] vuid,
        byte[] relayName,
        byte mode,
        byte[] sessionKey,
        byte flag,
        byte[] address,
        uint value)
    {
        try
        {
            using var loopback = LoopbackSocketPair.Create();
            var scratch = new byte[512];
            var returned = TcpSendRlyReq(
                clientId,
                loopback.SocketFd,
                vuid,
                (uint)(vuid.Length - 1),
                relayName,
                mode,
                sessionKey,
                flag,
                address,
                value,
                scratch);
            lines.Add(BuildSocketLine($"TCPSend_TCPRlyReq[{name}]", returned, loopback, scratch));
        }
        catch (Exception ex)
        {
            lines.Add($"- TCPSend_TCPRlyReq[{name}] loopback: exception={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TcpSendRsLgnVariant(
        ICollection<string> lines,
        string name,
        byte[] clientId,
        byte[] vuid,
        byte[] relayName,
        ushort value1,
        ushort value2,
        ushort value3,
        ushort value4,
        uint value5,
        byte[] address,
        uint value6)
    {
        try
        {
            using var loopback = LoopbackSocketPair.Create();
            var scratch = new byte[512];
            var returned = TcpSendRsLgn(
                clientId,
                loopback.SocketFd,
                vuid,
                (uint)(vuid.Length - 1),
                relayName,
                value1,
                value2,
                value3,
                value4,
                value5,
                address,
                value6,
                scratch);
            lines.Add(BuildSocketLine($"TCPSend_TCPRSLgn[{name}]", returned, loopback, scratch));
        }
        catch (Exception ex)
        {
            lines.Add($"- TCPSend_TCPRSLgn[{name}] loopback: exception={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void WriteRlyReqVariant(
        ICollection<string> lines,
        string name,
        byte[] clientId,
        byte[] vuid,
        byte[] relayName,
        byte mode,
        byte[] sessionKey,
        byte flag,
        byte[] address)
    {
        var output = new byte[512];
        try
        {
            var returned = WriteTcpRlyReq(
                output,
                clientId,
                (uint)(clientId.Length - 1),
                vuid,
                mode,
                sessionKey,
                flag,
                address);
            lines.Add(BuildOutputLine($"Write_TCPRlyReq[{name}]", returned, output));
        }
        catch (Exception ex)
        {
            lines.Add($"- Write_TCPRlyReq[{name}]: exception={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void WriteRsLgnVariant(
        ICollection<string> lines,
        string name,
        byte[] clientId,
        byte[] vuid,
        ushort value1,
        ushort value2,
        ushort value3,
        ushort value4,
        uint value5,
        byte[] address)
    {
        var output = new byte[512];
        try
        {
            var returned = WriteTcpRsLgn(
                output,
                clientId,
                (uint)(clientId.Length - 1),
                vuid,
                value1,
                value2,
                value3,
                value4,
                value5,
                address);
            lines.Add(BuildOutputLine($"Write_TCPRSLgn[{name}]", returned, output));
        }
        catch (Exception ex)
        {
            lines.Add($"- Write_TCPRSLgn[{name}]: exception={ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string BuildOutputLine(string name, int returned, byte[] output, int? previewLength = null)
    {
        var nonZeroLength = LastNonZeroIndex(output) + 1;
        var candidateLength = returned is > 0 and <= 512 ? returned : nonZeroLength;
        if (candidateLength <= 0)
            candidateLength = Math.Min(output.Length, 64);

        var previewBytes = Math.Min(
            output.Length,
            Math.Max(candidateLength, Math.Max(nonZeroLength, previewLength ?? 0)));

        return $"- {name}: return={returned} nonZeroLength={nonZeroLength} " +
               $"candidateLength={candidateLength} hex={Convert.ToHexString(output.AsSpan(0, candidateLength))} " +
               $"previewLength={previewBytes} preview={Convert.ToHexString(output.AsSpan(0, previewBytes))}";
    }

    private static string BuildSocketLine(string name, int returned, LoopbackSocketPair loopback, byte[] scratch)
    {
        var received = loopback.Receive();
        var hex = received.Length > 0 ? Convert.ToHexString(received) : string.Empty;
        return $"- {name} loopback: return={returned} fd={loopback.SocketFd} received={received.Length} " +
               $"hex={hex} scratchPrefix={Convert.ToHexString(scratch.AsSpan(0, 32))}";
    }

    private static byte[] BuildSockaddrCs2Loopback()
    {
        return BuildSockaddrCs2(127, 0, 0, 1, 65527);
    }

    private static byte[] BuildSockaddrCs2(byte a, byte b, byte c, byte d, ushort port)
    {
        var bytes = new byte[32];
        bytes[0] = 2;
        bytes[1] = 0;
        bytes[2] = (byte)(port >> 8);
        bytes[3] = (byte)port;
        bytes[4] = a;
        bytes[5] = b;
        bytes[6] = c;
        bytes[7] = d;
        return bytes;
    }

    private static byte[] NullTerminatedAscii(string value)
    {
        var bytes = new byte[value.Length + 1];
        for (var i = 0; i < value.Length; i++)
            bytes[i] = (byte)value[i];
        return bytes;
    }

    private sealed class LoopbackSocketPair : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Socket _client;
        private readonly Socket _accepted;

        private LoopbackSocketPair(TcpListener listener, Socket client, Socket accepted)
        {
            _listener = listener;
            _client = client;
            _accepted = accepted;
            SocketFd = client.Handle.ToInt32();
            _accepted.ReceiveTimeout = 1500;
        }

        public int SocketFd { get; }

        public static LoopbackSocketPair Create()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            client.Connect(IPAddress.Loopback, port);
            var accepted = listener.AcceptSocket();
            return new LoopbackSocketPair(listener, client, accepted);
        }

        public byte[] Receive()
        {
            var buffer = new byte[1024];
            var received = _accepted.Poll(1_500_000, SelectMode.SelectRead)
                ? _accepted.Receive(buffer)
                : 0;
            return received > 0 ? buffer.AsSpan(0, received).ToArray() : [];
        }

        public void Dispose()
        {
            _accepted.Dispose();
            _client.Dispose();
            _listener.Stop();
        }
    }

    private delegate int NativePacketCreator(byte[] output);

    private delegate int NativeP2pIdPacketCreator(byte[] output, byte[] p2pId);

    [DllImport("OKSMARTPPCS", EntryPoint = "create_Hello")]
    private static extern int CreateHello(byte[] output);

    [DllImport("OKSMARTPPCS", EntryPoint = "create_RlyHello")]
    private static extern int CreateRlyHello(byte[] output);

    [DllImport("OKSMARTPPCS", EntryPoint = "create_SvrReq")]
    private static extern int CreateSvrReq(byte[] output);

    [DllImport("OKSMARTPPCS", EntryPoint = "create_LstReq")]
    private static extern int CreateLstReq(byte[] output, byte[] p2pId);

    [DllImport("OKSMARTPPCS", EntryPoint = "create_PunchPkt")]
    private static extern int CreatePunchPkt(byte[] output, byte[] p2pId);

    [DllImport("OKSMARTPPCS", EntryPoint = "create_P2pRdy")]
    private static extern int CreateP2pRdy(byte[] output, byte[] p2pId);

    [DllImport("OKSMARTPPCS", EntryPoint = "create_P2pReq")]
    private static extern int CreateP2pReq(byte[] output, byte[] p2pId, byte[] reverseAddress, int addressFamily);

    [DllImport("OKSMARTPPCS", EntryPoint = "_Z31cs2p2p_PPPP_Proto_TCPSend_HelloPKcijPc")]
    private static extern int TcpSendHello(byte[] value, int socketFd, uint arg, byte[] scratch);

    [DllImport("OKSMARTPPCS", EntryPoint = "_Z35cs2p2p_PPPP_Proto_TCPSend_TCPRlyReqPKciS0_jS0_cPKhcPK12sockaddr_cs2jPc")]
    private static extern int TcpSendRlyReq(
        byte[] clientId,
        int socketFd,
        byte[] vuid,
        uint vuidLength,
        byte[] relayName,
        byte mode,
        byte[] sessionKey,
        byte flag,
        byte[] address,
        uint value,
        byte[] scratch);

    [DllImport("OKSMARTPPCS", EntryPoint = "_Z34cs2p2p_PPPP_Proto_TCPSend_TCPRSLgnPKciS0_jS0_ttttjPK12sockaddr_cs2jPc")]
    private static extern int TcpSendRsLgn(
        byte[] clientId,
        int socketFd,
        byte[] vuid,
        uint vuidLength,
        byte[] relayName,
        ushort value1,
        ushort value2,
        ushort value3,
        ushort value4,
        uint value5,
        byte[] address,
        uint value6,
        byte[] scratch);

    [DllImport("OKSMARTPPCS", EntryPoint = "_Z33cs2p2p_PPPP_Proto_Write_TCPRlyReqP19st_cs2p2p_TCPRlyReqPKcjS2_cPKhcPK12sockaddr_cs2")]
    private static extern int WriteTcpRlyReq(
        byte[] output,
        byte[] clientId,
        uint clientIdLength,
        byte[] vuid,
        byte mode,
        byte[] sessionKey,
        byte flag,
        byte[] address);

    [DllImport("OKSMARTPPCS", EntryPoint = "_Z32cs2p2p_PPPP_Proto_Write_TCPRSLgnP18st_cs2p2p_TCPRSLgnPKcjS2_ttttjPK12sockaddr_cs2")]
    private static extern int WriteTcpRsLgn(
        byte[] output,
        byte[] clientId,
        uint clientIdLength,
        byte[] vuid,
        ushort value1,
        ushort value2,
        ushort value3,
        ushort value4,
        uint value5,
        byte[] address);
}
