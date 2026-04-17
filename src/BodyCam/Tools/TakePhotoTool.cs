using System.ComponentModel;

namespace BodyCam.Tools;

public class TakePhotoArgs
{
    [Description("Optional description or label for the photo")]
    public string? Description { get; set; }
}

public class TakePhotoTool : ToolBase<TakePhotoArgs>
{
    public override string Name => "take_photo";
    public override string Description =>
        "Take a photo with the camera and save it. " +
        "Use when the user asks to take a picture or capture an image.";

    protected override async Task<ToolResult> ExecuteAsync(
        TakePhotoArgs args, ToolContext context, CancellationToken ct)
    {
        var frame = await context.CaptureFrame(ct);
        if (frame is null)
            return ToolResult.Fail("Camera not available.");

        // Save to app data directory
        var fileName = $"photo_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jpg";
        var filePath = Path.Combine(FileSystem.AppDataDirectory, "photos", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllBytesAsync(filePath, frame, ct);

        context.Log($"Photo saved: {fileName}");

        return ToolResult.Success(new
        {
            saved = true,
            fileName,
            description = args.Description ?? "Photo captured"
        });
    }
}
