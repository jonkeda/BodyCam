# M18 - QR Code Scanning

**Status:** IMPLEMENTED / DOCS UPDATED TO CURRENT CODE

**Goal:** Scan QR codes and barcodes from the active camera, classify the decoded content, show post-scan actions, and let the AI offer the same actions by voice.

**Depends on:** camera command infrastructure (`CameraManager`, `ICameraCommandService`), QR services, Realtime tool dispatch, and audio output. It does **not** depend on `VisionPipeline` for Phase 5.

---

## Why This Matters

Smart glasses see QR codes everywhere: restaurant menus, product labels, event tickets, WiFi credentials, business cards. BodyCam lets the user trigger a scan from the Actions drawer, a tool call, or a scan wake word, then turns the result into a readable summary and contextual actions.

---

## Current User Flow

```
1. User sees QR code or barcode.
2. User taps Actions -> Scan, or the Realtime model calls scan_qr_code.
3. CameraCommandService executes the "scan" camera command.
4. ScanCommand captures one frame from CameraManager or waits for manual aim.
5. ZXingQrScanner decodes QR/barcode formats from the JPEG.
6. QrContentResolver classifies the content and suggests actions.
7. The transcript and ScanResultOverlay show a summary and action buttons.
8. If the Realtime tool path was used, the AI also announces the result and asks what to do.
```

---

## Current Architecture

```
Actions drawer / Realtime tool / wake word
        |
        v
scan_qr_code tool OR MainViewModel.ScanCommand
        |
        v
CameraCommandService.ExecuteAsync("scan")
        |
        v
ScanCommand
        |
        +--> capture frame via CameraManager / manual capture coordinator
        |
        +--> IQrCodeScanner.ScanAsync(byte[] jpeg)
        |       |
        |       +--> null: found=false, "No QR code or barcode detected."
        |       |
        |       +--> QrScanResult { Content, Format, ScannedAt }
        |
        +--> QrCodeService.Add(result)
        |
        +--> QrContentResolver.Resolve(content)
        |
        +--> CameraCommandResult.Data:
              found, content, format, content_type,
              suggested_actions, details, requires_confirmation
```

### Post-Scan UI Wiring

There are two reliable overlay paths in the current code:

| Trigger | UI path |
|---------|---------|
| Actions drawer `Scan` | `MainViewModel.ExecuteCameraCommandAsync("scan")` -> `TryShowScanResult` -> `ShowScanResultCard` |
| Realtime `scan_qr_code` tool call | `AgentOrchestrator.TryFireScanResult` -> `ScanResultReady` -> `ShowScanResultCard` |

The direct wake-word `InvokeTool` branch executes the tool, but it does not currently raise `ScanResultReady`.

---

## Key Components

| Component | Responsibility |
|-----------|----------------|
| `IQrCodeScanner` | Decode QR/barcode content from JPEG bytes |
| `ZXingQrScanner` | ZXing.Net + SkiaSharp scanner for QR, EAN-13, UPC-A, Code 128, Data Matrix |
| `QrScanResult` | `Content`, `Format`, `ScannedAt` |
| `QrCodeService` | In-memory last-20 scan history |
| `IQrContentHandler` | Content classifier, parser, summary, icon, and suggested actions |
| `QrContentResolver` | Ordered handler resolution with plain-text fallback |
| `ScanCommand` | Reusable camera command for the scan flow |
| `ScanQrCodeTool` | Realtime tool wrapper around `CameraCommandService.ExecuteAsync("scan")` |
| `RecallLastScanTool` | Recalls the most recent scan from history |
| `LookupBarcodeTool` | Product-barcode lookup via external product databases |
| `ScanResultOverlay` | Visual post-scan action card |

---

## Supported Formats

| Format | Status | Use Case |
|--------|--------|----------|
| QR Code | Implemented | URLs, WiFi, vCards, email, phone, text |
| EAN-13 | Implemented | Product barcodes |
| UPC-A | Implemented | Product barcodes |
| Code 128 | Implemented | Shipping/product labels |
| Data Matrix | Implemented | Industrial labels |

---

## Phases

### Phase 1: Core Scanning

Implemented. `IQrCodeScanner`, `QrScanResult`, `SKBitmapLuminanceSource`, and `ZXingQrScanner` exist. `ScanQrCodeTool` now delegates to the camera command layer rather than scanning directly.

### Phase 2: Scan UI

Implemented. The visible entry point is `ActionsDrawerView` with a `ScanButton`; `QuickActionsView` only toggles the Actions drawer. `MainViewModel.ScanCommand` directly executes camera command `"scan"` instead of sending a prompt and relying on tool selection.

### Phase 3: Barcode Support + History

Implemented. ZXing scans QR, EAN-13, UPC-A, Code 128, and Data Matrix. `QrCodeService` stores the last 20 results in memory. `RecallLastScanTool` returns the most recent scan.

### Phase 4: Content-Aware Actions

Implemented. Content handlers classify URL, WiFi, vCard, email, phone, and plain text. Scan results include `content_type`, `details`, and `suggested_actions`.

### Phase 5: Post-Scan UI & Voice Actions

Implemented. `ScanResultOverlay` renders the content summary and action buttons; scan transcript entries include a "Show actions" button. This phase uses `ScanCommand`, `ScanResultReady`, and `MainViewModel.ShowScanResultCard`. `VisionPipeline` is not required.

### Phase 6: Vision Pipeline

Not required for M18 Phase 5. Some `Services/Vision` pipeline classes are present and registered, but current `LookTool` delegates to `CameraCommandService` and `LookCommand`, not to `VisionPipeline`. Treat the phase 6 document as legacy/experimental unless the code is changed to use it.

---

## Tool Definition

```json
{
  "name": "scan_qr_code",
  "description": "Capture a photo and scan for QR codes or barcodes. Returns decoded content with type classification and suggested actions.",
  "parameters": {
    "type": "object",
    "properties": {
      "query": {
        "type": "string",
        "description": "Optional question about the QR code content"
      }
    }
  }
}
```

**Wake word binding:** `wakewords/bodycam-scan_en_windows.ppn` -> `scan_qr_code`.

---

## Integration Points

| System | Integration |
|--------|-------------|
| `CameraManager` | Captures the JPEG frame |
| `IManualCameraCaptureCoordinator` | Used when touch commands resolve to manual aim |
| `CameraCommandService` | Executes `ScanCommand` from UI and tool wrappers |
| `ToolDispatcher` | Dispatches `scan_qr_code`, `recall_last_scan`, and `lookup_barcode` |
| `AgentOrchestrator` | Fires `ScanResultReady` after Realtime `scan_qr_code` tool results |
| `MainViewModel` | Executes scan action, shows overlay, adds transcript entries |
| `ScanResultOverlay` | Displays summary and actions |
| `ISettingsService` | Controls default touch command mode and external-action confirmation |

---

## Current Success Criteria

1. Actions -> Scan captures a frame and returns a transcript result.
2. Successful scans show `ScanResultOverlay` with summary and suggested actions.
3. Realtime `scan_qr_code` tool calls return enriched JSON and raise the overlay event.
4. QR and barcode formats decode through `ZXingQrScanner`.
5. Last-scan recall works through `RecallLastScanTool`.
6. Product barcode lookup works through `LookupBarcodeTool`.
7. No-QR, camera-unavailable, invalid-image, and unsupported-provider paths are handled gracefully.
