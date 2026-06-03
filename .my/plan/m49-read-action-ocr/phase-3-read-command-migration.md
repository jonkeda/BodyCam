# M49 Phase 3 - Read Command Migration

**Status:** Proposed
**Goal:** Rework `ReadCommand` so OCR extraction uses `IReadOcrService` instead
of `VisionAgent`, while preserving the existing command and tool contract.

## Current Behavior

`src/BodyCam/Services/Camera/Commands/ReadCommand.cs` currently:

- depends on `VisionAgent`;
- checks active provider image capability before capture;
- captures one frame;
- sends the frame and a text-extraction prompt to the vision model;
- returns model text or "No text detected."

M49 keeps the public command ID and options:

- `Id = "read"`
- `ToolName = "read_text"`
- `ReadCommandOptions.DetailLevel`
- `ReadCommandOptions.Focus`
- `ReadDetailLevel.Summary`
- `ReadDetailLevel.Overview`
- `ReadDetailLevel.Full`

## Target Behavior

`ReadCommand` should:

1. Resolve options exactly as it does today.
2. Capture a frame based on full-auto or manual-aim mode.
3. Call `IReadOcrService.RecognizeAsync`.
4. Format `Full` locally from OCR lines.
5. Format `Summary` and `Overview` locally first.
6. Optionally use text-only AI post-processing when available and enabled.
7. Return a `CameraCommandResult` with OCR metadata.

The command should no longer fail just because the active provider lacks vision
or image input capability.

## Constructor And Dependencies

Replace the hard dependency on `VisionAgent` with OCR-specific dependencies.

Suggested shape:

```csharp
public sealed class ReadCommand : CameraCommandBase<ReadCommandOptions>, ICommandPromptProvider
{
    private readonly IReadOcrService _ocr;
    private readonly ReadOcrFormatter _formatter;
    private readonly IAiProviderRegistry _providerRegistry;
    private readonly IAnalyticsService _analytics;
}
```

Keep test constructors simple. Existing tests currently create a `VisionAgent`;
those tests should move to fake OCR results.

## Result Data

Return structured OCR data for debugging, future UI overlays, and tests:

```csharp
data["capability_path"] = "local_ocr";
data["detail_level"] = options.DetailLevel?.ToString();
data["ocr_text"] = ocr.AllText;
data["ocr_line_count"] = ocr.Lines.Count;
data["ocr_average_confidence"] = ocr.AverageConfidence;
data["postprocess_path"] = "local" | "text_ai" | "none";
```

If bounding boxes are not too large, include a compact `ocr_elements` list. If
results are large, store only counts and confidence in the command result and
leave full element output for diagnostics.

## Detail-Level Formatting

### Full

Use OCR lines directly. Preserve line breaks when they help comprehension. If
the OCR engine returns only `AllText`, fall back to it.

### Overview

Produce a short explanation from OCR text:

- likely type: sign, menu, label, document, receipt, screen, form, or unknown;
- important sections or headings;
- dates, prices, warnings, contact info, URLs, or medication/allergen language;
- uncertainty when confidence is low or text is fragmented.

### Summary

Keep this brief. Use high-signal lines first and avoid reading the whole text.

### Optional AI Post-Processing

For `Summary` and `Overview`, a text-only AI post-processor may turn OCR text
into a better spoken answer. It must:

- use OCR text only, not the original image;
- require only text/chat provider capability;
- preserve a local formatting fallback;
- never invent unread OCR content.

Do not add this optional path until local formatting is passing tests.

## Error Handling

| Case | Transcript |
| --- | --- |
| Camera unavailable | Existing camera unavailable result. |
| OCR initialized but no text found | `No text detected.` |
| OCR unsupported on platform | `Read is not available on this device yet.` |
| OCR plugin/platform failure | `Read error: OCR failed. Try better lighting or move closer.` |
| Low confidence or fragmented text | Include a confidence warning before the text. |

Cancellation should keep current behavior and rethrow when requested.

## Analytics

Update the existing analytics event instead of adding a new one:

- `command = read`
- `capability.path = local_ocr`
- `provider.id = local` unless optional text AI post-processing runs
- `fallback.path = none`, `local_format`, or `text_ai`
- `result = success | error`
- `error.category = no_text | ocr_unavailable | ocr_error | camera_unavailable`

## Acceptance

- `ReadCommand` compiles without `VisionAgent`.
- `read_text` still delegates to the command without API changes.
- A text-only AI provider no longer blocks OCR capture and extraction.
- `Full`, `Overview`, and `Summary` return distinct transcript formats.
- Existing no-text behavior remains friendly.
- Tests cover focus hints, detail levels, OCR errors, no-text output, and the
  "text-only provider still reads" path.
