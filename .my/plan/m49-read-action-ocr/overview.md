# M49 - Read Action OCR

**Status:** Proposed
**Goal:** Move the existing Read action from vision-model OCR to native
on-device OCR with `Plugin.Maui.OCR`, while preserving the existing
`camera.read` assistive action, `read` command, `read_text` tool, actions
drawer entry, wake word, and detail-level behavior.

**Depends on:** M44 Command Redesign

## Why This Matters

Read is currently implemented as a vision prompt. `ReadCommand` captures a
frame, checks that the active AI provider supports image input, and calls
`VisionAgent.DescribeFrameAsync`.

That works, but it makes basic OCR depend on a networked vision-capable model.
For blind-first daily use, reading signs, labels, mail, menus, and screens
should be faster, cheaper, more private, and available even when the selected AI
provider is text-only.

M49 makes OCR a local capability:

- `read` extracts text with `Plugin.Maui.OCR`.
- `read_text` continues to delegate to `ReadCommand`.
- the existing `camera.read` assistive action continues to invoke the `read`
  command.
- any internal `read-ocr` action is wired behind Read; it is not a separate
  user-facing command or drawer item.
- AI is optional post-processing for `Summary` and `Overview`, not the source
  of extracted text.

## Source Notes

The plan uses the official `Plugin.Maui.OCR` docs and package page as the
integration source of truth:

- GitHub/docs: <https://github.com/kfrancis/ocr>
- NuGet: <https://www.nuget.org/packages/Plugin.Maui.OCR/>

Relevant plugin facts for implementation:

- MAUI setup is via `.UseOcr()` in `MauiProgram.cs`.
- The plugin registers/injects `Plugin.Maui.OCR.IOcrService`.
- OCR accepts image bytes and returns an `OcrResult` with `AllText`, `Lines`,
  and per-element bounding/confidence data.
- `OcrOptions.Builder()` can set language, `TryHard`, and pattern configs.
- Android needs ML Kit OCR metadata in the `<application>` element:
  `com.google.mlkit.vision.DEPENDENCIES=ocr`.
- Plugin platform minimums are below the app's current supported OS versions:
  iOS 13+, macOS 10.15+, Android API 21+, Windows 10 1809+.

## Current State

Primary code paths:

| Area | Current file | Current behavior |
| --- | --- | --- |
| Read command | `src/BodyCam/Services/Camera/Commands/ReadCommand.cs` | Requires image-capable AI provider and calls `VisionAgent`. |
| Read tool | `src/BodyCam/Tools/ReadTextTool.cs` | Delegates to the registered camera command. |
| Read action | `src/BodyCam/Services/Actions/AssistiveActionIds.cs` and `src/BodyCam/ServiceExtensions.cs` | `camera.read` is registered as a `CameraAssistiveAction` that executes command ID `read`. |
| DI | `src/BodyCam/ServiceExtensions.cs` and `src/BodyCam/MauiProgram.cs` | Registers command and action services, but no OCR service. |
| Android setup | `src/BodyCam/Platforms/Android/AndroidManifest.xml` | Camera permissions exist, but ML Kit OCR dependency metadata does not. |
| Packages | `src/BodyCam/BodyCam.csproj` | No `Plugin.Maui.OCR` package reference. |

## Target Architecture

```
Read trigger
  -> camera.read assistive action / read_text tool / wake word / command UI
  -> CameraCommandService
  -> ReadCommand
  -> CaptureFrameForModeAsync
  -> IReadOcrService
  -> Plugin.Maui.OCR.IOcrService
  -> ReadOcrResult
  -> ReadTextFormatter / optional text-only AI post-processing
  -> CameraCommandResult + transcript
```

The command should depend on a BodyCam-owned abstraction, not directly on the
plugin interface. That keeps tests simple, allows fallback implementations, and
keeps plugin-specific result quirks out of command logic.

Suggested app-facing OCR contracts:

```csharp
public sealed record ReadOcrRequest(
    string? Focus,
    string? Language,
    bool TryHard);

public sealed record ReadOcrResult(
    bool Success,
    string AllText,
    IReadOnlyList<string> Lines,
    IReadOnlyList<ReadOcrElement> Elements,
    double? AverageConfidence,
    string? Error);

public sealed record ReadOcrElement(
    string Text,
    float? Confidence,
    int X,
    int Y,
    int Width,
    int Height);

public interface IReadOcrService
{
    Task InitializeAsync(CancellationToken ct);

    Task<ReadOcrResult> RecognizeAsync(
        byte[] imageBytes,
        ReadOcrRequest request,
        CancellationToken ct);
}
```

The plugin-backed implementation should be small:

- call plugin `InitAsync` lazily or at app startup;
- build `OcrOptions` from language, `TryHard`, and future pattern configs;
- call `RecognizeTextAsync`;
- map plugin results into BodyCam records;
- normalize blank or failed plugin output into one error shape.

## Read Detail Levels

OCR becomes the source text for every level.

| Level | Behavior after M49 |
| --- | --- |
| `Full` | Return OCR text as completely and faithfully as possible, preserving useful line breaks. |
| `Overview` | Identify likely document/sign/screen structure from OCR lines, with optional text-only AI post-processing when available. |
| `Summary` | Provide the shortest useful summary from OCR text, with a deterministic fallback if no AI post-processing is available. |

The command must not require a vision-capable provider anymore. If a provider is
needed for optional post-processing, text/chat capability is enough and local
formatting remains the fallback.

## Action Wiring Rule

OCR belongs behind the existing Read action. The app should not add a second
user-facing OCR action beside Read.

The current action chain should remain:

```
camera.read assistive action -> CameraAssistiveAction -> command ID "read" -> ReadCommand
```

If implementation introduces an internal `read-ocr` action or service name for
diagnostics, route it through `ReadCommand` and keep `AssistiveActionIds.Read`
as the user-facing action identity.

## Phases

1. [Plugin Integration And Platform Setup](phase-1-plugin-integration.md)
2. [OCR Service Abstraction](phase-2-ocr-service-abstraction.md)
3. [Read Command Migration](phase-3-read-command-migration.md)
4. [Read Action Wiring](phase-4-read-action-wiring.md)
5. [Tests, Platform Validation, And Accessibility](phase-5-tests-platform-validation-accessibility.md)

## Success Criteria

- `read_text` can read visible text without an image-capable AI provider.
- `ReadCommand` no longer calls `VisionAgent` for OCR extraction.
- The existing `camera.read` action executes the OCR-backed `ReadCommand`.
- No new user-facing `read-ocr` action appears beside Read.
- The implementation works on Windows and Android first, with iOS and
  MacCatalyst validated through the same abstraction.
- OCR failures are spoken clearly: no text, low confidence, unsupported
  platform, initialization failure, or camera unavailable.
- Long OCR results are transcript-friendly and ready for speech chunking.

## Out Of Scope

- Replacing QR/barcode scanning.
- Building a full document-layout engine.
- Translating OCR text.
- Auto-opening links or performing external actions found in OCR text.
- Changing Look or Scan.
