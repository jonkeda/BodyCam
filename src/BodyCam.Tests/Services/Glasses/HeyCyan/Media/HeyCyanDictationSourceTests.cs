using BodyCam.Services.Glasses.HeyCyan.Media;
using FluentAssertions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan.Media;

public class HeyCyanDictationSourceTests
{
    [Fact]
    public void SourceId_is_heycyan_voicenote()
    {
        var source = new HeyCyanDictationSource();
        source.SourceId.Should().Be("heycyan-voicenote");
    }

    [Fact]
    public void MimeType_is_audio_ogg()
    {
        var source = new HeyCyanDictationSource();
        source.MimeType.Should().Be("audio/ogg");
    }

    [Fact]
    public async Task OpenAsync_opens_file_from_path()
    {
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "test content");

        try
        {
            var source = new HeyCyanDictationSource();
            await using var stream = await source.OpenAsync(tempFile, CancellationToken.None);

            stream.Should().NotBeNull();
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            content.Should().Be("test content");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task OpenAsync_opens_file_from_file_uri()
    {
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "test content from uri");
        var fileUri = new Uri(tempFile).AbsoluteUri;

        try
        {
            var source = new HeyCyanDictationSource();
            await using var stream = await source.OpenAsync(fileUri, CancellationToken.None);

            stream.Should().NotBeNull();
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            content.Should().Be("test content from uri");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
