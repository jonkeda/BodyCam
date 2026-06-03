# M18 Phase 3 - Barcode Support + Scan History

**Status:** IMPLEMENTED
**Depends on:** M18 Phase 1

---

## Goal

Decode common barcode formats in addition to QR codes and keep a short in-memory scan history for recall.

---

## Current Scanner Formats

`ZXingQrScanner` currently configures:

```csharp
BarcodeFormat.QR_CODE,
BarcodeFormat.EAN_13,
BarcodeFormat.UPC_A,
BarcodeFormat.CODE_128,
BarcodeFormat.DATA_MATRIX
```

Mapping:

| ZXing format | BodyCam format |
|--------------|----------------|
| `QR_CODE` | `QrCodeFormat.QrCode` |
| `EAN_13` | `QrCodeFormat.Ean13` |
| `UPC_A` | `QrCodeFormat.UpcA` |
| `CODE_128` | `QrCodeFormat.Code128` |
| `DATA_MATRIX` | `QrCodeFormat.DataMatrix` |
| anything else | `QrCodeFormat.Unknown` |

---

## `QrCodeService`

```
src/BodyCam/Services/QrCode/QrCodeService.cs
```

Current behavior:

| Capability | Status |
|------------|--------|
| Store scan result | Implemented |
| Max history size | 20 |
| Thread safety | lock-protected list |
| `LastResult` | Implemented |
| `SearchHistory(query)` | Implemented, case-insensitive content search |
| `GetHistory()` | Implemented |
| Persistence | Not implemented |
| Auto-save to `MemoryStore` | Not implemented |

The earlier idea of auto-saving URLs/vCards to memory is not in the current code.

---

## `RecallLastScanTool`

```
src/BodyCam/Tools/RecallLastScanTool.cs
```

Tool metadata:

```json
{
  "name": "recall_last_scan",
  "description": "Recall the most recent QR code or barcode scan result. Use when the user asks 'what was that QR code?' or 'what did we scan?'"
}
```

If history is empty:

```json
{
  "found": false,
  "message": "No previous scan results."
}
```

If history has a result, the tool returns:

```json
{
  "found": true,
  "content": "...",
  "format": "QrCode",
  "content_type": "url",
  "scanned_at": "2026-06-03T...",
  "details": {}
}
```

---

## Product Barcode Lookup

Product lookup is implemented separately from scan history:

```
src/BodyCam/Tools/LookupBarcodeTool.cs
src/BodyCam/Services/Barcode/
```

`LookupBarcodeTool` can either:

1. Use a provided barcode string.
2. Capture a frame and scan for a product barcode.

It accepts EAN-13, UPC-A, and Code 128 as product formats. QR codes are rejected with guidance to use `scan_qr_code`.

`BarcodeLookupService` queries registered clients:

| Client | Source |
|--------|--------|
| `OpenFoodFactsClient` | food products |
| `UpcItemDbClient` | general UPC data |
| `OpenGtinDbClient` | GTIN fallback |

---

## Tests

| Area | Current test files |
|------|--------------------|
| Barcode decode formats | `src/BodyCam.Tests/Services/ZXingQrScannerTests.cs` |
| History behavior | `src/BodyCam.Tests/Services/QrCodeServiceTests.cs` |
| Last scan recall | `src/BodyCam.Tests/Tools/RecallLastScanToolTests.cs` |
| Product lookup | `src/BodyCam.Tests/Tools/LookupBarcodeToolTests.cs`, `src/BodyCam.Tests/Services/BarcodeLookupServiceTests.cs` |

---

## Exit Criteria

1. Scanner decodes QR, EAN-13, UPC-A, Code 128, and Data Matrix.
2. Successful scan commands add results to `QrCodeService`.
3. `recall_last_scan` returns the latest scan with content classification.
4. `lookup_barcode` handles product lookups separately from general QR scans.
