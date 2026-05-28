using BodyCam.Services.Camera.A9;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Camera.A9;

public class A9SessionTests
{
    [Fact]
    public async Task ConnectAsync_WithMockServer_StartsStreamingAndReceivesFrame()
    {
        await using var server = new FakeA9UdpServer();
        await using var session = CreateSession(server.Port);
        var frameReceived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.FrameReceived += frame => frameReceived.TrySetResult(frame);

        await session.ConnectAsync(CancellationToken.None);
        var frame = await frameReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        session.IsStreaming.Should().BeTrue();
        session.DeviceId.Should().StartWith("A9X5");
        frame.Should().Equal(FakeA9UdpServer.FirstJpegFrame);
        server.ReceivedCommands.Should().Contain(A9Protocol.CmdLanSearch);
        server.ReceivedCommands.Should().Contain(A9Protocol.CmdP2pRdy);
        server.ReceivedCommands.Should().Contain(A9Protocol.CmdDrw);
    }

    [Fact]
    public async Task ConnectAsync_WhenNoPunchPktArrives_TimesOut()
    {
        await using var server = new FakeA9UdpServer(FakeA9UdpServerMode.IgnoreLanSearch);
        await using var session = CreateSession(server.Port, timeoutMs: 200);

        var act = async () => await session.ConnectAsync(CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*No PunchPkt*");
    }

    [Fact]
    public async Task ConnectAsync_WhenLoginAckMissing_TimesOut()
    {
        await using var server = new FakeA9UdpServer(FakeA9UdpServerMode.NoLoginAck);
        await using var session = CreateSession(server.Port, timeoutMs: 300);

        var act = async () => await session.ConnectAsync(CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*Login timed out*");
    }

    [Fact]
    public async Task CorruptFrame_IsDroppedAndNextGoodFrameIsEmitted()
    {
        await using var server = new FakeA9UdpServer(FakeA9UdpServerMode.CorruptThenGoodFrames);
        await using var session = CreateSession(server.Port);
        var frameReceived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.FrameReceived += frame => frameReceived.TrySetResult(frame);

        await session.ConnectAsync(CancellationToken.None);
        var frame = await frameReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        frame.Should().Equal(FakeA9UdpServer.GoodJpegFrameAfterCorruption);
    }

    [Fact]
    public async Task DisposeAsync_StopsStreamingAndRaisesDisconnected()
    {
        await using var server = new FakeA9UdpServer();
        var session = CreateSession(server.Port);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Disconnected += () => disconnected.TrySetResult();

        await session.ConnectAsync(CancellationToken.None);
        await session.DisposeAsync();

        session.IsStreaming.Should().BeFalse();
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static A9Session CreateSession(int port, int timeoutMs = 1000)
    {
        return new A9Session(
            "127.0.0.1",
            "admin",
            "admin",
            NullLogger.Instance,
            port,
            timeoutMs,
            keepaliveIntervalMs: 100);
    }
}
