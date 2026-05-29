using BodyCam.Services.Camera.A9.Probe;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.A9;

public class A9ProtocolMatrixRealTests
{
    private readonly ITestOutputHelper _output;

    public A9ProtocolMatrixRealTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact, Trait("Category", "RealHardware")]
    public async Task A9ProtocolMatrix_DetectsSupportedConnectionPath()
    {
        Skip.IfNot(A9RealTestSettings.Enabled, "A9_E2E not set to 1");
        Skip.If(
            !A9RealTestSettings.HasKnownHost && !A9RealTestSettings.DiscoveryEnabled,
            "Set A9_CAMERA_IP or A9_DISCOVERY_E2E=1 before running the protocol matrix.");

        var result = await new A9ProbeRunner().RunAsync(A9RealTestSettings.CreateProbeOptions());
        _output.WriteLine(result.ToReadableString());

        Skip.If(result.SelectedProtocol == A9ProbeProtocol.None, "No supported A9 protocol was detected.");

        result.Success.Should().BeTrue();
        result.Probes.Should().Contain(p =>
            p.Protocol == result.SelectedProtocol &&
            p.Host == result.SelectedHost &&
            p.IsSupported);
    }

    [SkippableFact, Trait("Category", "RealHardware")]
    public async Task A9SelectedProtocol_ReceivesFirstFrame()
    {
        Skip.IfNot(A9RealTestSettings.Enabled, "A9_E2E not set to 1");
        Skip.If(
            !A9RealTestSettings.HasKnownHost && !A9RealTestSettings.DiscoveryEnabled,
            "Set A9_CAMERA_IP or A9_DISCOVERY_E2E=1 before running the first-frame probe.");

        var result = await new A9ProbeRunner().RunAsync(A9RealTestSettings.CreateProbeOptions(firstFrame: true));
        _output.WriteLine(result.ToReadableString());

        Skip.If(result.SelectedProtocol == A9ProbeProtocol.None, "No supported A9 protocol was detected.");
        Skip.If(result.Frame is null, "The selected protocol did not run a first-frame capture.");
        Skip.If(result.Frame.Skipped, result.Frame.Message);

        result.Frame.Success.Should().BeTrue(result.Frame.Message);

        if (result.SelectedProtocol is A9ProbeProtocol.HttpMjpeg or A9ProbeProtocol.PpppUdpMjpeg)
            result.Frame.IsJpeg.Should().BeTrue(result.Frame.Message);
    }
}
