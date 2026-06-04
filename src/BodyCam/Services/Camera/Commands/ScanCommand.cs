using BodyCam.Services.QrCode;

namespace BodyCam.Services.Camera.Commands;

public sealed record ScanCommandOptions(string? Query);

public sealed class ScanCommand : CameraCommandBase<ScanCommandOptions>, ICameraActionVariantProvider
{
    private readonly IQrCodeScanner _scanner;
    private readonly QrCodeService _history;
    private readonly QrContentResolver _contentResolver;

    public ScanCommand(
        IQrCodeScanner scanner,
        QrCodeService history,
        QrContentResolver contentResolver)
    {
        _scanner = scanner;
        _history = history;
        _contentResolver = contentResolver;
    }

    public override string Id => "scan";
    public override string DisplayName => "Scan";
    public override string? ToolName => "scan_qr_code";

    public override CameraCommandCapabilities Capabilities { get; } = new(
        SupportsFullAuto: true,
        SupportsManualAim: true,
        RequiresStillFrame: true,
        CanUseFrameStream: false,
        RequiresConfirmationForExternalActions: true);

    public override IReadOnlyList<CommandOptionDefinition> Options { get; } =
    [
        new(nameof(ScanCommandOptions.Query), typeof(string), null, false),
    ];

    public IReadOnlyList<CameraActionVariantDefinition> CameraActionVariants =>
    [
        new(
            "Default",
            DisplayName,
            "Scan code.",
            new ScanCommandOptions(Query: null),
            IsDefault: true),
    ];

    public override async Task<CameraCommandResult> ExecuteAsync(
        CameraCommandContext context,
        CancellationToken ct)
    {
        var options = ResolveOptions(context);
        var frame = await CaptureFrameForModeAsync(context, ct).ConfigureAwait(false);
        if (frame is null)
            return CameraUnavailable(Id);

        var scan = await _scanner.ScanAsync(frame, ct).ConfigureAwait(false);
        if (scan is null)
        {
            var notFoundData = BaseData(context, options);
            notFoundData["found"] = false;
            notFoundData["message"] = "No QR code or barcode detected in the image.";

            return new CameraCommandResult(
                Id,
                Success: true,
                TranscriptText: "No QR code or barcode detected.",
                Data: notFoundData,
                Error: null);
        }

        _history.Add(scan);

        var handler = _contentResolver.Resolve(scan.Content);
        var parsed = handler.Parse(scan.Content);
        var summary = handler.Summarize(parsed);

        var data = BaseData(context, options);
        data["found"] = true;
        data["content"] = scan.Content;
        data["format"] = scan.Format.ToString();
        data["content_type"] = handler.ContentType;
        data["suggested_actions"] = handler.SuggestedActions;
        data["details"] = parsed;
        data["requires_confirmation"] = context.Settings.ConfirmExternalScanActions;

        return new CameraCommandResult(
            Id,
            Success: true,
            TranscriptText: $"{handler.DisplayName}: {summary}",
            Data: data,
            Error: null);
    }

    public ScanCommandOptions ResolveOptions(CameraCommandContext context)
    {
        var supplied = TryReadOptions(context.Request);
        return new ScanCommandOptions(Normalize(supplied?.Query ?? context.Request.Query));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
