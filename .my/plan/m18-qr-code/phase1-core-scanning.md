# M18 Phase 1 - Core QR Code Scanning

**Status:** IMPLEMENTED
**Depends on:** `CameraManager`, `CameraCommandService`, `ZXing.Net`, SkiaSharp

---

## Goal

Capture one camera frame, decode QR/barcode content locally with ZXing.Net, and return structured data that the UI and Realtime AI can use.

---

## Current Implementation

### `IQrCodeScanner`

```
src/BodyCam/Services/QrCode/IQrCodeScanner.cs
```

```csharp
public interface IQrCodeScanner
{
    Task<QrScanResult?> ScanAsync(byte[] jpegFrame, CancellationToken ct = default);
}
```

### `QrScanResult`

```
src/BodyCam/Models/QrScanResult.cs
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

### `ZXingQrScanner`

```
src/BodyCam/Services/QrCode/ZXingQrScanner.cs
```

The scanner decodes JPEG bytes through `SKBitmap.Decode`, wraps the bitmap in `SKBitmapLuminanceSource`, and runs `BarcodeReaderGeneric` with `TryHarder = true`.

Supported ZXing formats:

```csharp
BarcodeFormat.QR_CODE
BarcodeFormat.EAN_13
BarcodeFormat.UPC_A
BarcodeFormat.CODE_128
BarcodeFormat.DATA_MATRIX
```

Behavior:

| Case | Result |
|------|--------|
| Valid supported code | `QrScanResult` with mapped `QrCodeFormat` |
| No code | `null` |
| Invalid/undecodable JPEG | `null` |
| Decode exception | swallowed and returned as `null` |

### `SKBitmapLuminanceSource`

```
src/BodyCam/Services/QrCode/SKBitmapLuminanceSource.cs
```

Thin adapter from Skia pixels to ZXing luminance bytes.

---

## Scan Tool Shape

`ScanQrCodeTool` no longer owns scanner orchestration directly. Current code delegates to the camera command layer:

```
ScanQrCodeTool.ExecuteAsync
    -> CameraCommandService.ExecuteAsync(
           new CameraCommandRequest("scan", ...))
    -> ScanCommand.ExecuteAsync
    -> IQrCodeScanner.ScanAsync
```

```
src/BodyCam/Tools/ScanQrCodeTool.cs
src/BodyCam/Services/Camera/Commands/ScanCommand.cs
```

Tool metadata:

| Property | Current value |
|----------|---------------|
| Name | `scan_qr_code` |
| Description | captures a photo, scans QR/barcodes, returns classification and suggested actions |
| Wake word | `wakewords/bodycam-scan_en_windows.ppn` |
| Initial prompt | `Scan for QR codes in front of me and tell me what you find.` |

---

## DI Registration

```
src/BodyCam/ServiceExtensions.cs
```

```csharp
services.AddSingleton<IQrCodeScanner, ZXingQrScanner>();
services.AddSingleton<QrCodeService>();
services.AddSingleton<ICameraCommand, ScanCommand>();
services.AddSingleton<ITool, ScanQrCodeTool>();
```

`AddQrCodeServices()` registers scanner/history/content handlers. `AddCameraServices()` registers `ScanCommand`. `AddTools()` registers `ScanQrCodeTool`.

---

## Tests

| Area | Current test files |
|------|--------------------|
| Scanner decode and error handling | `src/BodyCam.Tests/Services/ZXingQrScannerTests.cs` |
| Tool wrapper delegation | `src/BodyCam.Tests/Tools/ScanQrCodeToolTests.cs` |
| Camera command scan behavior | `src/BodyCam.Tests/Services/Camera/Commands/ScanCommandTests.cs` |
| Real Realtime scan flow | `src/BodyCam.RealTests/Pipeline/QrCodeScanTests.cs` |

---

## Exit Criteria

1. `ZXingQrScanner.ScanAsync` decodes QR and supported barcode formats from JPEG frames.
2. Bad images and missing codes return `null` without crashing.
3. `ScanQrCodeTool` delegates to the reusable `"scan"` camera command.
4. Successful scans return enriched data for Phase 4 and Phase 5.
