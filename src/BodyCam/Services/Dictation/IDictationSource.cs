namespace BodyCam.Services.Dictation;

/// <summary>
/// M16 dictation source contract.
/// Stub interface for future M16 dictation feature.
/// When M16 is implemented, this interface will be defined in the M16 module.
/// </summary>
public interface IDictationSource
{
    /// <summary>
    /// Unique identifier for this dictation source (e.g., "heycyan-voicenote").
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// MIME type of the audio stream (e.g., "audio/ogg").
    /// </summary>
    string MimeType { get; }

    /// <summary>
    /// Open the audio stream for the given local URI.
    /// </summary>
    Task<Stream> OpenAsync(string localUri, CancellationToken ct);
}
