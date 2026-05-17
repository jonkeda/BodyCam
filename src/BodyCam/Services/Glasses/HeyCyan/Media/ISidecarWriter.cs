namespace BodyCam.Services.Glasses.HeyCyan.Media;

/// <summary>
/// Abstracts sidecar file writing for test substitution.
/// </summary>
public interface ISidecarWriter
{
    /// <summary>
    /// Write a JSON sidecar for the given imported media file.
    /// Returns the absolute path to the written sidecar file.
    /// </summary>
    Task<string> WriteAsync(
        string mediaLocalUri,
        RecordedMediaSidecar sidecar,
        CancellationToken ct);
}
