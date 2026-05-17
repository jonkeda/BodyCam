namespace BodyCam.Services.Glasses.HeyCyan.Media;

/// <summary>
/// Classify recorded media files by extension.
/// Matches the iOS demo's MediaGalleryViewController filter.
/// </summary>
public static class RecordedMediaClassifier
{
    public static RecordedMediaKind Classify(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" => RecordedMediaKind.Photo,
            ".mp4" or ".mov" => RecordedMediaKind.Video,
            ".opus" or ".ogg" => RecordedMediaKind.Audio,
            _ => RecordedMediaKind.Unknown
        };
    }
}
