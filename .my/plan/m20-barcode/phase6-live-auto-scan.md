# M20 Phase 6 - Live Auto Scan

**Status:** NOT STARTED
**Depends on:** M18 camera preview/decoding, M20 Phase 5 product UI, M44 Scan command design

---

## Goal

Make the camera actively look for QR codes and barcodes while the preview is
open. When a valid code is detected with enough confidence, the app should run
the same scan/product lookup workflow automatically instead of waiting for the
user to tap the scan button.

The scan button remains useful as an explicit command, but it should not be the
only way a visible code can be scanned.

---

## User Flow

```
Camera preview is visible
        |
        v
App samples preview frames at a controlled rate
        |
        v
ZXing scans each sampled frame for QR/barcode content
        |
        +--> no code:
        |       keep preview running
        |
        +--> code detected once:
        |       show subtle scanning feedback, keep confirming
        |
        +--> same code detected consistently:
                run scan/product workflow automatically
                add result to transcript
                pause duplicate scans briefly
```

---

## Behavior

The camera should continuously search for supported codes while live preview is
active:

- QR codes
- EAN-13
- UPC-A
- Code 128
- other barcode formats already supported by the shared decoder

Detection should reuse the existing decoder abstraction, for example
`IQrCodeScanner`, rather than adding a separate scanning pipeline.

Automatic scanning should trigger when:

1. A code is visible in the camera frame.
2. The decoded text/value is non-empty.
3. The same code is detected across a small stability window, for example 2 out
   of 3 sampled frames.
4. No scan workflow is already running.
5. The same value has not just been handled inside the duplicate cooldown.

Suggested defaults:

| Setting | Initial value |
|---------|---------------|
| Frame sample rate | 3-5 frames per second |
| Stability threshold | same value in 2 frames |
| Duplicate cooldown | 5 seconds |
| Max concurrent scan workflows | 1 |

---

## Scan Routing

Detected content should route through the same command/workflow paths as manual
scan actions.

| Detected format/content | Automatic action |
|-------------------------|------------------|
| Product barcode | Run product barcode lookup workflow |
| QR URL | Add scan result and ask before opening |
| QR plain text | Add decoded content to transcript |
| Wi-Fi/contact/SMS/email/map QR | Add result and require confirmation for external action |
| Unknown barcode | Add raw code and format to transcript |

Product barcode auto-scan should reuse:

```
IProductBarcodeLookupWorkflow
BarcodeLookupService
ProductInfo
ProductDetailPage
```

Generic QR/barcode auto-scan should align with the M44 `ScanCommand` direction:
decoding, content classification, action suggestions, and confirmation belong in
the registered scan command pipeline.

---

## UX Requirements

The camera should make it clear that it is looking for a code without requiring
the user to understand implementation details.

Recommended UI states:

| State | UI behavior |
|-------|-------------|
| Searching | Normal preview, optional small scan indicator |
| Candidate detected | Brief visual feedback around the preview or scan button |
| Handling result | Disable duplicate auto-trigger, show compact progress |
| Result found | Transcript receives the same result shape as manual scan |
| No result | Continue searching without noisy transcript entries |

Avoid adding a transcript entry for every failed frame. Only record meaningful
results, errors from an invoked workflow, or an explicit user action.

Manual scan button behavior:

- Still captures/scans immediately when tapped.
- Can bypass the stability window.
- Should cancel or ignore any pending auto candidate for the same frame/value.

---

## Safety

Auto-detection may decode content automatically, but it must not perform unsafe
external actions automatically.

- Never silently open a URL.
- Never silently call, email, text, join Wi-Fi, or open maps.
- Product lookup may run automatically only if the app setting allows online
  product lookup from scans.
- If network lookup requires consent, auto-scan should stop at the decoded
  barcode and ask before lookup.
- Keep raw decoded content available in the transcript for audit/debugging.

---

## Implementation Notes

Add a small live scanning coordinator near the camera preview layer, for example:

```
Services/Barcode/ILiveBarcodeScanner.cs
Services/Barcode/LiveBarcodeScanner.cs
```

Suggested responsibilities:

1. Subscribe to preview frame availability.
2. Throttle frame sampling.
3. Decode sampled frames through the shared scanner.
4. Stabilize candidate values across frames.
5. Raise one confirmed detection event.
6. Suppress duplicates during cooldown.

Example shape:

```csharp
public interface ILiveBarcodeScanner
{
    IAsyncEnumerable<LiveBarcodeDetection> WatchAsync(
        ICameraFrameSource frameSource,
        LiveBarcodeScannerOptions options,
        CancellationToken ct);
}

public record LiveBarcodeDetection(
    string Value,
    string Format,
    DateTimeOffset DetectedAt);
```

The view model or scan command host should own routing the confirmed detection
into product lookup or generic scan classification. `LiveBarcodeScanner` should
not know about transcript UI or product pages.

---

## Settings

Add user-facing settings only if the app already has a suitable settings
surface:

| Setting | Default |
|---------|---------|
| Auto-scan visible QR/barcodes | On |
| Auto-lookup product barcodes online | Follow existing product lookup consent |
| Haptic/audio feedback on detection | Follow existing feedback preferences |

If no settings surface exists yet, keep the behavior behind an internal option
so tests can enable/disable it deterministically.

---

## Tests

| Test | File |
|------|------|
| Live scanner emits detection after stable repeated value | `LiveBarcodeScannerTests.cs` |
| Live scanner does not emit for single-frame noise | `LiveBarcodeScannerTests.cs` |
| Live scanner throttles frame decoding | `LiveBarcodeScannerTests.cs` |
| Live scanner suppresses duplicate value during cooldown | `LiveBarcodeScannerTests.cs` |
| Auto product barcode routes to product lookup workflow | `MainViewModelAutoScanTests.cs` |
| Auto QR URL creates confirmation action, not browser launch | `ScanCommandAutoDetectionTests.cs` |
| Manual scan still works while auto-scan is enabled | `MainViewModelAutoScanTests.cs` |
| Failed/no-code frames do not create transcript spam | `MainViewModelAutoScanTests.cs` |

---

## Exit Criteria

1. Camera preview actively scans frames for QR codes and barcodes.
2. A visible supported code can be detected without tapping the scan button.
3. Confirmed detections route through the same product/generic scan workflows as
   manual scan.
4. Duplicate detections are suppressed while the same code remains in view.
5. Unsafe external actions still require confirmation.
6. Manual scan remains available and responsive.
7. Tests cover stability, throttling, duplicate suppression, and routing.
