using BodyCam.Services.Glasses.HeyCyan.Media;
using BodyCam.ViewModels;
using FluentAssertions;

namespace BodyCam.Tests.ViewModels;

public class MediaTileViewModelTests
{
    [Fact]
    public void Constructor_InitializesProperties()
    {
        var item = new ImportedMediaItem(
            new RecordedMediaItem("photo.jpg", RecordedMediaKind.Photo, 1000, null),
            "content://test/photo.jpg",
            1000,
            TimeSpan.FromSeconds(1));

        var vm = new MediaTileViewModel(item);

        vm.LocalUri.Should().Be("content://test/photo.jpg");
        vm.FileName.Should().Be("photo.jpg");
        vm.Kind.Should().Be(RecordedMediaKind.Photo);
    }

    [Fact]
    public void IsPhoto_TrueForPhotoKind()
    {
        var item = new ImportedMediaItem(
            new RecordedMediaItem("photo.jpg", RecordedMediaKind.Photo, 1000, null),
            "content://test/photo.jpg",
            1000,
            TimeSpan.FromSeconds(1));

        var vm = new MediaTileViewModel(item);

        vm.IsPhoto.Should().BeTrue();
        vm.IsVideo.Should().BeFalse();
        vm.IsAudio.Should().BeFalse();
    }

    [Fact]
    public void IsVideo_TrueForVideoKind()
    {
        var item = new ImportedMediaItem(
            new RecordedMediaItem("video.mp4", RecordedMediaKind.Video, 2000, null),
            "content://test/video.mp4",
            2000,
            TimeSpan.FromSeconds(2));

        var vm = new MediaTileViewModel(item);

        vm.IsPhoto.Should().BeFalse();
        vm.IsVideo.Should().BeTrue();
        vm.IsAudio.Should().BeFalse();
    }

    [Fact]
    public void IsAudio_TrueForAudioKind()
    {
        var item = new ImportedMediaItem(
            new RecordedMediaItem("audio.opus", RecordedMediaKind.Audio, 3000, null),
            "file:///audio.ogg",
            3000,
            TimeSpan.FromSeconds(3));

        var vm = new MediaTileViewModel(item);

        vm.IsPhoto.Should().BeFalse();
        vm.IsVideo.Should().BeFalse();
        vm.IsAudio.Should().BeTrue();
    }

    [Fact]
    public void ThumbnailSource_DefaultsToNull()
    {
        var item = new ImportedMediaItem(
            new RecordedMediaItem("photo.jpg", RecordedMediaKind.Photo, 1000, null),
            "content://test/photo.jpg",
            1000,
            TimeSpan.FromSeconds(1));

        var vm = new MediaTileViewModel(item);

        vm.ThumbnailSource.Should().BeNull();
    }

    [Fact]
    public void Duration_DefaultsToNull()
    {
        var item = new ImportedMediaItem(
            new RecordedMediaItem("photo.jpg", RecordedMediaKind.Photo, 1000, null),
            "content://test/photo.jpg",
            1000,
            TimeSpan.FromSeconds(1));

        var vm = new MediaTileViewModel(item);

        vm.Duration.Should().BeNull();
    }

    [Fact]
    public async Task LoadThumbnailAsync_CallsLoader()
    {
        var item = new ImportedMediaItem(
            new RecordedMediaItem("photo.jpg", RecordedMediaKind.Photo, 1000, null),
            "content://test/photo.jpg",
            1000,
            TimeSpan.FromSeconds(1));

        var vm = new MediaTileViewModel(item);
        var loaderCalled = false;

        await vm.LoadThumbnailAsync(tile =>
        {
            loaderCalled = true;
            return Task.FromResult<ImageSource?>(null);
        });

        loaderCalled.Should().BeTrue();
    }
}
