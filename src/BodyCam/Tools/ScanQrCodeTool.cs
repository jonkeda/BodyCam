using System.ComponentModel;
using BodyCam.Services.Camera.Commands;

namespace BodyCam.Tools;

public class ScanQrCodeArgs
{
    [Description("Optional question about the QR code content")]
    public string? Query { get; set; }
}

public class ScanQrCodeTool : ToolBase<ScanQrCodeArgs>
{
    private readonly ICameraCommandService _commands;

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

    public ScanQrCodeTool(ICameraCommandService commands)
    {
        _commands = commands;
    }

    protected override async Task<ToolResult> ExecuteAsync(
        ScanQrCodeArgs args, ToolContext context, CancellationToken ct)
    {
        var request = new CameraCommandRequest(
            "scan",
            Mode: null,
            Origin: context.CommandOrigin ?? CommandTriggerOrigin.LlmToolCall,
            Options: new ScanCommandOptions(args.Query),
            Query: args.Query);

        var result = await _commands.ExecuteAsync(request, ct);
        return result.Success
            ? ToolResult.Success(result.Data ?? new { message = result.TranscriptText })
            : ToolResult.Fail(result.Error ?? result.TranscriptText);
    }
}
