using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Null implementation of <see cref="IHeyCyanCodecProbe"/> for platforms
/// that don't support HeyCyan glasses (Windows, MacCatalyst, etc.).
/// Always returns null.
/// </summary>
internal sealed class NullCodecProbe : IHeyCyanCodecProbe
{
    private readonly ILogger<NullCodecProbe> _log;

    public NullCodecProbe(ILogger<NullCodecProbe> log)
    {
        _log = log;
    }

    public Task<HeyCyanAudioRouteInfo?> ProbeAsync(string mac, CancellationToken ct)
    {
        _log.LogDebug("Codec probe not supported on this platform.");
        return Task.FromResult<HeyCyanAudioRouteInfo?>(null);
    }
}
