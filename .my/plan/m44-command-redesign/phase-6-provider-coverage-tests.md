# M44 Phase 6 - Provider Coverage And Tests

Goal: prove the command redesign works across camera providers and trigger
origins.

## Provider Coverage

Run against:

- phone camera;
- glasses camera;
- bodycam camera;
- USB camera;
- A9 / Wi-Fi camera where available;
- test camera provider.

Commands should depend on provider capabilities, not provider IDs.

Suggested camera capability metadata:

```csharp
public sealed record CameraProviderCapabilities(
    bool SupportsStillCapture,
    bool SupportsLivePreview,
    bool SupportsFrameStream,
    bool IsWearable,
    bool IsHandsFreePreferred,
    TimeSpan TypicalCaptureLatency);
```

Manual aim requires live preview. If preview is unavailable, fall back to a
guided full-auto capture and explain why.

## Unit Tests

Cover:

- command registry lookup by id and optional tool name;
- command service delegation without Look/Read/Scan switch cases;
- default mode by trigger origin;
- explicit manual override;
- detail-level prompt selection;
- Read focus hints;
- Scan content classification;
- scan confirmation rules;
- provider with capture but no preview;
- provider with preview and capture;
- failures when camera is unavailable.

## Brinell / UI Tests

Add focused tests for:

- Actions drawer opens command list.
- Look manual opens preview and waits for capture.
- Look full-auto does not wait for preview.
- Read detail level persists.
- Scan URL asks before opening.
- Screen-reader accessible names exist for action controls.

## Real Hardware Tests

Create a checklist for:

- phone camera direct use;
- glasses button full-auto Look;
- bodycam / USB manual aim;
- Scan on QR and product barcode;
- Read on sign, document, and screen;
- blind-first flow with screen reader enabled.

## Acceptance

- All command defaults are deterministic.
- Full-auto commands do not accidentally block on preview.
- Manual commands do not accidentally capture before the user presses capture.
- Look, Read, and Scan are covered as separate command classes.
- Provider coverage does not require provider ID switch logic.
- Command execution does not require command-kind switch logic.
- Test artifacts show command request, resolved mode, provider, and result.
