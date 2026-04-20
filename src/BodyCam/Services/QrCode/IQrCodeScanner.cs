using BodyCam.Models;

namespace BodyCam.Services.QrCode;

public interface IQrCodeScanner
{
    Task<QrScanResult?> ScanAsync(byte[] jpegFrame, CancellationToken ct = default);
}
