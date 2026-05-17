using BodyCam.Services.Dictation;

namespace BodyCam.Services.Glasses.HeyCyan.Media;

/// <summary>
/// M16 dictation source for HeyCyan-imported .opus voice notes.
/// When M16 is present and the FeedVoiceNotesToDictation flag is enabled,
/// this source provides a playable Ogg/Opus stream to the transcription pipeline.
/// </summary>
public sealed class HeyCyanDictationSource : IDictationSource
{
    public string SourceId => "heycyan-voicenote";
    public string MimeType => "audio/ogg";

    /// <summary>
    /// Open the audio file at the given local URI.
    /// Accepts "file://" URIs or raw file paths.
    /// </summary>
    public Task<Stream> OpenAsync(string localUri, CancellationToken ct)
    {
        var path = LocalPath(localUri);
        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    private static string LocalPath(string uri)
    {
        if (uri.StartsWith("file://", StringComparison.Ordinal))
        {
            return new Uri(uri).LocalPath;
        }
        return uri;
    }
}
