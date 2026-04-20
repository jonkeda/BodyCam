using SkiaSharp;
using ZXing;

namespace BodyCam.Services.QrCode;

public class SKBitmapLuminanceSource : BaseLuminanceSource
{
    public SKBitmapLuminanceSource(SKBitmap bitmap)
        : base(bitmap.Width, bitmap.Height)
    {
        var pixels = bitmap.Pixels;
        luminances = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            // ITU-R BT.709 luminance
            luminances[i] = (byte)(0.2126f * c.Red + 0.7152f * c.Green + 0.0722f * c.Blue);
        }
    }

    private SKBitmapLuminanceSource(byte[] luminances, int width, int height)
        : base(width, height)
    {
        this.luminances = luminances;
    }

    protected override LuminanceSource CreateLuminanceSource(byte[] newLuminances, int width, int height)
        => new SKBitmapLuminanceSource(newLuminances, width, height);
}
