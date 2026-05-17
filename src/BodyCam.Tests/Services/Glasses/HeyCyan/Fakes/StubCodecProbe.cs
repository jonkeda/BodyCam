using BodyCam.Services.Glasses.HeyCyan;

namespace BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;

/// <summary>
/// Stub codec probe for testing HeyCyanAudioDiagnostics.
/// Returns a fixed HeyCyanAudioRouteInfo or throws, depending on configuration.
/// </summary>
public sealed class StubCodecProbe : IHeyCyanCodecProbe
{
    public HeyCyanAudioRouteInfo? FixedResult { get; set; }
    public bool ShouldThrow { get; set; }
    public string? LastProbedMac { get; private set; }
    public int ProbeCallCount { get; private set; }

    public Task<HeyCyanAudioRouteInfo?> ProbeAsync(string mac, CancellationToken ct)
    {
        ProbeCallCount++;
        LastProbedMac = mac;

        if (ShouldThrow)
            throw new InvalidOperationException("Codec probe intentionally failed (test).");

        return Task.FromResult(FixedResult);
    }
}
