using BodyCam.Mvvm;
using BodyCam.Services.Glasses.HeyCyan.Media;

namespace BodyCam.ViewModels;

/// <summary>
/// View model for a single media tile in the gallery.
/// </summary>
public sealed class MediaTileViewModel : ViewModelBase
{
    private ImageSource? _thumbnailSource;

    public MediaTileViewModel(ImportedMediaItem item)
    {
        LocalUri = item.LocalUri;
        FileName = item.Source.FileName;
        Kind = item.Source.Kind;
        Duration = null; // Set externally from sidecar if available
    }

    public string LocalUri { get; }
    public string FileName { get; }
    public RecordedMediaKind Kind { get; }

    public bool IsPhoto => Kind == RecordedMediaKind.Photo;
    public bool IsVideo => Kind == RecordedMediaKind.Video;
    public bool IsAudio => Kind == RecordedMediaKind.Audio;

    public ImageSource? ThumbnailSource
    {
        get => _thumbnailSource;
        set => SetProperty(ref _thumbnailSource, value);
    }

    private string? _duration;
    public string? Duration
    {
        get => _duration;
        set => SetProperty(ref _duration, value);
    }

    /// <summary>
    /// Lazy-load thumbnail on first access.
    /// </summary>
    public async Task LoadThumbnailAsync(Func<MediaTileViewModel, Task<ImageSource?>> loader)
    {
        if (ThumbnailSource is not null)
            return;

        ThumbnailSource = await loader(this);
    }
}
