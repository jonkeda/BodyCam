using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Tests.TestInfrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

public class StoredImageHeyCyanMediaTransferTests
{
    [Fact]
    public async Task DownloadAsync_creates_and_reads_fallback_jpeg_file()
    {
        using var temp = new TempFallbackDirectory();
        var transfer = Build(temp.Path, TestAssets.MinimalJpeg);

        var result = await transfer.DownloadAsync("ignored.jpg", CancellationToken.None);

        result.Should().BeEquivalentTo(TestAssets.MinimalJpeg);
        File.Exists(Path.Combine(temp.Path, StoredImageHeyCyanMediaTransfer.DefaultFallbackFileName))
            .Should().BeTrue();
        transfer.IsWarm.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_returns_single_fallback_photo_entry()
    {
        using var temp = new TempFallbackDirectory();
        var transfer = Build(temp.Path, TestAssets.MinimalJpeg);

        var entries = await transfer.ListAsync(CancellationToken.None);

        entries.Should().ContainSingle();
        entries[0].Name.Should().Be(StoredImageHeyCyanMediaTransfer.DefaultFallbackFileName);
        entries[0].Kind.Should().Be(HeyCyanMediaKind.Photo);
        entries[0].Size.Should().Be(TestAssets.MinimalJpeg.Length);
    }

    [Fact]
    public async Task OpenAsync_returns_readable_stream_for_created_jpeg()
    {
        using var temp = new TempFallbackDirectory();
        var transfer = Build(temp.Path, TestAssets.MinimalJpeg);

        await using var stream = await transfer.OpenAsync("ignored.jpg", CancellationToken.None);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        ms.ToArray().Should().BeEquivalentTo(TestAssets.MinimalJpeg);
    }

    [Fact]
    public async Task DownloadAsync_reuses_existing_file()
    {
        using var temp = new TempFallbackDirectory();
        var existing = new byte[] { 0xFF, 0xD8, 0x44, 0x55 };
        File.WriteAllBytes(
            Path.Combine(temp.Path, StoredImageHeyCyanMediaTransfer.DefaultFallbackFileName),
            existing);

        var transfer = Build(temp.Path, TestAssets.MinimalJpeg);

        var result = await transfer.DownloadAsync("ignored.jpg", CancellationToken.None);

        result.Should().BeEquivalentTo(existing);
    }

    [Fact]
    public async Task DownloadAsync_non_jpeg_seed_throws()
    {
        using var temp = new TempFallbackDirectory();
        var transfer = Build(temp.Path, new byte[] { 0x00, 0x01, 0x02 });

        var act = () => transfer.DownloadAsync("ignored.jpg", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    private static StoredImageHeyCyanMediaTransfer Build(string directory, byte[] seedBytes) =>
        new(
            NullLogger<StoredImageHeyCyanMediaTransfer>.Instance,
            directory,
            seedBytes);

    private sealed class TempFallbackDirectory : IDisposable
    {
        public TempFallbackDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"bodycam-heycyan-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
