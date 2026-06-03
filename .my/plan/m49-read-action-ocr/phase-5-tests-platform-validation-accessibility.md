# M49 Phase 5 - Tests, Platform Validation, And Accessibility

**Status:** Proposed
**Goal:** Prove the OCR-backed Read action is reliable, testable, and useful for
blind-first use across supported platforms.

## Unit Tests

Update or add focused tests in `src/BodyCam.Tests`.

### OCR Service

Use fakes for command tests. Only plugin mapping tests should reference plugin
types, and those can be thin if direct construction is awkward.

Test cases:

- maps `AllText`, `Lines`, elements, and confidence;
- blank plugin result becomes no-text;
- initialization happens once;
- cancellation is propagated;
- platform/plugin exceptions become readable OCR failures.

### Read Command

Update `src/BodyCam.Tests/Services/Camera/Commands/ReadCommandTests.cs`.

Test cases:

- `Full` preserves OCR text and line breaks;
- `Summary` is shorter than full text;
- `Overview` identifies likely sections or important fields;
- no text returns `No text detected.`;
- OCR error returns a friendly `Read error`;
- text-only AI provider does not block capture;
- optional text AI post-processing never runs for `Full`;
- analytics uses `capability.path = local_ocr`.

### Read Action Wiring

Add/update tests for the action path:

- `AssistiveActionIds.Read` remains `camera.read`;
- the registered Read `CameraAssistiveAction` invokes command ID `read`;
- `ReadTextTool` still invokes command ID `read`;
- no separate user-facing `read-ocr`, `read_ocr`, or `ocr_read` command/tool is
  registered;
- action result data contains OCR metadata from `ReadCommand`.

## Integration And Real Tests

Add real/manual validation cases in `src/BodyCam.RealTests` or a short field
test checklist if automated OCR test images are not stable across platforms.

Minimum validation matrix:

| Platform | Cases |
| --- | --- |
| Windows | Printed document, laptop screen, product label, no-text frame. |
| Android | Printed document, sign/menu, first-run ML Kit model availability, offline after first run. |
| iOS | Printed document and no-text frame, if an iOS build machine/device is available. |
| MacCatalyst | Basic restore/build and one OCR image, if available. |

Use deterministic image fixtures where possible, but expect OCR output to vary
by platform. Assertions should focus on meaningful substrings, line count,
success/failure, and latency buckets rather than exact full text.

## Accessibility Requirements

Read exists for blind and visually impaired users, so output quality matters as
much as extraction.

Acceptance checks:

- full-auto Read works without touching the screen;
- manual aim still supports the existing capture flow;
- long text is chunkable and transcript-friendly;
- low confidence is announced before potentially wrong text;
- safety-critical text such as medication, allergens, warnings, prices, dates,
  and addresses is not presented as certain when OCR confidence is weak;
- "no text" and "move closer / improve lighting" guidance is short and clear;
- `read_text` wake word behavior remains intact.

## Performance Targets

Initial targets:

- local OCR starts within 1 second after frame capture on Windows laptop class
  hardware;
- Android OCR completes within 2 seconds for a single photo after the model is
  available;
- scene-description vision call is skipped when the explicit Read command
  succeeds locally.

These are field targets, not strict unit-test thresholds.

## Release Checklist

- Package restore and app build verified.
- Android manifest includes ML Kit OCR metadata.
- Windows OCR language availability failure is handled.
- Read command no longer requires image-capable provider.
- Read action wiring still points to command ID `read`.
- No separate user-facing OCR action is registered beside Read.
- Tests updated for OCR service, Read command, and Read action wiring.
- Manual validation notes recorded in the phase or realtests log.

## Follow-Up Ideas

- Add optional image preprocessing for small, skewed, or low-contrast text.
- Add language preference from settings into `ReadOcrRequest`.
- Use pattern configs for phone numbers, dates, prices, medication strengths,
  or addresses.
- Add a transcript action to continue reading long OCR results from where speech
  stopped.
