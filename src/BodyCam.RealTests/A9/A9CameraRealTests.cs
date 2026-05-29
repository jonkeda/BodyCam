using BodyCam.Services.Camera.A9;
using BodyCam.Services.Camera.A9.Probe;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.A9;

public class A9CameraRealTests
{
    private readonly ITestOutputHelper _output;

    public A9CameraRealTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static bool RealEnabled =>
        Environment.GetEnvironmentVariable("A9_E2E") == "1";

    [SkippableFact, Trait("Category", "RealHardware")]
    public async Task A9Session_ConnectsAndReceivesJpegFrame()
    {
        Skip.IfNot(RealEnabled, "A9_E2E not set to 1");

        var ip = Environment.GetEnvironmentVariable("A9_CAMERA_IP");
        Skip.If(string.IsNullOrWhiteSpace(ip), "A9_CAMERA_IP not set");

        var probe = await new A9ProbeRunner().RunAsync(A9RealTestSettings.CreateProbeOptions(
            protocol: A9ProbeProtocol.PpppUdpMjpeg));
        _output.WriteLine(probe.ToReadableString());

        Skip.If(
            probe.SelectedProtocol != A9ProbeProtocol.PpppUdpMjpeg,
            "The configured camera did not answer the PPPP/MJPEG protocol used by A9Session.");

        var username = Environment.GetEnvironmentVariable("A9_CAMERA_USERNAME") ?? "admin";
        var password = Environment.GetEnvironmentVariable("A9_CAMERA_PASSWORD") ?? "admin";
        await using var session = new A9Session(ip!, username, password, NullLogger.Instance);
        var frameReceived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.FrameReceived += frame => frameReceived.TrySetResult(frame);

        using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await session.ConnectAsync(connectTimeout.Token);
        var frame = await frameReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

        frame.Should().NotBeNull();
        frame.Should().StartWith(new byte[] { 0xff, 0xd8 });
        _output.WriteLine($"A9 frame bytes: {frame.Length}");
        _output.WriteLine($"A9 device id: {session.DeviceId}");
    }
}
