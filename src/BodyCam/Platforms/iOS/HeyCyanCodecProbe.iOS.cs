#if IOS
using BodyCam.Services.Glasses.HeyCyan;
using Microsoft.Extensions.Logging;

namespace BodyCam.Platforms.iOS;

/// <summary>
/// iOS implementation of <see cref="IHeyCyanCodecProbe"/>.
/// iOS deliberately hides Bluetooth codec details from third-party apps.
/// Returns a route info with all codec fields set to null/0.
/// </summary>
internal sealed class HeyCyanCodecProbe : IHeyCyanCodecProbe
{
    private readonly ILogger<HeyCyanCodecProbe> _log;

    public HeyCyanCodecProbe(ILogger<HeyCyanCodecProbe> log)
    {
        _log = log;
    }

    public Task<HeyCyanAudioRouteInfo?> ProbeAsync(string mac, CancellationToken ct)
    {
        _log.LogDebug("iOS does not expose Bluetooth codec details to third-party apps.");

        return Task.FromResult<HeyCyanAudioRouteInfo?>(new HeyCyanAudioRouteInfo(
            "heycyan-glasses",
            "heycyan-glasses",
            NegotiatedA2dpCodec: null,
            SampleRateHz: 0,
            Channels: 0,
            HfpCodec: null));
    }
}
#endif
