# M18 Phase 1 — Core QR Code Scanning

**Status:** NOT STARTED  
**Depends on:** M11 Phase 1 (CameraManager), M13 Phase 1 (AudioOutputManager)

---

## Goal

Capture a camera frame on demand, decode QR codes from it using ZXing.Net, and return the content to the AI so it can read it aloud and ask the user what to do.

---

## Wave 1: Scanner Interface + ZXing Implementation

### 1.1 `IQrCodeScanner` Interface

```
Services/QrCode/IQrCodeScanner.cs
```

```csharp
public interface IQrCodeScanner
{
    Task<QrScanResult?> ScanAsync(byte[] jpegFrame, CancellationToken ct = default);
}
```

### 1.2 `QrScanResult` Model

```
Models/QrScanResult.cs
```

```csharp
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
```

Phase 1 only handles `QrCode`; other formats are Phase 2.

### 1.3 `ZXingQrScanner` Implementation

```
Services/QrCode/ZXingQrScanner.cs
```

- Add NuGet: `ZXing.Net` (latest stable)
- Decode JPEG → `SKBitmap` or `System.Drawing.Bitmap` → ZXing `BarcodeReader`
- Configure reader: `TryHarder = true`, `PossibleFormats = { BarcodeFormat.QR_CODE }`
- Map ZXing `Result` → `QrScanResult`
- Return `null` if no QR code found

**Image decoding approach:**
- Use `SkiaSharp` (already in MAUI) to decode JPEG to pixel array
- Feed luminance source to ZXing

```csharp
public async Task<QrScanResult?> ScanAsync(byte[] jpegFrame, CancellationToken ct)
{
    return await Task.Run(() =>
    {
        using var bitmap = SKBitmap.Decode(jpegFrame);
        if (bitmap is null) return null;

        var luminanceSource = new SKBitmapLuminanceSource(bitmap);
        var reader = new BarcodeReaderGeneric();
        reader.Options.TryHarder = true;
        reader.Options.PossibleFormats = [BarcodeFormat.QR_CODE];

        var result = reader.Decode(luminanceSource);
        if (result is null) return null;

        return new QrScanResult(result.Text, QrCodeFormat.QrCode, DateTimeOffset.UtcNow);
    }, ct);
}
```

### 1.4 `SKBitmapLuminanceSource`

```
Services/QrCode/SKBitmapLuminanceSource.cs
```

ZXing needs a `LuminanceSource`. Create a thin adapter that reads pixel luminance from `SKBitmap`:

```csharp
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

    protected override LuminanceSource CreateLuminanceSource(byte[] newLuminances, int width, int height)
        => new SKBitmapLuminanceSource(newLuminances, width, height);
}
```

---

## Wave 2: ScanQrCodeTool

### 2.1 `ScanQrCodeTool`

```
Tools/ScanQrCodeTool.cs
```

```csharp
public class ScanQrCodeTool : ToolBase<ScanQrCodeArgs>
{
    private readonly IQrCodeScanner _scanner;

    public ScanQrCodeTool(IQrCodeScanner scanner) => _scanner = scanner;

    public override string Name => "scan_qr_code";
    public override string Description => "Capture a photo and scan for QR codes or barcodes. Returns decoded content.";

    protected override async Task<ToolResult> ExecuteAsync(
        ScanQrCodeArgs args, ToolContext context, CancellationToken ct)
    {
        var frame = await context.CaptureFrame(ct);
        if (frame is null)
            return ToolResult.Fail("Camera not available.");

        var result = await _scanner.ScanAsync(frame, ct);
        if (result is null)
            return ToolResult.Success(new { found = false, message = "No QR code detected in the image" });

        return ToolResult.Success(new
        {
            found = true,
            content = result.Content,
            format = result.Format.ToString(),
        });
    }
}
```

**Wake word binding:**
```csharp
public override WakeWordBinding? WakeWord => new(
    KeywordPath: "wakewords/scan-that.ppn",
    Mode: WakeWordMode.QuickAction,
    InitialPrompt: "Scan for QR codes in front of me and tell me what you find.");
```

### 2.2 Tool Args

```csharp
public record ScanQrCodeArgs(string? Query = null);
```

---

## Wave 3: DI Registration + Integration

### 3.1 ServiceExtensions.cs

```csharp
services.AddSingleton<IQrCodeScanner, ZXingQrScanner>();
services.AddSingleton<ITool, ScanQrCodeTool>();
```

The tool is converted to an `AITool` via `AIFunctionFactory.Create()` in `AgentOrchestrator` and dispatched manually through `RawRepresentation` — the same pattern used by all existing tools.

### 3.2 NuGet Package

Add to `BodyCam.csproj`:
```xml
<PackageReference Include="ZXing.Net" Version="0.16.*" />
```

SkiaSharp is already included via MAUI.

---

## Wave 4: Unit Tests

### 4.1 Test Assets

Create test JPEG images with known QR codes:
- `TestAssets/qr-url.jpg` — QR containing `https://example.com`
- `TestAssets/qr-text.jpg` — QR containing `Hello World`
- `TestAssets/no-qr.jpg` — Photo with no QR code

Generate these programmatically in test setup using ZXing's `BarcodeWriter` + SkiaSharp.

### 4.2 Scanner Tests

```
BodyCam.Tests/Services/ZXingQrScannerTests.cs
```

| Test | Asserts |
|------|---------|
| `ScanAsync_WithQrCode_ReturnsContent` | Content matches encoded text |
| `ScanAsync_WithUrl_ReturnsUrl` | URL decoded correctly |
| `ScanAsync_NoQrCode_ReturnsNull` | Returns null, no exception |
| `ScanAsync_EmptyImage_ReturnsNull` | Handles gracefully |
| `ScanAsync_InvalidJpeg_ReturnsNull` | Doesn't throw on bad data |

### 4.3 Tool Tests

```
BodyCam.Tests/Tools/ScanQrCodeToolTests.cs
```

| Test | Asserts |
|------|---------|
| `Execute_WithQrFrame_ReturnsContent` | JSON contains found=true + content |
| `Execute_NoQrCode_ReturnsNotFound` | JSON contains found=false |
| `Execute_CameraUnavailable_ReturnsError` | JSON contains error message |

---

## Exit Criteria

1. `ZXingQrScanner.ScanAsync` decodes QR codes from JPEG frames
2. `ScanQrCodeTool` captures frame + decodes + returns JSON to AI
3. AI reads content aloud and asks what to do (handled by system prompt + tool result)
4. All unit tests pass
5. Works on both Windows and Android
