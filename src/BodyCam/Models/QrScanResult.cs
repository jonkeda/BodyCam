namespace BodyCam.Models;

public record QrScanResult(
    string Content,
    QrCodeFormat Format,
    DateTimeOffset ScannedAt);

public enum QrCodeFormat
{
    QrCode,
    Ean13,
    UpcA,
    Code128,
    DataMatrix,
    Unknown
}
