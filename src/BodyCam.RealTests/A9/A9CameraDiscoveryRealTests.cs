using BodyCam.Services.Camera.A9.Probe;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.A9;

public class A9CameraDiscoveryRealTests
{
    private readonly ITestOutputHelper _output;

    public A9CameraDiscoveryRealTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact, Trait("Category", "RealHardware")]
    public async Task A9Discovery_DiscoversCameraOrPrintsProbeSummary()
    {
        Skip.IfNot(A9RealTestSettings.Enabled, "A9_E2E not set to 1");
        Skip.IfNot(A9RealTestSettings.DiscoveryEnabled, "A9_DISCOVERY_E2E not set to 1");

        var result = await new A9ProbeRunner().RunAsync(A9RealTestSettings.CreateProbeOptions());
        _output.WriteLine(result.ToReadableString());

        Skip.If(result.SelectedProtocol == A9ProbeProtocol.None, "No A9 camera answered the discovery probe.");
        result.Success.Should().BeTrue();
    }
}
