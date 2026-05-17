namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Cross-platform orchestrator for HeyCyan glasses media transfer (photos/videos/audio).
/// Hides the BLE-trigger → Wi-Fi-Direct → HTTP dance behind a clean interface.
/// Key feature: warm transfer mode — holds the session open across consecutive
/// captures with a short idle timeout (8s) so back-to-back frames amortize the
/// ~2-5s group-formation cost down to 700ms-1.5s.
/// </summary>
public interface IHeyCyanMediaTransfer : IAsyncDisposable
{
    /// <summary>
    /// True if transfer mode is currently active (session is warm).
    /// </summary>
    bool IsWarm { get; }

    /// <summary>
    /// List all media files available on the glasses.
    /// Fetches /files/media.config over HTTP and parses the filenames.
    /// If transfer mode is not active, enters it first.
    /// </summary>
    Task<IReadOnlyList<HeyCyanMediaEntry>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Download a specific media file by name.
    /// If transfer mode is not active, enters it first.
    /// After a successful download, schedules an idle-exit timer (8s default).
    /// </summary>
    Task<byte[]> DownloadAsync(string fileName, CancellationToken ct);

    /// <summary>
    /// Open a stream to download a specific media file by name.
    /// If transfer mode is not active, enters it first.
    /// Caller must dispose the returned stream.
    /// After the stream is closed, schedules an idle-exit timer (8s default).
    /// </summary>
    Task<Stream> OpenAsync(string fileName, CancellationToken ct);

    /// <summary>
    /// Explicitly exit transfer mode and tear down the P2P group.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    Task ExitAsync(CancellationToken ct);
}

/// <summary>
/// Metadata for a single media file on the glasses.
/// </summary>
public sealed record HeyCyanMediaEntry(
    string Name,
    long Size,
    DateTimeOffset Timestamp,
    HeyCyanMediaKind Kind);

/// <summary>
/// Media file type classification by extension.
/// </summary>
public enum HeyCyanMediaKind
{
    Photo,
    Video,
    Audio,
    Other
}
