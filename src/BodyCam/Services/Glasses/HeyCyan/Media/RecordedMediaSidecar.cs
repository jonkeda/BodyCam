namespace BodyCam.Services.Glasses.HeyCyan.Media;

/// <summary>
/// Provenance metadata sidecar for imported MP4 and OPUS files.
/// Written as `.bodycam.json` in the app-private Sidecars directory,
/// keyed by SHA-256 to enable instant dedup.
/// </summary>
public sealed record RecordedMediaSidecar(
    int Schema,
    string SourceFileName,
    string GlassesMacAddress,
    DateTimeOffset ImportedAt,
    DateTimeOffset? GlassesTimestamp,
    TimeSpan? Duration,
    long SizeBytes,
    string Sha256);
