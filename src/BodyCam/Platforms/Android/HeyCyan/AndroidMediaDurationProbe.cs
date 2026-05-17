#if ANDROID
using Android.Content;
using Android.Media;
using Microsoft.Extensions.Logging;
using BodyCam.Services.Glasses.HeyCyan.Media;

namespace BodyCam.Platforms.Android.HeyCyan;

/// <summary>
/// Android duration probe using MediaMetadataRetriever.
/// </summary>
internal sealed class AndroidMediaDurationProbe : IMediaDurationProbe
{
    private readonly Context _context;
    private readonly ILogger<AndroidMediaDurationProbe> _log;

    public AndroidMediaDurationProbe(ILogger<AndroidMediaDurationProbe> log)
    {
        _context = global::Android.App.Application.Context;
        _log = log;
    }

    public Task<TimeSpan?> ProbeAsync(string localUri, CancellationToken ct)
    {
        try
        {
            using var retriever = new MediaMetadataRetriever();

            // Parse content:// URI
            var androidUri = global::Android.Net.Uri.Parse(localUri);
            if (androidUri is null)
            {
                _log.LogWarning("Failed to parse URI: {Uri}", localUri);
                return Task.FromResult<TimeSpan?>(null);
            }

            retriever.SetDataSource(_context, androidUri);

            var durationStr = retriever.ExtractMetadata(MetadataKey.Duration);
            if (string.IsNullOrEmpty(durationStr))
            {
                _log.LogDebug("No duration metadata in {Uri}", localUri);
                return Task.FromResult<TimeSpan?>(null);
            }

            if (long.TryParse(durationStr, out var durationMs))
            {
                var duration = TimeSpan.FromMilliseconds(durationMs);
                _log.LogDebug("Probed duration {Duration} for {Uri}", duration, localUri);
                return Task.FromResult<TimeSpan?>(duration);
            }

            _log.LogWarning("Failed to parse duration '{DurationStr}' for {Uri}", durationStr, localUri);
            return Task.FromResult<TimeSpan?>(null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Exception probing duration for {Uri}", localUri);
            return Task.FromResult<TimeSpan?>(null);
        }
    }
}
#endif
