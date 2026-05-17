using BodyCam.Json;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;

namespace BodyCam.Services.Glasses.HeyCyan.Media;

/// <summary>
/// Writes JSON sidecars to the app-private Sidecars directory.
/// Atomic write (tmp + move), keyed by SHA-256.
/// </summary>
internal sealed class JsonSidecarWriter : ISidecarWriter
{
    private readonly IMediaDurationProbe _durationProbe;
    private readonly ILogger<JsonSidecarWriter> _log;
    private readonly string _baseDirectory;

    public JsonSidecarWriter(
        IMediaDurationProbe durationProbe,
        ILogger<JsonSidecarWriter> log)
        : this(durationProbe, log, FileSystem.Current.AppDataDirectory)
    {
    }

    internal JsonSidecarWriter(
        IMediaDurationProbe durationProbe,
        ILogger<JsonSidecarWriter> log,
        string baseDirectory)
    {
        _durationProbe = durationProbe;
        _log = log;
        _baseDirectory = baseDirectory;
    }

    public async Task<string> WriteAsync(
        string mediaLocalUri,
        RecordedMediaSidecar sidecar,
        CancellationToken ct)
    {
        // Probe duration if not already set
        var finalSidecar = sidecar;
        if (finalSidecar.Duration is null)
        {
            try
            {
                var duration = await _durationProbe.ProbeAsync(mediaLocalUri, ct).ConfigureAwait(false);
                if (duration.HasValue)
                {
                    finalSidecar = sidecar with { Duration = duration.Value };
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to probe duration for {Uri}, continuing without it", mediaLocalUri);
            }
        }

        // Sidecar path: <BaseDir>/RecordedMedia/Sidecars/<sha256>.bodycam.json
        var sidecarDir = Path.Combine(
            _baseDirectory,
            "RecordedMedia",
            "Sidecars");
        Directory.CreateDirectory(sidecarDir);

        var sidecarPath = Path.Combine(sidecarDir, $"{finalSidecar.Sha256}.bodycam.json");
        var tmpPath = $"{sidecarPath}.tmp";

        // Atomic write
        try
        {
            await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(
                    fs,
                    finalSidecar,
                    BodyCamJsonContext.Default.RecordedMediaSidecar,
                    ct).ConfigureAwait(false);
                await fs.FlushAsync(ct).ConfigureAwait(false);
            }

            // Move to final location (overwrite if exists — refresh metadata)
            File.Move(tmpPath, sidecarPath, overwrite: true);

            _log.LogDebug("Wrote sidecar {Path} for {Source}", sidecarPath, finalSidecar.SourceFileName);
            return sidecarPath;
        }
        catch
        {
            // Clean up tmp file on failure
            try { File.Delete(tmpPath); } catch { /* ignore */ }
            throw;
        }
    }
}
