using BodyCam.Tools;
using FluentAssertions;
using SkiaSharp;

namespace BodyCam.Tests.Tools;

public class FindObjectAnnotationTests
{
    private static byte[] CreateTestJpeg()
    {
        using var bitmap = new SKBitmap(100, 100);
        using var surface = SKSurface.Create(new SKImageInfo(100, 100));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    [Fact]
    public void AnnotateFrame_ProducesValidJpeg()
    {
        var jpeg = CreateTestJpeg();
        var annotated = FindObjectTool.AnnotateFrame(jpeg, "test object");

        annotated.Should().NotBeEmpty();
        annotated.Should().NotEqual(jpeg);
        // Verify JPEG magic bytes
        annotated[0].Should().Be(0xFF);
        annotated[1].Should().Be(0xD8);
    }
}
