# M18 — QR Code Scanning

**Status:** PLANNING  
**Goal:** Scan QR codes from the camera feed on demand, read the content aloud via AI, then ask the user what to do with it (open URL, save to memory, etc.).

**Depends on:** M11 (camera abstraction), M3 (vision pipeline), M13 (audio output).

---

## Why This Matters

Smart glasses see QR codes everywhere — restaurant menus, product labels, event tickets, WiFi credentials, business cards. Today the user has to pull out their phone, open a camera app, and squint at the screen. With BodyCam, a button tap or voice command ("scan that") captures the frame, decodes any QR/barcode, reads the content aloud, and asks what to do next.

This is a concrete, high-value "glasses moment" — the kind of thing that makes smart glasses feel useful in daily life.

---

## User Flow

```
1. User sees QR code on a menu, package, poster, etc.
2. User says "scan that" or taps the Scan button
3. BodyCam captures a frame via CameraManager
4. QR decoder extracts content from the frame
5. AI reads content aloud: "I found a QR code with a URL: example.com/menu"
6. AI asks: "Would you like me to open it, save it, or ignore it?"
7. User responds by voice → action is taken
```

---

## Architecture

```
User trigger (button/voice)
     │
     ▼
CameraManager.CaptureFrameAsync()
     │
     ▼
IQrCodeScanner.ScanAsync(byte[] jpeg)
     │
     ├─ Found → QrScanResult { Content, Format, BoundingBox }
     │            │
     │            ▼
     │       AI announces content + asks what to do
     │            │
     │            ▼
     │       User responds → action dispatched
     │
     └─ Not found → AI says "I didn't find a QR code"
```

### Key Components

| Component | Responsibility |
|-----------|---------------|
| `IQrCodeScanner` | Decode QR/barcode from JPEG frame |
| `QrScanResult` | Content string, barcode format, confidence |
| `ScanQrCodeTool` | Realtime API function-call tool (capture + decode + announce) |
| `QrCodeService` | Orchestrates scan flow, manages result history |

### Library Options

| Library | Platform | License | Notes |
|---------|----------|---------|-------|
| **ZXing.Net** | All (pure .NET) | Apache 2.0 | Mature, wide format support, no native deps |
| **ZXing.Net.Maui** | MAUI | MIT | MAUI-specific wrapper, camera integration |
| **IronBarcode** | All | Commercial | Not suitable (license cost) |
| **SkiaSharp + ZXing** | All | Apache 2.0 | If we already have SkiaSharp for image processing |

**Recommendation:** `ZXing.Net` (pure .NET, no platform dependencies). We already capture JPEG frames through `CameraManager` — we just need the decoder, not a camera view.

---

## Supported Formats

| Format | Priority | Use Case |
|--------|----------|----------|
| QR Code | P0 | URLs, WiFi, vCards, text |
| EAN-13 / UPC-A | P1 | Product barcodes |
| Code 128 | P2 | Shipping labels |
| Data Matrix | P2 | Industrial labels |

Phase 1 focuses on QR codes only. Barcode support is Phase 2.

---

## Phases

### Phase 1: Core QR Scanning

- `IQrCodeScanner` interface + `ZXingQrScanner` implementation
- `QrScanResult` model (content, format, raw bytes)
- `ScanQrCodeTool` (ITool) — captures frame, decodes, returns content to AI
- AI reads content aloud and asks what to do
- Unit tests (decode from test images)
- DI registration

### Phase 2: Barcode Support + History

- Extend scanner for EAN-13, UPC-A, Code 128, Data Matrix
- `QrCodeService` with scan history (last N results)
- `RecallLastScanTool` — "what was that QR code again?"
- Save-to-memory integration (auto-save scanned URLs/contacts)

### Phase 3: Content-Aware Actions

- URL detection → offer to open in browser
- WiFi QR → offer to connect to network
- vCard → offer to save contact
- Plain text → offer to save to memory
- Action dispatch through AI conversation (user chooses by voice)

### Phase 4: iOS Platform Support

- Verify ZXing.Net works on iOS (.NET AOT)
- Test with iOS camera provider
- Platform-specific permission handling if needed

---

## Tool Definition

```json
{
  "name": "scan_qr_code",
  "description": "Capture a photo and scan it for QR codes or barcodes. Returns the decoded content.",
  "parameters": {
    "type": "object",
    "properties": {
      "query": {
        "type": "string",
        "description": "Optional: what the user is looking for (e.g., 'menu', 'WiFi password')"
      }
    }
  }
}
```

**Wake word binding:** `"scan that"` → QuickAction → `scan_qr_code`

---

## Integration Points

| System | Integration |
|--------|-------------|
| **CameraManager** | `CaptureFrameAsync()` for frame capture |
| **ToolDispatcher** | Register `ScanQrCodeTool` as function call |
| **AgentOrchestrator** | AI reads result aloud, asks for action |
| **MemoryStore** | Optional save of scanned content |
| **ButtonInputManager** | Map gesture to scan action |
| **WakeWordService** | "scan that" keyword binding |

---

## Success Criteria

1. User says "scan that" or taps button → QR code content read aloud within 2 seconds
2. AI asks what to do with the content → user responds by voice
3. Works with phone camera and glasses camera
4. Handles "no QR found" gracefully
5. Unit tests decode QR from test JPEG images
