using BodyCam.Services.QrCode;

namespace BodyCam.Services.Vision;

/// <summary>
/// Pipeline stage that scans for QR codes and barcodes using ZXing (local, ~10ms).
/// </summary>
public class QrScanStage : IVisionPipelineStage
{
    private readonly IQrCodeScanner _scanner;
    private readonly QrCodeService _history;
    private readonly QrContentResolver _resolver;

    public string Name => "QR Scan";
    public int Cost => 0;

    public QrScanStage(IQrCodeScanner scanner, QrCodeService history, QrContentResolver resolver)
    {
        _scanner = scanner;
        _history = history;
        _resolver = resolver;
    }

    public async Task<VisionPipelineResult?> ProcessAsync(
        byte[] jpegFrame, string? query, CancellationToken ct)
    {
        var scan = await _scanner.ScanAsync(jpegFrame, ct);
        if (scan is null) return null;

        _history.Add(scan);
        var handler = _resolver.Resolve(scan.Content);
        var parsed = handler.Parse(scan.Content);

        return new VisionPipelineResult("QR Scan", handler.Summarize(parsed), new()
        {
            ["found_type"] = "qr_barcode",
            ["content"] = scan.Content,
            ["format"] = scan.Format.ToString(),
            ["content_type"] = handler.ContentType,
            ["suggested_actions"] = handler.SuggestedActions,
            ["details"] = parsed,
        });
    }
}
