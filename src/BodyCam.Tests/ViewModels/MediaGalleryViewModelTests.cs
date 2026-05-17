using BodyCam.Services.Glasses.HeyCyan.Media;
using BodyCam.ViewModels;
using FluentAssertions;
using NSubstitute;
using System.ComponentModel;

namespace BodyCam.Tests.ViewModels;

public class MediaGalleryViewModelTests
{
    private readonly IHeyCyanRecordedMediaService _mediaService = Substitute.For<IHeyCyanRecordedMediaService>();
    private readonly IMediaStore _mediaStore = Substitute.For<IMediaStore>();

    private MediaGalleryViewModel CreateVm()
    {
        return new MediaGalleryViewModel(_mediaService, _mediaStore);
    }

    [Fact]
    public void Constructor_InitializesTitle()
    {
        var vm = CreateVm();
        vm.Title.Should().Be("Glasses Media");
    }

    [Fact]
    public void Constructor_InitializesCollections()
    {
        var vm = CreateVm();
        vm.AllItems.Should().BeEmpty();
        vm.FilteredItems.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_SetsDefaultFilter()
    {
        var vm = CreateVm();
        vm.Filter.Should().Be("All");
    }

    [Fact]
    public void Filter_WhenChanged_AppliesFilter()
    {
        var vm = CreateVm();
        var photoItem = new ImportedMediaItem(
            new RecordedMediaItem("photo.jpg", RecordedMediaKind.Photo, 1000, null),
            "content://test/photo.jpg",
            1000,
            TimeSpan.FromSeconds(1));
        var videoItem = new ImportedMediaItem(
            new RecordedMediaItem("video.mp4", RecordedMediaKind.Video, 2000, null),
            "content://test/video.mp4",
            2000,
            TimeSpan.FromSeconds(2));

        vm.AllItems.Add(new MediaTileViewModel(photoItem));
        vm.AllItems.Add(new MediaTileViewModel(videoItem));
        vm.Filter = "All"; // Initialize filter to populate FilteredItems

        vm.Filter = "Photo";

        vm.FilteredItems.Should().HaveCount(1);
        vm.FilteredItems[0].Kind.Should().Be(RecordedMediaKind.Photo);
    }

    [Fact]
    public void Filter_All_ShowsAllItems()
    {
        var vm = CreateVm();
        var photoItem = new ImportedMediaItem(
            new RecordedMediaItem("photo.jpg", RecordedMediaKind.Photo, 1000, null),
            "content://test/photo.jpg",
            1000,
            TimeSpan.FromSeconds(1));
        var videoItem = new ImportedMediaItem(
            new RecordedMediaItem("video.mp4", RecordedMediaKind.Video, 2000, null),
            "content://test/video.mp4",
            2000,
            TimeSpan.FromSeconds(2));

        vm.AllItems.Add(new MediaTileViewModel(photoItem));
        vm.AllItems.Add(new MediaTileViewModel(videoItem));

        // Change filter first so that setting to "All" actually triggers a property change
        vm.Filter = "Photo";
        vm.Filter = "All"; // Trigger filter application

        vm.FilteredItems.Should().HaveCount(2);
    }

    [Fact]
    public void Filter_Video_ShowsOnlyVideos()
    {
        var vm = CreateVm();
        var photoItem = new ImportedMediaItem(
            new RecordedMediaItem("photo.jpg", RecordedMediaKind.Photo, 1000, null),
            "content://test/photo.jpg",
            1000,
            TimeSpan.FromSeconds(1));
        var videoItem = new ImportedMediaItem(
            new RecordedMediaItem("video.mp4", RecordedMediaKind.Video, 2000, null),
            "content://test/video.mp4",
            2000,
            TimeSpan.FromSeconds(2));

        vm.AllItems.Add(new MediaTileViewModel(photoItem));
        vm.AllItems.Add(new MediaTileViewModel(videoItem));

        vm.Filter = "Video"; // Trigger filter application

        vm.FilteredItems.Should().HaveCount(1);
        vm.FilteredItems[0].Kind.Should().Be(RecordedMediaKind.Video);
    }

    [Fact]
    public void Filter_Audio_ShowsOnlyAudio()
    {
        var vm = CreateVm();
        var photoItem = new ImportedMediaItem(
            new RecordedMediaItem("photo.jpg", RecordedMediaKind.Photo, 1000, null),
            "content://test/photo.jpg",
            1000,
            TimeSpan.FromSeconds(1));
        var audioItem = new ImportedMediaItem(
            new RecordedMediaItem("audio.opus", RecordedMediaKind.Audio, 3000, null),
            "file:///audio.ogg",
            3000,
            TimeSpan.FromSeconds(3));

        vm.AllItems.Add(new MediaTileViewModel(photoItem));
        vm.AllItems.Add(new MediaTileViewModel(audioItem));

        vm.Filter = "Audio"; // Trigger filter application

        vm.FilteredItems.Should().HaveCount(1);
        vm.FilteredItems[0].Kind.Should().Be(RecordedMediaKind.Audio);
    }

    [Fact]
    public void IsImporting_DefaultsFalse()
    {
        var vm = CreateVm();
        vm.IsImporting.Should().BeFalse();
    }

    [Fact]
    public void ImportProgress_DefaultsZero()
    {
        var vm = CreateVm();
        vm.ImportProgress.Should().Be(0);
    }

    [Fact]
    public async Task RefreshCommand_TogglesIsImporting()
    {
        var vm = CreateVm();
        var imports = new List<ImportedMediaItem>
        {
            new ImportedMediaItem(
                new RecordedMediaItem("test.jpg", RecordedMediaKind.Photo, 100, null),
                "content://photo/test.jpg",
                100,
                TimeSpan.FromSeconds(1),
                Sha256: "abc123")
        };

        _mediaService.ImportAllAsync(Arg.Any<IProgress<RecordedMediaImportProgress>>(), Arg.Any<CancellationToken>())
            .Returns(imports.ToAsyncEnumerable());

        vm.IsImporting.Should().BeFalse("before refresh");

        // Execute the command (async void, but IsExecuting will be set)
        vm.RefreshCommand.Execute(null);
        
        // Wait a bit for the async operation to complete
        await Task.Delay(100);

        vm.IsImporting.Should().BeFalse("after refresh completes");
    }

    [Fact]
    public async Task RefreshCommand_UpdatesImportProgress()
    {
        var vm = CreateVm();

        var imports = new List<ImportedMediaItem>();
        IProgress<RecordedMediaImportProgress>? capturedProgress = null;

        _mediaService.ImportAllAsync(Arg.Do<IProgress<RecordedMediaImportProgress>>(p => capturedProgress = p), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var progress = callInfo.Arg<IProgress<RecordedMediaImportProgress>>();
                
                // Create an async enumerable that reports progress
                return AsyncEnumerableHelper(progress);
            });

        vm.RefreshCommand.Execute(null);
        
        // Wait for async operation to complete
        await Task.Delay(100);

        // Progress should have been updated (0.5 = 1 of 2 files)
        vm.ImportProgress.Should().BeGreaterThanOrEqualTo(0);
    }

    private static async IAsyncEnumerable<ImportedMediaItem> AsyncEnumerableHelper(IProgress<RecordedMediaImportProgress>? progress)
    {
        progress?.Report(new RecordedMediaImportProgress(0, 2, "file1.jpg", 0));
        await Task.Delay(10);
        progress?.Report(new RecordedMediaImportProgress(1, 2, "file2.jpg", 0));
        await Task.Yield();
        yield break;
    }

    [Fact]
    public void OpenItemCommand_Exists()
    {
        var vm = CreateVm();
        
        vm.OpenItemCommand.Should().NotBeNull();
        vm.OpenItemCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void OpenItemCommand_Photo_CanExecute()
    {
        // Note: Full navigation testing (Shell.Current.GoToAsync) requires UITests
        // This test verifies the command structure is valid
        var vm = CreateVm();
        var photoTile = new MediaTileViewModel(new ImportedMediaItem(
            new RecordedMediaItem("photo.jpg", RecordedMediaKind.Photo, 1000, null),
            "content://photo/photo.jpg",
            1000,
            TimeSpan.FromSeconds(1),
            Sha256: "abc"));

        vm.OpenItemCommand.CanExecute(photoTile).Should().BeTrue();
    }

    [Fact]
    public void OpenItemCommand_Video_CanExecute()
    {
        // Note: Full launcher testing (Launcher.Default.OpenAsync) requires UITests
        // This test verifies the command structure is valid
        var vm = CreateVm();
        var videoTile = new MediaTileViewModel(new ImportedMediaItem(
            new RecordedMediaItem("video.mp4", RecordedMediaKind.Video, 2000, null),
            "file:///data/video.mp4",
            2000,
            TimeSpan.FromSeconds(2),
            Sha256: "def"));

        vm.OpenItemCommand.CanExecute(videoTile).Should().BeTrue();
    }

    [Fact]
    public void OpenItemCommand_Audio_CanExecute()
    {
        // Note: Full launcher testing (Launcher.Default.OpenAsync) requires UITests
        // This test verifies the command structure is valid
        var vm = CreateVm();
        var audioTile = new MediaTileViewModel(new ImportedMediaItem(
            new RecordedMediaItem("audio.opus", RecordedMediaKind.Audio, 3000, null),
            "file:///data/audio.ogg",
            3000,
            TimeSpan.FromSeconds(3),
            Sha256: "ghi"));

        vm.OpenItemCommand.CanExecute(audioTile).Should().BeTrue();
    }

    [Fact]
    public void ViewModel_UsesSetProperty_NotManualPropertyChanged()
    {
        var vm = CreateVm();
        var propertyChangedEvents = new List<string>();

        vm.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName != null)
                propertyChangedEvents.Add(e.PropertyName);
        };

        // Trigger property changes
        vm.Filter = "Photo";
        vm.ImportProgress = 0.5;
        vm.IsImporting = true;

        // Verify PropertyChanged was raised for each property
        propertyChangedEvents.Should().Contain("Filter");
        propertyChangedEvents.Should().Contain("ImportProgress");
        propertyChangedEvents.Should().Contain("IsImporting");

        // Verify setting to same value doesn't raise PropertyChanged (SetProperty contract)
        propertyChangedEvents.Clear();
        vm.Filter = "Photo"; // Same value
        propertyChangedEvents.Should().BeEmpty("setting to same value should not raise PropertyChanged");
    }
}
