using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Camera.A9;

/// <summary>
/// Manages a UDP session with an A9/X5 camera using the iLnkP2P/PPPP protocol.
///
/// Session lifecycle:
///   1. Send LanSearch broadcast (or unicast to known IP) on UDP port 32108.
///   2. Camera replies with PunchPkt containing its device serial.
///   3. Send P2pRdy echoing the serial back.
///   4. Camera replies with P2pRdy → we send ConnectUser (login).
///   5. Camera replies with ConnectUserAck containing a 4-byte session ticket.
///   6. Send VideoResolution + StartVideo using the ticket.
///   7. Camera streams Drw data packets containing JPEG frames.
///   8. We ACK each Drw packet and reassemble JPEG frames.
///
/// Keepalive: camera sends P2PAlive every 400-500 ms; we reply with P2PAliveAck.
/// If no packet received for <see cref="TimeoutMs"/>, we consider the session dead.
/// </summary>
internal sealed class A9Session : IAsyncDisposable
{
    private readonly ILogger _log;
    private readonly string _targetIp;
    private readonly string _username;
    private readonly string _password;
    private readonly int _port;
    private readonly int _timeoutMs;
    private readonly int _keepaliveIntervalMs;

    private UdpClient? _udp;
    private IPEndPoint? _remoteEp;
    private CancellationTokenSource? _sessionCts;
    private Task? _receiveLoop;

    // Session state
    private byte[] _ticket = [0, 0, 0, 0];
    private int _outgoingCmdId;
    private int _rcvSeqId;
    private bool _loggedIn;
    private bool _streaming;
    private long _lastReceivedTicks;

    // Frame assembly: JPEG frames come in multiple Drw data packets
    private readonly List<byte[]> _currentFrameSegments = [];
    private bool _frameIsBad;

    public const int TimeoutMs = 5000;
    private const int KeepaliveIntervalMs = 400;

    /// <summary>Fires when a complete JPEG frame has been assembled.</summary>
    public event Action<byte[]>? FrameReceived;

    /// <summary>Fires when the session disconnects (timeout, error, or explicit close).</summary>
    public event Action? Disconnected;

    /// <summary>Whether the session is currently streaming video frames.</summary>
    public bool IsStreaming => _streaming;

    /// <summary>Device ID parsed from the PunchPkt (e.g. "FTYC477360FAWUK").</summary>
    public string? DeviceId { get; private set; }

    public A9Session(
        string targetIp,
        string username,
        string password,
        ILogger logger,
        int port = A9Protocol.DefaultPort,
        int timeoutMs = TimeoutMs,
        int keepaliveIntervalMs = KeepaliveIntervalMs)
    {
        _targetIp = targetIp;
        _username = username;
        _password = password;
        _log = logger;
        _port = port;
        _timeoutMs = timeoutMs;
        _keepaliveIntervalMs = keepaliveIntervalMs;
    }

    /// <summary>
    /// Discover the camera, establish a session, and start the JPEG video stream.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _udp = new UdpClient();
        _udp.Client.ReceiveTimeout = _timeoutMs;
        _remoteEp = new IPEndPoint(IPAddress.Parse(_targetIp), _port);
        _lastReceivedTicks = Environment.TickCount64;
        _outgoingCmdId = 0;

        // Step 1: Send LanSearch to discover the camera
        _log.LogInformation("A9: Sending LanSearch to {Ip}", _targetIp);
        var lanSearch = A9Protocol.BuildLanSearch();
        await _udp.SendAsync(lanSearch, lanSearch.Length, _remoteEp);

        // Step 2: Wait for PunchPkt response
        var punchResult = await ReceiveWithTimeoutAsync(_timeoutMs, _sessionCts.Token);
        if (punchResult is null)
            throw new TimeoutException($"A9: No PunchPkt response from {_targetIp}");

        var cmdId = A9Protocol.ReadCommandId(punchResult);
        if (cmdId != A9Protocol.CmdPunchPkt)
            throw new InvalidOperationException($"A9: Expected PunchPkt (0xf141), got 0x{cmdId:x4}");

        DeviceId = A9Protocol.ParsePunchPktDeviceId(punchResult);
        _log.LogInformation("A9: Discovered camera {DeviceId} at {Ip}", DeviceId, _targetIp);

        // Step 3: Send P2pRdy echoing the device serial
        var p2pRdy = A9Protocol.BuildP2pRdy(punchResult.AsSpan(4));
        await _udp.SendAsync(p2pRdy, p2pRdy.Length, _remoteEp);

        // Step 4: Wait for P2pRdy reply, then send ConnectUser
        var rdyReply = await ReceiveWithTimeoutAsync(_timeoutMs, _sessionCts.Token);
        if (rdyReply is not null)
        {
            var rdyCmd = A9Protocol.ReadCommandId(rdyReply);
            if (rdyCmd == A9Protocol.CmdP2pRdy)
            {
                _log.LogDebug("A9: Got P2pRdy reply, sending ConnectUser");
                var connectUser = A9Protocol.BuildConnectUser(ref _outgoingCmdId, _ticket, _username, _password);
                await _udp.SendAsync(connectUser, connectUser.Length, _remoteEp);
            }
        }

        // Step 5: Wait for ConnectUserAck containing the session ticket
        // The ack comes inside a Drw control packet, so we need to process it
        var loginDeadline = Environment.TickCount64 + _timeoutMs;
        while (!_loggedIn && Environment.TickCount64 < loginDeadline)
        {
            _sessionCts.Token.ThrowIfCancellationRequested();
            var pkt = await ReceiveWithTimeoutAsync(Math.Min(1000, _timeoutMs), _sessionCts.Token);
            if (pkt is not null)
                ProcessPacket(pkt);
        }

        if (!_loggedIn)
            throw new TimeoutException("A9: Login timed out — no ConnectUserAck received");

        _log.LogInformation("A9: Logged in to camera {DeviceId}", DeviceId);

        // Step 6: Set video resolution to 640x480 and start the stream
        var res = A9Protocol.BuildVideoResolution(ref _outgoingCmdId, _ticket, 2);
        await _udp.SendAsync(res, res.Length, _remoteEp);

        var startVideo = A9Protocol.BuildStartVideo(ref _outgoingCmdId, _ticket);
        await _udp.SendAsync(startVideo, startVideo.Length, _remoteEp);
        _streaming = true;
        _log.LogInformation("A9: Video stream started on {DeviceId}", DeviceId);

        // Step 7: Start the background receive + keepalive loop
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_sessionCts.Token), _sessionCts.Token);
    }

    /// <summary>
    /// Background loop: read incoming UDP packets, handle keepalives, detect timeouts.
    /// </summary>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var keepaliveTimer = Environment.TickCount64;

        try
        {
            while (!ct.IsCancellationRequested && _udp is not null)
            {
                // Check timeout
                if (Environment.TickCount64 - _lastReceivedTicks > _timeoutMs)
                {
                    _log.LogWarning("A9: Camera {DeviceId} timed out", DeviceId);
                    break;
                }

                // Send keepalive if needed
                if (Environment.TickCount64 - keepaliveTimer > _keepaliveIntervalMs)
                {
                    var alive = A9Protocol.BuildP2pAlive();
                    try { await _udp.SendAsync(alive, alive.Length, _remoteEp); }
                    catch { /* best effort */ }
                    keepaliveTimer = Environment.TickCount64;
                }

                // Try to receive a packet (non-blocking with short timeout)
                try
                {
                    var result = await ReceiveWithTimeoutAsync(200, ct);
                    if (result is not null)
                        ProcessPacket(result);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex)
        {
            _log.LogError(ex, "A9: Receive loop error");
        }
        finally
        {
            _streaming = false;
            Disconnected?.Invoke();
        }
    }

    /// <summary>
    /// Route an incoming packet to the appropriate handler based on its command ID.
    /// </summary>
    private void ProcessPacket(byte[] data)
    {
        if (data.Length < 4) return;
        _lastReceivedTicks = Environment.TickCount64;

        var cmdId = A9Protocol.ReadCommandId(data);
        switch (cmdId)
        {
            case A9Protocol.CmdP2pAlive:
                HandleP2pAlive();
                break;

            case A9Protocol.CmdP2pAliveAck:
                // Our keepalive was acknowledged — nothing to do
                break;

            case A9Protocol.CmdDrw:
                HandleDrw(data);
                break;

            case A9Protocol.CmdDrwAck:
                // ACK for a packet we sent — no retransmit logic needed for now
                break;

            case A9Protocol.CmdClose:
                _log.LogInformation("A9: Camera requested close");
                _streaming = false;
                break;

            default:
                _log.LogDebug("A9: Unhandled command 0x{CmdId:x4}", cmdId);
                break;
        }
    }

    /// <summary>Reply to a keepalive ping from the camera.</summary>
    private void HandleP2pAlive()
    {
        var ack = A9Protocol.BuildP2pAliveAck();
        try { _udp?.Send(ack, ack.Length, _remoteEp); }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Handle a Drw packet. The 5th byte (offset 4) discriminates:
    ///   0xd1 = outgoing direction (we shouldn't receive this normally)
    ///   0xd2 = ack (handled separately)
    ///   stream byte at offset 5: 0 = control sub-command, 1 = data (audio/video)
    /// </summary>
    private void HandleDrw(byte[] data)
    {
        if (data.Length < 8) return;

        // ACK the packet immediately
        byte streamId = data[5];
        ushort packetId = A9Protocol.ReadU16BE(data, 6);
        var ack = A9Protocol.BuildDrwAck(streamId, packetId);
        try { _udp?.Send(ack, ack.Length, _remoteEp); }
        catch { /* best effort */ }

        if (streamId == 0)
        {
            // Control sub-command
            HandleDrwControl(data);
        }
        else if (streamId == 1)
        {
            // Data (video/audio)
            HandleDrwData(data);
        }
    }

    /// <summary>
    /// Handle a control sub-command inside a Drw packet.
    /// Most importantly, extract the session ticket from ConnectUserAck.
    /// </summary>
    private void HandleDrwControl(byte[] data)
    {
        if (data.Length < 14) return;

        // Verify start marker at offset 8
        ushort startType = A9Protocol.ReadU16BE(data, 8);
        if (startType != A9Protocol.DrwStartMarker) return;

        ushort controlCmd = A9Protocol.ReadU16BE(data, 10);
        ushort payloadLen = A9Protocol.ReadU16LE(data, 12);
        const int rotateKey = 4;

        // Decrypt the payload if it's longer than 4 bytes (skip the first 20 bytes of header)
        if (payloadLen > rotateKey && data.Length > 20)
        {
            int decLen = Math.Min(payloadLen - 4, data.Length - 20);
            if (decLen > 0)
                A9Protocol.XqBytesDec(data.AsSpan(20, decLen), rotateKey);
        }

        switch (controlCmd)
        {
            case A9Protocol.CtrlConnectUserAck:
                // Ticket is at offset 0x18 (24), 4 bytes
                if (data.Length >= 0x1c)
                {
                    Array.Copy(data, 0x18, _ticket, 0, 4);
                    _loggedIn = true;
                    _log.LogDebug("A9: Got ConnectUserAck, ticket: {Ticket}",
                        BitConverter.ToString(_ticket));
                }
                break;

            case A9Protocol.CtrlStartVideoAck:
                _log.LogDebug("A9: StartVideo acknowledged");
                break;

            case A9Protocol.CtrlVideoParamSetAck:
                _log.LogDebug("A9: VideoParamSet acknowledged");
                break;

            case A9Protocol.CtrlDevStatusAck:
                _log.LogDebug("A9: DevStatus acknowledged");
                break;

            default:
                _log.LogDebug("A9: Unhandled control sub-command 0x{Cmd:x4}", controlCmd);
                break;
        }
    }

    /// <summary>
    /// Handle a Drw data packet containing video (JPEG) or audio frames.
    ///
    /// Frame reassembly:
    ///   - A new JPEG image starts either with a "framed" packet (stream_type == 0x03
    ///     with a 32-byte stream_head_t) or with unframed data starting with FF D8 FF DB.
    ///   - Subsequent packets in the same frame are continuation segments.
    ///   - When a new frame starts, the previous frame's segments are concatenated and emitted.
    ///   - Packets arriving out of sequence mark the frame as bad (dropped).
    /// </summary>
    private void HandleDrwData(byte[] data)
    {
        if (data.Length < 12) return;

        ushort pktLen = A9Protocol.ReadU16BE(data, 2);
        ushort pktId = A9Protocol.ReadU16BE(data, 6);

        // Check for the 4-byte frame header at offset 8 (0x55aa15a8 = audio, else potentially framed video)
        bool isFramed = data.Length >= 12 &&
                        data[8] == A9Protocol.AudioFrameHeader[0] &&
                        data[9] == A9Protocol.AudioFrameHeader[1] &&
                        data[10] == A9Protocol.AudioFrameHeader[2] &&
                        data[11] == A9Protocol.AudioFrameHeader[3];

        if (isFramed)
        {
            if (data.Length < 13) return;
            byte streamType = data[12];

            if (streamType == A9Protocol.StreamTypeJpeg)
            {
                // New JPEG frame with stream_head_t (32 bytes after offset 8)
                int toRead = pktLen - 4 - 32;
                if (toRead > 0 && data.Length >= 40 + toRead)
                {
                    var segment = new byte[toRead];
                    Array.Copy(data, 40, segment, 0, toRead);
                    StartNewFrame(segment, pktId);
                }
            }
            // Audio frames (streamType == 0x06) are ignored for now
        }
        else
        {
            // Unframed data: check if it starts with JPEG SOI marker (FF D8 FF DB)
            bool isNewImage = data.Length >= 12 &&
                              data[8] == A9Protocol.JpegHeader[0] &&
                              data[9] == A9Protocol.JpegHeader[1] &&
                              data[10] == A9Protocol.JpegHeader[2] &&
                              data[11] == A9Protocol.JpegHeader[3];

            int dataOffset = 8;
            int dataLen = pktLen - 4;
            if (dataLen <= 0 || data.Length < dataOffset + dataLen) return;

            var segData = new byte[dataLen];
            Array.Copy(data, dataOffset, segData, 0, dataLen);

            if (isNewImage)
            {
                StartNewFrame(segData, pktId);
            }
            else
            {
                // Continuation segment
                if (pktId <= _rcvSeqId)
                    return; // retransmit — ignore

                if (pktId > _rcvSeqId + 1)
                {
                    // Packet loss — mark frame as bad and drop it
                    if (!_frameIsBad)
                    {
                        _frameIsBad = true;
                        _log.LogDebug("A9: Dropping corrupt frame, expected seq {Expected} got {Got}",
                            _rcvSeqId + 1, pktId);
                    }
                    return;
                }

                _rcvSeqId = pktId;
                _currentFrameSegments.Add(segData);
            }
        }
    }

    /// <summary>
    /// Emit the previous frame (if valid) and start accumulating a new one.
    /// </summary>
    private void StartNewFrame(byte[] firstSegment, int pktId)
    {
        EmitCurrentFrame();
        _frameIsBad = false;
        _currentFrameSegments.Clear();
        _currentFrameSegments.Add(firstSegment);
        _rcvSeqId = pktId;
    }

    /// <summary>
    /// If we have a valid frame assembled, concatenate segments and fire FrameReceived.
    /// </summary>
    private void EmitCurrentFrame()
    {
        if (_currentFrameSegments.Count == 0 || _frameIsBad) return;

        int totalLen = 0;
        foreach (var seg in _currentFrameSegments) totalLen += seg.Length;

        var jpeg = new byte[totalLen];
        int offset = 0;
        foreach (var seg in _currentFrameSegments)
        {
            Array.Copy(seg, 0, jpeg, offset, seg.Length);
            offset += seg.Length;
        }

        FrameReceived?.Invoke(jpeg);
    }

    /// <summary>Receive a single UDP packet with a timeout.</summary>
    private async Task<byte[]?> ReceiveWithTimeoutAsync(int timeoutMs, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var result = await _udp!.ReceiveAsync(linked.Token);
            return result.Buffer;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return null; // timeout — not a cancellation
        }
    }

    public async ValueTask DisposeAsync()
    {
        _streaming = false;

        if (_sessionCts is not null)
        {
            await _sessionCts.CancelAsync();
            if (_receiveLoop is not null)
            {
                try { await _receiveLoop; }
                catch { /* swallow cancellation */ }
            }
            _sessionCts.Dispose();
        }

        _udp?.Dispose();
        _udp = null;
    }
}
