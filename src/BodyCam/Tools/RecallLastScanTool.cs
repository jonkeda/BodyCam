using BodyCam.Services.QrCode;

namespace BodyCam.Tools;

public class RecallLastScanArgs { }

public class RecallLastScanTool : ToolBase<RecallLastScanArgs>
{
    private readonly QrCodeService _history;
    private readonly QrContentResolver _contentResolver;

    public override string Name => "recall_last_scan";
    public override string Description =>
        "Recall the most recent QR code or barcode scan result. " +
        "Use when the user asks 'what was that QR code?' or 'what did we scan?'";

    public RecallLastScanTool(QrCodeService history, QrContentResolver contentResolver)
    {
        _history = history;
        _contentResolver = contentResolver;
    }

    protected override Task<ToolResult> ExecuteAsync(
        RecallLastScanArgs args, ToolContext context, CancellationToken ct)
    {
        var last = _history.LastResult;
        if (last is null)
            return Task.FromResult(ToolResult.Success(new { found = false, message = "No previous scan results." }));

        var handler = _contentResolver.Resolve(last.Content);
        var parsed = handler.Parse(last.Content);

        return Task.FromResult(ToolResult.Success(new Dictionary<string, object>
        {
            ["found"] = true,
            ["content"] = last.Content,
            ["format"] = last.Format.ToString(),
            ["content_type"] = handler.ContentType,
            ["scanned_at"] = last.ScannedAt.ToString("o"),
            ["details"] = parsed
        }));
    }
}
