using BodyCam.Models;
using SkiaSharp;
using ZXing;

namespace BodyCam.Services.QrCode;

public class ZXingQrScanner : IQrCodeScanner
{
    private static readonly BarcodeFormat[] SupportedFormats =
    [
        BarcodeFormat.QR_CODE,
        BarcodeFormat.EAN_13,
        BarcodeFormat.UPC_A,
        BarcodeFormat.CODE_128,
        BarcodeFormat.DATA_MATRIX
    ];

    public Task<QrScanResult?> ScanAsync(byte[] jpegFrame, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            SKBitmap? bitmap = null;
            try
            {
                bitmap = SKBitmap.Decode(jpegFrame);
                if (bitmap is null) return null;

                var luminanceSource = new SKBitmapLuminanceSource(bitmap);
                var reader = new BarcodeReaderGeneric();
                reader.Options.TryHarder = true;
                reader.Options.PossibleFormats = SupportedFormats;

                var result = reader.Decode(luminanceSource);
                if (result is null) return null;

                var format = MapFormat(result.BarcodeFormat);
                return new QrScanResult(result.Text, format, DateTimeOffset.UtcNow);
            }
            catch
            {
                return null;
            }
            finally
            {
                bitmap?.Dispose();
            }
        }, ct);
    }

    private static QrCodeFormat MapFormat(BarcodeFormat format) => format switch
    {
        BarcodeFormat.QR_CODE => QrCodeFormat.QrCode,
        BarcodeFormat.EAN_13 => QrCodeFormat.Ean13,
        BarcodeFormat.UPC_A => QrCodeFormat.UpcA,
        BarcodeFormat.CODE_128 => QrCodeFormat.Code128,
        BarcodeFormat.DATA_MATRIX => QrCodeFormat.DataMatrix,
        _ => QrCodeFormat.Unknown
    };
}
