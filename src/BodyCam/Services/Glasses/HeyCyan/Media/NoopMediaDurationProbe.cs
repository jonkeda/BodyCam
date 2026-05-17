namespace BodyCam.Services.Glasses.HeyCyan.Media;

/// <summary>
/// Noop duration probe for unsupported platforms (Windows, etc.).
/// Always returns null.
/// </summary>
internal sealed class NoopMediaDurationProbe : IMediaDurationProbe
{
    public Task<TimeSpan?> ProbeAsync(string localUri, CancellationToken ct)
    {
        return Task.FromResult<TimeSpan?>(null);
    }
}
