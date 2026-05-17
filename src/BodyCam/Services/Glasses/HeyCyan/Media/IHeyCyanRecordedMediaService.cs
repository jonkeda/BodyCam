namespace BodyCam.Services.Glasses.HeyCyan.Media;

/// <summary>
/// Bulk-import the recorded .jpg / .mp4 / .opus files that accumulate on
/// the glasses, classify them, normalize audio, and write them into the
/// platform photo / video / audio library.
/// </summary>
public interface IHeyCyanRecordedMediaService
{
    /// <summary>
    /// Fired when an audio file is successfully imported.
    /// Enables optional M16 dictation hook.
    /// </summary>
    event EventHandler<ImportedMediaItem>? AudioImported;

    /// <summary>
    /// List all recorded media files on the glasses.
    /// Enters transfer mode if not already active.
    /// </summary>
    IAsyncEnumerable<RecordedMediaItem> EnumerateAsync(CancellationToken ct);

    /// <summary>
    /// Import all recorded media files from the glasses.
    /// Skips files that already exist locally (idempotent).
    /// </summary>
    IAsyncEnumerable<ImportedMediaItem> ImportAllAsync(
        IProgress<RecordedMediaImportProgress>? progress,
        CancellationToken ct);

    /// <summary>
    /// Import a single recorded media file.
    /// Throws NotSupportedException for Unknown kind.
    /// </summary>
    Task<ImportedMediaItem> ImportAsync(
        RecordedMediaItem item,
        CancellationToken ct);

    /// <summary>
    /// Delete a file from the glasses (opt-in).
    /// Returns false if not supported by firmware.
    /// </summary>
    Task<bool> DeleteRemoteAsync(string fileName, CancellationToken ct);
}
