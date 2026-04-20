# M18 Phase 3 — Barcode Support + Scan History

**Status:** NOT STARTED  
**Depends on:** M18 Phase 1

---

## Goal

Extend the scanner to support common barcode formats (EAN-13, UPC-A, Code 128, Data Matrix). Add a scan history service so the user can recall previous scans by voice.

---

## Changes

### 1. Extend ZXingQrScanner

Add barcode formats to the reader configuration:

```csharp
reader.Options.PossibleFormats = [
    BarcodeFormat.QR_CODE,
    BarcodeFormat.EAN_13,
    BarcodeFormat.UPC_A,
    BarcodeFormat.CODE_128,
    BarcodeFormat.DATA_MATRIX
];
```

Map ZXing `BarcodeFormat` → `QrCodeFormat` enum (already defined in Phase 1).

### 2. QrCodeService

```
Services/QrCode/QrCodeService.cs
```

- Maintains a circular buffer of last 20 scan results
- Provides `LastResult` property
- Provides `SearchHistory(query)` — substring match on content
- Auto-saves URL and vCard scans to `MemoryStore` (category: "scans")

### 3. RecallLastScanTool

```
Tools/RecallLastScanTool.cs
```

Realtime API tool: "what was that QR code?" → returns last scan result from `QrCodeService`.

```json
{
  "name": "recall_last_scan",
  "description": "Recall the most recent QR code or barcode scan result.",
  "parameters": { "type": "object", "properties": {} }
}
```

### 4. Tests

| Test | File |
|------|------|
| Decode EAN-13 barcode | `ZXingQrScannerTests.cs` |
| Decode Code 128 barcode | `ZXingQrScannerTests.cs` |
| History stores last 20 | `QrCodeServiceTests.cs` |
| History search by content | `QrCodeServiceTests.cs` |
| RecallLastScan returns last | `RecallLastScanToolTests.cs` |
| RecallLastScan empty history | `RecallLastScanToolTests.cs` |

---

## Exit Criteria

1. Scanner decodes EAN-13, UPC-A, Code 128, Data Matrix
2. Scan history persists across scans within a session
3. User can ask "what was that barcode?" and get the answer
