using System.Net;

namespace BodyCam.Services.Camera.A9.Vue990;

public sealed class A9Vue990ConnectByServerState
{
    public const int NativeConnectType = 0x3f;

    public const int NativeP2pType = 1;

    public required string ClientId { get; init; }

    public required string Vuid { get; init; }

    public required string User { get; init; }

    public required string Password { get; init; }

    public int ConnectType { get; init; } = NativeConnectType;

    public int P2pType { get; init; } = NativeP2pType;

    public required IPEndPoint LocalEndpoint { get; init; }

    public IReadOnlyList<A9Vue990DasTokenSnapshot> Tokens { get; init; } = [];

    public required A9Vue990DasTokenSnapshot OpaqueToken { get; init; }

    public required A9Vue990DasTokenSnapshot ModeToken { get; init; }

    public required A9Vue990DasTokenSnapshot RelayHostToken { get; init; }

    public required A9Vue990DasTokenSnapshot RelayNameToken { get; init; }

    public required A9Vue990DasTokenSnapshot SelectorToken { get; init; }

    public IReadOnlyList<string> ModeParts { get; init; } = [];

    public IReadOnlyList<string> RelayHosts { get; init; } = [];

    public byte[] ClientP2pId { get; init; } = [];

    public byte[] VuidP2pId { get; init; } = [];

    public string CandidateDasConnectText =>
        $"das,{P2pType},{ModeToken.EscapedAscii},{RelayHostToken.EscapedAscii},{RelayNameToken.EscapedAscii}";

    public string Selector => SelectorToken.EscapedAscii;

    public static bool TryCreate(
        A9Vue990DasServerParameter das,
        string clientId,
        string vuid,
        IPEndPoint localEndpoint,
        out A9Vue990ConnectByServerState? state,
        out string? error,
        string user = "admin",
        string password = "888888",
        int connectType = NativeConnectType,
        int p2pType = NativeP2pType)
    {
        state = null;
        error = null;

        if (das.DecodedPayload.ConnectDescriptor is not { } descriptor ||
            !descriptor.HasNativeConnectByServerShape)
        {
            error = "DAS parameter does not contain the native ConnectByServer token shape.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            error = "Client id is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(vuid))
        {
            error = "VUID is required.";
            return false;
        }

        if (localEndpoint.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            error = "Managed LAN-hole opener requires an IPv4 local endpoint.";
            return false;
        }

        if (localEndpoint.Port is <= 0 or > 65535)
        {
            error = "Managed LAN-hole opener requires a concrete local UDP port.";
            return false;
        }

        if (descriptor.OpaqueToken is null ||
            descriptor.ModeToken is null ||
            descriptor.RelayHostToken is null ||
            descriptor.RelayNameToken is null ||
            descriptor.SelectorToken is null)
        {
            error = "DAS descriptor is missing one or more required native tokens.";
            return false;
        }

        var tokens = descriptor.Tokens
            .Select(A9Vue990DasTokenSnapshot.FromToken)
            .ToArray();

        state = new A9Vue990ConnectByServerState
        {
            ClientId = clientId.Trim(),
            Vuid = vuid.Trim(),
            User = string.IsNullOrWhiteSpace(user) ? "admin" : user.Trim(),
            Password = string.IsNullOrWhiteSpace(password) ? "888888" : password,
            ConnectType = connectType,
            P2pType = p2pType,
            LocalEndpoint = localEndpoint,
            Tokens = tokens,
            OpaqueToken = A9Vue990DasTokenSnapshot.FromToken(descriptor.OpaqueToken),
            ModeToken = A9Vue990DasTokenSnapshot.FromToken(descriptor.ModeToken),
            RelayHostToken = A9Vue990DasTokenSnapshot.FromToken(descriptor.RelayHostToken),
            RelayNameToken = A9Vue990DasTokenSnapshot.FromToken(descriptor.RelayNameToken),
            SelectorToken = A9Vue990DasTokenSnapshot.FromToken(descriptor.SelectorToken),
            ModeParts = descriptor.ModeParts.ToArray(),
            RelayHosts = descriptor.RelayHosts.ToArray(),
            ClientP2pId = A9Vue990Hlp2pPacketBuilder.BuildCompactP2pId(clientId),
            VuidP2pId = A9Vue990Hlp2pPacketBuilder.BuildCompactP2pId(vuid),
        };
        return true;
    }

    public IReadOnlyList<A9Vue990NamedPacket> BuildPrimaryLanHoleOpenPackets()
    {
        return
        [
            new A9Vue990NamedPacket(
                "vuid-list-request",
                A9Vue990Hlp2pPacketBuilder.ListRequest,
                A9Vue990Hlp2pPacketBuilder.BuildListRequest(VuidP2pId)),
            new A9Vue990NamedPacket(
                "vuid-punch-packet",
                A9Vue990Hlp2pPacketBuilder.PunchPacket,
                A9Vue990Hlp2pPacketBuilder.BuildPunchPacket(VuidP2pId)),
            new A9Vue990NamedPacket(
                "vuid-p2p-ready",
                A9Vue990Hlp2pPacketBuilder.P2pReady,
                A9Vue990Hlp2pPacketBuilder.BuildP2pReady(VuidP2pId)),
            new A9Vue990NamedPacket(
                "vuid-p2p-request4",
                A9Vue990Hlp2pPacketBuilder.P2pRequest,
                A9Vue990Hlp2pPacketBuilder.BuildP2pRequest4(
                    VuidP2pId,
                    LocalEndpoint.Address,
                    (ushort)LocalEndpoint.Port)),
        ];
    }

    public IReadOnlyList<A9Vue990NamedPacket> BuildClientIdLanHoleOpenPackets()
    {
        return
        [
            new A9Vue990NamedPacket(
                "client-list-request",
                A9Vue990Hlp2pPacketBuilder.ListRequest,
                A9Vue990Hlp2pPacketBuilder.BuildListRequest(ClientP2pId)),
            new A9Vue990NamedPacket(
                "client-punch-packet",
                A9Vue990Hlp2pPacketBuilder.PunchPacket,
                A9Vue990Hlp2pPacketBuilder.BuildPunchPacket(ClientP2pId)),
            new A9Vue990NamedPacket(
                "client-p2p-ready",
                A9Vue990Hlp2pPacketBuilder.P2pReady,
                A9Vue990Hlp2pPacketBuilder.BuildP2pReady(ClientP2pId)),
            new A9Vue990NamedPacket(
                "client-p2p-request4",
                A9Vue990Hlp2pPacketBuilder.P2pRequest,
                A9Vue990Hlp2pPacketBuilder.BuildP2pRequest4(
                    ClientP2pId,
                    LocalEndpoint.Address,
                    (ushort)LocalEndpoint.Port)),
        ];
    }

    public IReadOnlyList<A9Vue990NamedPacket> BuildNativeClientSessionSetupPackets()
    {
        return
        [
            new A9Vue990NamedPacket(
                "native-client-list-request",
                A9Vue990Hlp2pPacketBuilder.ListRequest,
                A9Vue990Hlp2pPacketBuilder.BuildListRequest(ClientP2pId)),
            new A9Vue990NamedPacket(
                "native-client-p2p-request4",
                A9Vue990Hlp2pPacketBuilder.P2pRequest,
                A9Vue990Hlp2pPacketBuilder.BuildP2pRequest4(
                    ClientP2pId,
                    LocalEndpoint.Address,
                    (ushort)LocalEndpoint.Port)),
            new A9Vue990NamedPacket(
                "native-lan-search",
                A9Vue990Hlp2pPacketBuilder.LanSearch,
                A9Vue990Hlp2pPacketBuilder.BuildLanSearch()),
        ];
    }

    public IReadOnlyList<A9Vue990NamedPacket> BuildNativeAlivePackets()
    {
        return
        [
            new A9Vue990NamedPacket(
                "native-p2p-alive",
                A9Vue990Hlp2pPacketBuilder.P2pAlive,
                A9Vue990Hlp2pPacketBuilder.BuildP2pAlive()),
            new A9Vue990NamedPacket(
                "native-p2p-alive-ack",
                A9Vue990Hlp2pPacketBuilder.P2pAliveAck,
                A9Vue990Hlp2pPacketBuilder.BuildP2pAliveAck()),
        ];
    }
}

public sealed class A9Vue990DasTokenSnapshot
{
    public int Index { get; init; }

    public byte[] Bytes { get; init; } = [];

    public int ByteLength => Bytes.Length;

    public string EscapedAscii { get; init; } = string.Empty;

    public string Hex { get; init; } = string.Empty;

    public bool IsPrintableAscii { get; init; }

    public static A9Vue990DasTokenSnapshot FromToken(A9Vue990DasToken token)
    {
        return new A9Vue990DasTokenSnapshot
        {
            Index = token.Index,
            Bytes = token.Bytes.ToArray(),
            EscapedAscii = token.EscapedAscii,
            Hex = token.Hex,
            IsPrintableAscii = token.IsPrintableAscii,
        };
    }
}

public sealed record A9Vue990NamedPacket(string Name, ushort Command, byte[] Bytes);
