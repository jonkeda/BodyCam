# M18 Phase 6 — Cascading Vision Pipeline

**Status:** PLANNING  
**Depends on:** M18 Phase 1–5 (QR scanning), M3 (vision pipeline)

---

## Problem

Today, the Realtime LLM decides which tool to call: `scan_qr_code`, `read_text`, or `describe_scene`. This is unreliable — the model frequently picks `describe_scene` when the user says "scan that" or picks `read_text` when there's a QR code in view. The user doesn't know (or care) which tool is right; they just want to **look**.

The fast, local operations (QR decode, text detection) should always run first. If they find something concrete, return immediately — no expensive LLM vision call needed. The LLM vision call is the fallback for everything else.

---

## Design: `look` as a Cascading Pipeline

Replace the current three-way LLM routing with a single `look` tool that cascades through stages:

```
User: "Look at that" / "What's that?" / "Scan that" / wake word "look"
                    │
                    ▼
            ┌── Stage A ──┐
            │  QR / Barcode │  ← ZXing, ~10ms, fully local
            │  scan_qr_code │
            └──────┬───────┘
                   │
            found? ─┤
            yes ────┤──► return QR result + content type + actions
                    │
                    ▼
            ┌── Stage B ──┐
            │  Text / OCR  │  ← local or lightweight model, ~50ms
            │  read_text   │
            └──────┬───────┘
                   │
            found? ─┤
            yes ────┤──► return extracted text
                    │
                    ▼
            ┌── Stage C ──┐
            │  LLM Vision  │  ← full vision model, ~1-3s, API call
            │describe_scene│
            └──────┬───────┘
                   │
                   ▼
              return scene description
```

### Key Principles

1. **Cheapest first.** QR scan is ~10ms local CPU. Text OCR is fast. LLM vision is 1–3s and costs money. Always cascade in cost order.
2. **First hit wins.** If Stage A finds a QR code, skip B and C entirely. The user gets an instant answer.
3. **Explicit tools still exist.** `scan_qr_code` and `read_text` remain as standalone tools for when the user is specific ("scan the barcode", "read that sign"). The pipeline tool is the default for vague look commands.
4. **Single frame capture.** One `CaptureFrame` call feeds all stages — no redundant camera access.
5. **Extensible.** New stages (object detection, landmark recognition, etc.) slot in as pipeline steps.

---

## Architecture

### `IVisionPipelineStage`

```csharp
public interface IVisionPipelineStage
{
    /// <summary>
    /// Display name for logging/debug ("QR Scan", "Text Detection", "Vision").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Approximate cost tier for ordering. Lower runs first.
    /// 0 = free/local, 10 = lightweight API, 100 = full LLM vision.
    /// </summary>
    int Cost { get; }

    /// <summary>
    /// Attempt to extract information from the frame.
    /// Returns null if this stage found nothing relevant.
    /// </summary>
    Task<VisionPipelineResult?> ProcessAsync(
        byte[] jpegFrame, string? query, CancellationToken ct);
}
```

### `VisionPipelineResult`

```csharp
public record VisionPipelineResult(
    string StageName,
    string Summary,
    Dictionary<string, object> Details);
```

### `VisionPipeline`

```csharp
public class VisionPipeline
{
    private readonly IReadOnlyList<IVisionPipelineStage> _stages;

    public VisionPipeline(IEnumerable<IVisionPipelineStage> stages)
    {
        // Sort by cost ascending — cheapest stages run first
        _stages = stages.OrderBy(s => s.Cost).ToList();
    }

    public async Task<VisionPipelineResult> ExecuteAsync(
        byte[] jpegFrame, string? query, CancellationToken ct)
    {
        foreach (var stage in _stages)
        {
            var result = await stage.ProcessAsync(jpegFrame, query, ct);
            if (result is not null)
                return result;
        }

        // Should never reach here if LLM vision is the last stage
        // (it always produces a result)
        return new VisionPipelineResult("fallback", "Unable to analyze the image.", new());
    }
}
```

### `LookTool`

Replaces the current `describe_scene` as the default look/vision tool. Keeps the same wake word binding (`bodycam-look`).

```csharp
public class LookTool : ToolBase<LookArgs>
{
    private readonly VisionPipeline _pipeline;

    public override string Name => "look";
    public override string Description =>
        "Look at what the camera sees. Automatically scans for QR codes, " +
        "reads text, and describes the scene — returning the first useful result.";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "wakewords/bodycam-look_en_windows.ppn",
        Mode = WakeWordMode.QuickAction,
        InitialPrompt = "Look at what's in front of me."
    };

    protected override async Task<ToolResult> ExecuteAsync(
        LookArgs args, ToolContext context, CancellationToken ct)
    {
        var frame = await context.CaptureFrame(ct);
        if (frame is null)
            return ToolResult.Fail("Camera not available.");

        var result = await _pipeline.ExecuteAsync(frame, args.Query, ct);
        return ToolResult.Success(result);
    }
}
```

---

## Pipeline Stages (Implementation Plan)

### Stage A: QR / Barcode Scan

| Property | Value |
|----------|-------|
| Name | `QrScanStage` |
| Cost | `0` |
| Implementation | Reuses existing `IQrCodeScanner` + `QrContentResolver` |
| Returns | QR content, format, content type, suggested actions |
| Returns null when | No QR/barcode detected |

```csharp
public class QrScanStage : IVisionPipelineStage
{
    private readonly IQrCodeScanner _scanner;
    private readonly QrCodeService _history;
    private readonly QrContentResolver _resolver;

    public string Name => "QR Scan";
    public int Cost => 0;

    public async Task<VisionPipelineResult?> ProcessAsync(
        byte[] jpegFrame, string? query, CancellationToken ct)
    {
        var scan = await _scanner.ScanAsync(jpegFrame, ct);
        if (scan is null) return null;

        _history.Add(scan);
        var handler = _resolver.Resolve(scan.Content);
        var parsed = handler.Parse(scan.Content);

        return new VisionPipelineResult("QR Scan", handler.Summarize(parsed), new()
        {
            ["found_type"] = "qr_barcode",
            ["content"] = scan.Content,
            ["format"] = scan.Format.ToString(),
            ["content_type"] = handler.ContentType,
            ["suggested_actions"] = handler.SuggestedActions,
            ["details"] = parsed,
        });
    }
}
```

### Stage B: Text Detection

| Property | Value |
|----------|-------|
| Name | `TextDetectionStage` |
| Cost | `10` |
| Implementation | TBD — options below |
| Returns | Extracted text and approximate region |
| Returns null when | No meaningful text detected |

**Implementation options (pick one):**

| Option | Pros | Cons |
|--------|------|------|
| **Tesseract OCR (local)** | Fully offline, ~100ms, free | Large binary (~30MB), lower accuracy on photos |
| **Platform OCR** | iOS: Vision framework; Android: ML Kit; Windows: Windows.Media.Ocr | No cross-platform API, platform-specific code |
| **Lightweight LLM** | "Extract all visible text only, no description" prompt to a fast model | Still an API call, but cheaper than full vision |
| **VisionAgent with text-only prompt** | Reuse existing infrastructure, just change the prompt | Same cost as Stage C if same model |

**Recommended:** Use a **separate cheap model** (e.g., `gpt-4o-mini`) for text extraction. This keeps Stage B meaningfully cheaper than Stage C. Replace with local OCR later if latency matters.

The key distinction from Stage C: Stage B asks *only* for text extraction. It returns null if the response indicates no readable text was found (e.g., response is "No text visible" or similar). Stage C does full scene description.

```csharp
public class TextDetectionStage : IVisionPipelineStage
{
    private readonly VisionAgent _vision;

    public string Name => "Text Detection";
    public int Cost => 10;

    public async Task<VisionPipelineResult?> ProcessAsync(
        byte[] jpegFrame, string? query, CancellationToken ct)
    {
        var prompt = "Extract all visible text from this image. " +
                     "Return ONLY the text you can read, nothing else. " +
                     "If no text is visible, respond with exactly: NO_TEXT";

        var text = await _vision.DescribeFrameAsync(jpegFrame, prompt, ct);

        if (string.IsNullOrWhiteSpace(text)
            || text.Contains("NO_TEXT", StringComparison.OrdinalIgnoreCase)
            || text.Contains("no text", StringComparison.OrdinalIgnoreCase))
            return null;

        return new VisionPipelineResult("Text Detection", text, new()
        {
            ["found_type"] = "text",
            ["text"] = text,
        });
    }
}
```

**Open question:** Should Stage B always run, or should it be skipped when the user's prompt clearly isn't about text? The `query` parameter could be used for intent filtering — e.g., if the user said "what color is that car", skip text detection. For now, always run it; the pipeline is still fast since QR scan is ~10ms and Stage B with an LLM is ~500ms.

### Stage C: LLM Vision (Full Scene Description)

| Property | Value |
|----------|-------|
| Name | `SceneDescriptionStage` |
| Cost | `100` |
| Implementation | Reuses existing `VisionAgent.DescribeFrameAsync` |
| Returns | Always returns a result (never null) |

```csharp
public class SceneDescriptionStage : IVisionPipelineStage
{
    private readonly VisionAgent _vision;

    public string Name => "Scene Description";
    public int Cost => 100;

    public async Task<VisionPipelineResult?> ProcessAsync(
        byte[] jpegFrame, string? query, CancellationToken ct)
    {
        var description = await _vision.DescribeFrameAsync(jpegFrame, query, ct);

        return new VisionPipelineResult("Scene Description", description, new()
        {
            ["found_type"] = "scene",
            ["description"] = description,
        });
    }
}
```

---

## What Changes

| Before | After |
|--------|-------|
| LLM decides: `scan_qr_code` vs `read_text` vs `describe_scene` | Single `look` tool cascades all three |
| QR only scanned when LLM picks `scan_qr_code` (unreliable) | QR always scanned first (deterministic, ~10ms) |
| Text only read when LLM picks `read_text` | Text always checked before full vision |
| 3 separate wake words (look, scan, read) | `look` wake word triggers the pipeline; `scan` and `read` remain for explicit requests |
| System instructions must guide tool choice | Pipeline handles routing automatically |

### Tools After This Phase

| Tool | Purpose | Triggers |
|------|---------|----------|
| `look` | **NEW** — cascading pipeline (QR → text → vision) | Wake word "look", vague prompts like "what's that?" / "scan that" |
| `scan_qr_code` | Explicit QR/barcode scan only | "Scan the barcode", wake word "scan" |
| `read_text` | Explicit text reading only | "Read that sign", wake word "read" |
| `describe_scene` | **ENHANCED** — structured scene analysis (description + text + QR locations) | "Describe the scene", "What's going on here?" |
| `recall_last_scan` | Recall previous QR scan | "What was that QR code?" |

### `describe_scene` Structured Output

`describe_scene` stays as a separate tool but returns structured data instead of a plain string. This is for when the user wants a *holistic scene analysis* — not "what's that one thing" but "tell me everything about what you see".

If someone asks "describe the scene" and there's a QR code in the background, they want to hear about the scene — not just the QR code content. That's why `describe_scene` and `look` coexist:

- **`look`** = "identify the most important/actionable thing" (cascade, first hit wins)
- **`describe_scene`** = "tell me about the whole scene" (comprehensive, structured)

```csharp
// Structured response from describe_scene
public record SceneAnalysis
{
    public required string Description { get; init; }
    public string? ExtractedText { get; init; }
    public IReadOnlyList<DetectedCode>? DetectedCodes { get; init; }
    // Future: DetectedObjects, Landmarks, etc.
}

public record DetectedCode(
    string Format,     // "QR_CODE", "EAN_13", etc.
    string? Location); // "top-right", "center", etc. (from LLM description)
```

`describe_scene` would ask the vision model a structured prompt:

```
Analyze this image and respond in JSON:
{
  "description": "A concise scene description in 1-3 sentences.",
  "text": "Any readable text in the image, or null if none.",
  "codes": [{"format": "QR_CODE", "location": "bottom-left"}] or null
}
```

The detected code locations enable a follow-up: "Scan that QR code you spotted" → the user can act on it without `look` having short-circuited past the scene description.

---

## DI Registration

```csharp
public static IServiceCollection AddVisionPipeline(this IServiceCollection services)
{
    services.AddSingleton<IVisionPipelineStage, QrScanStage>();
    services.AddSingleton<IVisionPipelineStage, TextDetectionStage>();
    services.AddSingleton<IVisionPipelineStage, SceneDescriptionStage>();
    services.AddSingleton<VisionPipeline>();
    services.AddSingleton<ITool, LookTool>();
    return services;
}
```

---

## System Instructions Update

Remove the per-tool routing hints. Add:

```
- When the user asks to look at something, see something, or asks "what's that?" 
  or "scan that", use the look tool. It automatically checks for QR codes, reads 
  text, and describes the scene — returning the first useful result.
- When the user asks to describe or analyze the overall scene ("describe the scene",
  "what's going on here?"), use describe_scene for a comprehensive structured analysis.
- Use scan_qr_code only when the user explicitly asks to scan a barcode.
- Use read_text only when the user explicitly asks to read specific text.
```

---

## Future Pipeline Stages (Not In Scope)

These could slot in later between text detection and full vision:

| Stage | Cost | What It Does |
|-------|------|-------------|
| Object Detection | `20` | YOLO/local model — "3 people, 2 cars, 1 dog" |
| Landmark Recognition | `30` | Match against known landmarks database |
| Product Lookup | `15` | UPC/EAN → product database API |
| Face Recognition | `25` | Known faces from user's contact photos |
| Color/Material | `5` | Simple color histogram analysis |

The `IVisionPipelineStage` interface + cost-based ordering makes this trivial to extend.

---

## Test Strategy

### Unit Tests (`BodyCam.Tests`)

- `VisionPipelineTests` — pipeline executes stages in cost order
- `VisionPipelineTests` — first non-null result wins
- `VisionPipelineTests` — fallback to scene description when nothing found
- `QrScanStageTests` — returns result when QR found, null when not
- `TextDetectionStageTests` — returns null on "NO_TEXT" response
- `SceneDescriptionStageTests` — always returns result
- `LookToolTests` — integrates pipeline with ToolContext

### Real Tests (`BodyCam.RealTests`)

- `LookWithQrCode` — look at frame with QR code → returns QR result, no vision API call
- `LookWithText` — look at frame with text → returns extracted text
- `LookWithScene` — look at frame with neither → returns scene description
- `LookPipelinePerformance` — QR stage completes in < 50ms

---

## Resolved Decisions

1. **Stage B uses a separate cheap model.** Use `gpt-4o-mini` (or equivalent) for text extraction — different from the full vision model used in Stage C. This keeps Stage B meaningfully cheaper.

2. **`describe_scene` is kept and enhanced.** It returns structured data: scene description + extracted text + detected code locations. This serves a different intent than `look` — comprehensive scene analysis vs. "identify the one actionable thing". If a user says "describe the scene" and there's a QR code in the background, they get the full scene description (with the QR code noted as a detail), not just the QR content.

3. **No parallel execution.** Stages run sequentially in cost order. Stage A is ~10ms so parallelizing with B adds complexity for negligible gain.

4. **`ScanResultReady` fires from `look` too.** When Stage A (QR scan) produces a result through the `look` tool, the orchestrator fires the `ScanResultReady` event for the UI overlay, same as when `scan_qr_code` is called directly.
