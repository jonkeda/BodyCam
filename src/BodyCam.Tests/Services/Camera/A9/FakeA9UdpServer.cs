using System.Net;
using System.Net.Sockets;
using BodyCam.Services.Camera.A9;

namespace BodyCam.Tests.Services.Camera.A9;

internal sealed class FakeA9UdpServer : IAsyncDisposable
{
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly FakeA9UdpServerMode _mode;
    private ushort _packetId = 1;

    public FakeA9UdpServer(FakeA9UdpServerMode mode = FakeA9UdpServerMode.Normal)
    {
        _mode = mode;
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public int Port { get; }

    private readonly List<ushort> _receivedCommands = [];

    public IReadOnlyList<ushort> ReceivedCommands
    {
        get
        {
            lock (_receivedCommands)
                return _receivedCommands.ToArray();
        }
    }

    public static byte[] FirstJpegFrame { get; } =
    [
        0xff, 0xd8, 0xff, 0xdb,
        0x01, 0x02, 0x03,
        0xff, 0xd9
    ];

    public static byte[] GoodJpegFrameAfterCorruption { get; } =
    [
        0xff, 0xd8, 0xff, 0xdb,
        0x44, 0x55, 0x66,
        0xff, 0xd9
    ];

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp.ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (result.Buffer.Length < 4) continue;

            var command = A9Protocol.ReadCommandId(result.Buffer);
            lock (_receivedCommands)
            {
                _receivedCommands.Add(command);
            }

            switch (command)
            {
                case A9Protocol.CmdLanSearch:
                    if (_mode != FakeA9UdpServerMode.IgnoreLanSearch)
                        await SendAsync(BuildPunchPkt(), result.RemoteEndPoint, ct);
                    break;

                case A9Protocol.CmdP2pRdy:
                    await SendAsync(BuildP2pRdyReply(), result.RemoteEndPoint, ct);
                    break;

                case A9Protocol.CmdDrw:
                    await HandleDrwAsync(result.Buffer, result.RemoteEndPoint, ct);
                    break;
            }
        }
    }

    private async Task HandleDrwAsync(byte[] packet, IPEndPoint remote, CancellationToken ct)
    {
        if (packet.Length < 12 || packet[5] != 0) return;

        var control = A9Protocol.ReadU16BE(packet, 10);
        switch (control)
        {
            case A9Protocol.CtrlConnectUser:
                if (_mode != FakeA9UdpServerMode.NoLoginAck)
                    await SendAsync(BuildControlAck(A9Protocol.CtrlConnectUserAck), remote, ct);
                break;

            case A9Protocol.CtrlVideoParamSet:
                await SendAsync(BuildControlAck(A9Protocol.CtrlVideoParamSetAck), remote, ct);
                break;

            case A9Protocol.CtrlStartVideo:
                await SendAsync(BuildControlAck(A9Protocol.CtrlStartVideoAck), remote, ct);
                _ = Task.Run(() => SendFramesAsync(remote, _cts.Token), _cts.Token);
                break;
        }
    }

    private async Task SendFramesAsync(IPEndPoint remote, CancellationToken ct)
    {
        await Task.Delay(75, ct);

        if (_mode == FakeA9UdpServerMode.CorruptThenGoodFrames)
        {
            await SendAsync(BuildDataPacket(FirstJpegFrame, _packetId++), remote, ct);
            _packetId++;
            await SendAsync(BuildDataPacket([0x10, 0x20, 0x30, 0x40], _packetId++), remote, ct);
            await SendAsync(BuildDataPacket(GoodJpegFrameAfterCorruption, _packetId++), remote, ct);
            await SendAsync(BuildDataPacket([0xff, 0xd8, 0xff, 0xdb, 0x77, 0xff, 0xd9], _packetId++), remote, ct);
            return;
        }

        await SendAsync(BuildDataPacket(FirstJpegFrame, _packetId++), remote, ct);
        await SendAsync(BuildDataPacket([0xff, 0xd8, 0xff, 0xdb, 0x99, 0xff, 0xd9], _packetId++), remote, ct);
    }

    private async Task SendAsync(byte[] packet, IPEndPoint remote, CancellationToken ct)
    {
        await _udp.SendAsync(packet, packet.Length, remote).WaitAsync(ct);
    }

    private static byte[] BuildPunchPkt()
    {
        var packet = new byte[24];
        A9Protocol.WriteU16BE(packet, 0, A9Protocol.CmdPunchPkt);
        A9Protocol.WriteU16BE(packet, 2, 20);

        WriteAscii(packet, 4, "A9X5");
        WriteU64BE(packet, 8, 42);
        WriteAscii(packet, 16, "TEST");

        return packet;
    }

    private static byte[] BuildP2pRdyReply()
    {
        var punch = BuildPunchPkt();
        return A9Protocol.BuildP2pRdy(punch.AsSpan(4));
    }

    private ushort NextPacketId() => _packetId++;

    private byte[] BuildControlAck(ushort controlCommand)
    {
        var packet = new byte[28];
        A9Protocol.WriteU16BE(packet, 0, A9Protocol.CmdDrw);
        A9Protocol.WriteU16BE(packet, 2, 24);
        packet[4] = 0xd1;
        packet[5] = 0;
        A9Protocol.WriteU16BE(packet, 6, NextPacketId());
        A9Protocol.WriteU16BE(packet, 8, A9Protocol.DrwStartMarker);
        A9Protocol.WriteU16BE(packet, 10, controlCommand);
        A9Protocol.WriteU16LE(packet, 12, 8);
        A9Protocol.WriteU16BE(packet, 14, A9Protocol.GetControlDest(controlCommand));
        packet[24] = 0xaa;
        packet[25] = 0xbb;
        packet[26] = 0xcc;
        packet[27] = 0xdd;
        return packet;
    }

    private static byte[] BuildDataPacket(byte[] payload, ushort packetId)
    {
        var packet = new byte[8 + payload.Length];
        A9Protocol.WriteU16BE(packet, 0, A9Protocol.CmdDrw);
        A9Protocol.WriteU16BE(packet, 2, (ushort)(payload.Length + 4));
        packet[4] = 0xd2;
        packet[5] = 1;
        A9Protocol.WriteU16BE(packet, 6, packetId);
        payload.CopyTo(packet.AsSpan(8));
        return packet;
    }

    private static void WriteAscii(Span<byte> packet, int offset, string value)
    {
        for (var i = 0; i < value.Length; i++)
            packet[offset + i] = (byte)value[i];
    }

    private static void WriteU64BE(Span<byte> packet, int offset, ulong value)
    {
        for (var i = 7; i >= 0; i--)
        {
            packet[offset + i] = (byte)(value & 0xff);
            value >>= 8;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _udp.Dispose();
        try { await _loop; }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}

internal enum FakeA9UdpServerMode
{
    Normal,
    IgnoreLanSearch,
    NoLoginAck,
    CorruptThenGoodFrames,
}
