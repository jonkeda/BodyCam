namespace BodyCam.Services.Glasses.HeyCyan.Media;

/// <summary>
/// Platform abstraction for saving media files to the system library
/// (Android MediaStore, iOS Photos, or fallback file system).
/// </summary>
public interface IMediaStore
{
    /// <summary>
    /// Save an image to the photo library.
    /// Returns the content:// (Android) or file:// (iOS) URI.
    /// </summary>
    Task<string> SaveImageAsync(string fileName, Stream content, CancellationToken ct);

    /// <summary>
    /// Save a video to the video library.
    /// Returns the content:// (Android) or file:// (iOS) URI.
    /// </summary>
    Task<string> SaveVideoAsync(string fileName, Stream content, CancellationToken ct);

    /// <summary>
    /// Save an audio file to the audio library or documents folder.
    /// Returns the content:// (Android) or file:// (iOS/other) URI.
    /// </summary>
    Task<string> SaveAudioAsync(
        string fileName,
        string mimeType,
        Stream content,
        CancellationToken ct);

    /// <summary>
    /// Check if a file already exists in the library (idempotency).
    /// </summary>
    Task<bool> ExistsAsync(
        string fileName,
        RecordedMediaKind kind,
        CancellationToken ct);
}
