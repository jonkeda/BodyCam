using BodyCam.Services.Glasses.HeyCyan.Media;
using FluentAssertions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan.Media;

/// <summary>
/// Tests for <see cref="RecordedMediaClassifier"/>.
/// Verifies classification of file extensions per the Wave 2 specification.
/// </summary>
public class RecordedMediaClassifierTests
{
    [Theory]
    [InlineData("IMG_001.jpg", RecordedMediaKind.Photo)]
    [InlineData("IMG_001.JPG", RecordedMediaKind.Photo)]
    [InlineData("IMG_001.jpeg", RecordedMediaKind.Photo)]
    [InlineData("IMG_001.JPEG", RecordedMediaKind.Photo)]
    [InlineData("IMG_001.png", RecordedMediaKind.Photo)]
    [InlineData("IMG_001.PNG", RecordedMediaKind.Photo)]
    [InlineData("VID_001.mp4", RecordedMediaKind.Video)]
    [InlineData("VID_001.MP4", RecordedMediaKind.Video)]
    [InlineData("VID_001.mov", RecordedMediaKind.Video)]
    [InlineData("VID_001.MOV", RecordedMediaKind.Video)]
    [InlineData("AUD_001.opus", RecordedMediaKind.Audio)]
    [InlineData("AUD_001.OPUS", RecordedMediaKind.Audio)]
    [InlineData("AUD_001.ogg", RecordedMediaKind.Audio)]
    [InlineData("AUD_001.OGG", RecordedMediaKind.Audio)]
    [InlineData("file.txt", RecordedMediaKind.Unknown)]
    [InlineData("file.bin", RecordedMediaKind.Unknown)]
    [InlineData("file", RecordedMediaKind.Unknown)]
    [InlineData("", RecordedMediaKind.Unknown)]
    public void Classify_ByExtension_ReturnsCorrectKind(string fileName, RecordedMediaKind expectedKind)
    {
        var result = RecordedMediaClassifier.Classify(fileName);

        result.Should().Be(expectedKind);
    }
}
