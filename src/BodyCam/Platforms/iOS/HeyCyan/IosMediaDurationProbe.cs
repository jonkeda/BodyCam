#if IOS || MACCATALYST
using AVFoundation;
using Foundation;
using Microsoft.Extensions.Logging;
using BodyCam.Services.Glasses.HeyCyan.Media;

namespace BodyCam.Platforms.iOS.HeyCyan;

/// <summary>
/// iOS/Mac duration probe using AVAsset.
/// </summary>
internal sealed class IosMediaDurationProbe : IMediaDurationProbe
{
    private readonly ILogger<IosMediaDurationProbe> _log;

    public IosMediaDurationProbe(ILogger<IosMediaDurationProbe> log)
    {
        _log = log;
    }

    public Task<TimeSpan?> ProbeAsync(string localUri, CancellationToken ct)
    {
        try
        {
            var nsUrl = NSUrl.FromString(localUri);
            if (nsUrl is null)
            {
                _log.LogWarning("Failed to parse URI: {Uri}", localUri);
                return Task.FromResult<TimeSpan?>(null);
            }

            var asset = AVAsset.FromUrl(nsUrl);
            if (asset is null)
            {
                _log.LogWarning("Failed to load AVAsset from {Uri}", localUri);
                return Task.FromResult<TimeSpan?>(null);
            }

            var duration = TimeSpan.FromSeconds(asset.Duration.Seconds);
            _log.LogDebug("Probed duration {Duration} for {Uri}", duration, localUri);
            return Task.FromResult<TimeSpan?>(duration);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Exception probing duration for {Uri}", localUri);
            return Task.FromResult<TimeSpan?>(null);
        }
    }
}
#endif
