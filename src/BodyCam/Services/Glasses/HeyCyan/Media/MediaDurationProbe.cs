namespace BodyCam.Services.Glasses.HeyCyan.Media;

/// <summary>
/// Platform-specific duration probing for MP4 and OGG files.
/// Failures are non-fatal; returns null on unsupported platforms or errors.
/// </summary>
public interface IMediaDurationProbe
{
    /// <summary>
    /// Extract duration from the media file at the given URI.
    /// Returns null on failure or unsupported platforms.
    /// </summary>
    Task<TimeSpan?> ProbeAsync(string localUri, CancellationToken ct);
}
