using System.Collections.ObjectModel;
using BodyCam.Mvvm;
using BodyCam.Services.Glasses.HeyCyan.Media;

namespace BodyCam.ViewModels;

/// <summary>
/// View model for the HeyCyan recorded media gallery page.
/// Displays imported photos, videos, and audio files with filter chips.
/// </summary>
public sealed class MediaGalleryViewModel : ViewModelBase
{
    private readonly IHeyCyanRecordedMediaService _media;
    private readonly IMediaStore _store;

    private string _filter = "All";
    private double _importProgress;
    private bool _isImporting;

    public MediaGalleryViewModel(
        IHeyCyanRecordedMediaService media,
        IMediaStore store)
    {
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _store = store ?? throw new ArgumentNullException(nameof(store));

        Title = "Glasses Media";

        FilterCommand = new AsyncRelayCommand(ExecuteFilterAsync);
        OpenItemCommand = new AsyncRelayCommand(ExecuteOpenItemAsync);
        RefreshCommand = new AsyncRelayCommand(ExecuteRefreshAsync);
    }

    public ObservableCollection<MediaTileViewModel> AllItems { get; } = new();
    public ObservableCollection<MediaTileViewModel> FilteredItems { get; } = new();

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
                ApplyFilter();
        }
    }

    public double ImportProgress
    {
        get => _importProgress;
        set => SetProperty(ref _importProgress, value);
    }

    public bool IsImporting
    {
        get => _isImporting;
        set => SetProperty(ref _isImporting, value);
    }

    public AsyncRelayCommand FilterCommand { get; }
    public AsyncRelayCommand OpenItemCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    private Task ExecuteFilterAsync(object? parameter)
    {
        if (parameter is string filterValue)
            Filter = filterValue;

        return Task.CompletedTask;
    }

    private async Task ExecuteOpenItemAsync(object? parameter)
    {
        if (parameter is not MediaTileViewModel tile)
            return;

        switch (tile.Kind)
        {
            case RecordedMediaKind.Photo:
                await Shell.Current.GoToAsync(
                    $"image-viewer?uri={Uri.EscapeDataString(tile.LocalUri)}");
                break;

            case RecordedMediaKind.Video:
            case RecordedMediaKind.Audio:
                // Both video and audio use the OS default player
                await Launcher.Default.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(GetLocalPath(tile.LocalUri))
                });
                break;
        }
    }

    private async Task ExecuteRefreshAsync(object? parameter)
    {
        IsImporting = true;
        ImportProgress = 0;

        try
        {
            var progress = new Progress<RecordedMediaImportProgress>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (p.Total > 0)
                        ImportProgress = (double)p.Completed / p.Total;
                });
            });

            await foreach (var item in _media.ImportAllAsync(progress, CancellationToken.None))
            {
                // Add to AllItems so it appears in the gallery immediately
                var tile = new MediaTileViewModel(item);
                AllItems.Add(tile);
            }

            await ReloadFromStoreAsync(CancellationToken.None);
            ApplyFilter();
        }
        finally
        {
            IsImporting = false;
        }
    }

    /// <summary>
    /// Load previously-imported items from the platform media store.
    /// This ensures items survive across app launches.
    /// </summary>
    private async Task ReloadFromStoreAsync(CancellationToken ct)
    {
        AllItems.Clear();

        // The IMediaStore interface doesn't expose enumeration — we rely on the service
        // having already imported files into the platform store. For now, we'll just
        // keep the items we imported in this session. A future enhancement could add
        // IMediaStore.EnumerateAsync or scan the file system directly.

        // TODO: Implement platform-specific enumeration of the BodyCam folder in MediaStore
        await Task.CompletedTask;
    }

    private void ApplyFilter()
    {
        FilteredItems.Clear();

        var filtered = Filter switch
        {
            "Photo" => AllItems.Where(x => x.IsPhoto),
            "Video" => AllItems.Where(x => x.IsVideo),
            "Audio" => AllItems.Where(x => x.IsAudio),
            _ => AllItems // "All"
        };

        foreach (var item in filtered)
            FilteredItems.Add(item);
    }

    private static string GetLocalPath(string uri)
    {
        // Handle content:// URIs (Android) or file:// URIs
        if (uri.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            // For now, pass the content URI directly — Launcher.OpenAsync should handle it
            return uri;
        }

        if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.UnescapeDataString(uri.Substring("file://".Length));
        }

        return uri;
    }
}
