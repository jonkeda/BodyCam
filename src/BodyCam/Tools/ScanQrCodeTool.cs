using System.ComponentModel;
using BodyCam.Services.QrCode;

namespace BodyCam.Tools;

public class ScanQrCodeArgs
{
    [Description("Optional question about the QR code content")]
    public string? Query { get; set; }
}

public class ScanQrCodeTool : ToolBase<ScanQrCodeArgs>
{
    private readonly IQrCodeScanner _scanner;
    private readonly QrCodeService _history;
    private readonly QrContentResolver _contentResolver;

    public override string Name => "scan_qr_code";
    public override string Description =>
        "Capture a photo and scan for QR codes or barcodes. " +
        "Returns decoded content with type classification and suggested actions.";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "wakewords/bodycam-scan_en_windows.ppn",
        Mode = WakeWordMode.QuickAction,
        InitialPrompt = "Scan for QR codes in front of me and tell me what you find."
    };

    public ScanQrCodeTool(IQrCodeScanner scanner, QrCodeService history, QrContentResolver contentResolver)
    {
        _scanner = scanner;
        _history = history;
        _contentResolver = contentResolver;
    }

    protected override async Task<ToolResult> ExecuteAsync(
        ScanQrCodeArgs args, ToolContext context, CancellationToken ct)
    {
        var frame = await context.CaptureFrame(ct);
        if (frame is null)
            return ToolResult.Fail("Camera not available.");

        var result = await _scanner.ScanAsync(frame, ct);
        if (result is null)
            return ToolResult.Success(new { found = false, message = "No QR code or barcode detected in the image." });

        _history.Add(result);

        var handler = _contentResolver.Resolve(result.Content);
        var parsed = handler.Parse(result.Content);

        return ToolResult.Success(new Dictionary<string, object>
        {
            ["found"] = true,
            ["content"] = result.Content,
            ["format"] = result.Format.ToString(),
            ["content_type"] = handler.ContentType,
            ["suggested_actions"] = handler.SuggestedActions,
            ["details"] = parsed
        });
    }
}
