# M49 Phase 2 - OCR Service Abstraction

**Status:** Proposed
**Goal:** Introduce a BodyCam-owned OCR service layer that wraps
`Plugin.Maui.OCR.IOcrService` and exposes stable command-friendly results.

## New Components

Create a small namespace under `src/BodyCam/Services/Ocr/`.

Suggested files:

| File | Purpose |
| --- | --- |
| `OcrContracts.cs` | `IReadOcrService`, request, result, and element records. |
| `MauiReadOcrService.cs` | Plugin-backed implementation. |
| `ReadOcrFormatter.cs` | Shared formatting helpers for `Summary`, `Overview`, and `Full`. |
| `NullReadOcrService.cs` | Unsupported-platform fallback if needed. |

Keep the abstraction text-specific. This milestone is about the Read action, not
general computer vision.

## Contract Shape

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

public interface IReadOcrService
{
    Task InitializeAsync(CancellationToken ct);

    Task<ReadOcrResult> RecognizeAsync(
        byte[] imageBytes,
        ReadOcrRequest request,
        CancellationToken ct);
}
```

`ReadOcrResult.Success` means OCR ran successfully and meaningful text was
found. A successful plugin call with blank text should become `Success = false`
with a `no_text` style error.

## Plugin Mapping

`MauiReadOcrService` should:

- depend on `Plugin.Maui.OCR.IOcrService`;
- call `InitAsync` once, lazily, with cancellation support;
- pass raw JPEG bytes from the existing camera command capture path;
- use `OcrOptions.Builder()` when language, `TryHard`, or pattern configs are
  set;
- map `AllText`, `Lines`, and `Elements`;
- compute a best-effort average confidence when plugin elements expose it;
- avoid throwing for expected OCR failures such as blank output.

Keep exception handling narrow:

- cancellation rethrows;
- plugin or platform errors become `ReadOcrResult.Success = false`;
- unexpected errors are still logged and returned to the command as readable
  failure text.

## DI

Add registration in `src/BodyCam/ServiceExtensions.cs`.

Possible extension:

```csharp
public static IServiceCollection AddOcrServices(this IServiceCollection services)
{
    services.AddSingleton<IReadOcrService, MauiReadOcrService>();
    services.AddSingleton<ReadOcrFormatter>();
    return services;
}
```

Then call `.AddOcrServices()` in `MauiProgram.cs` before `.AddCameraServices()`
or before commands are resolved.

If platform or package compatibility requires conditional registration, keep the
same `IReadOcrService` contract and register `NullReadOcrService` only where the
plugin is not available.

## Formatting Rules

`ReadOcrFormatter` should centralize deterministic formatting:

- `Full`: preserve line order and useful line breaks.
- `Overview`: group likely headings, short sections, list-like text, prices,
  dates, phone numbers, emails, URLs, and warnings.
- `Summary`: shortest useful answer, with detected high-signal lines first.

The formatter should stay focused on the explicit Read action. Do not add
Look-specific output here.

## Acceptance

- Unit tests can use a fake `IReadOcrService` without referencing the plugin.
- Plugin result mapping preserves all text, lines, and element locations.
- Blank OCR output is normalized to a no-text result.
- Initialization is idempotent and cancellation-safe.
- DI can resolve `IReadOcrService` and `ReadOcrFormatter`.

## Risks

- Plugin element coordinates may use platform-specific image orientation or
  scaling. Preserve raw values first; normalize only after platform testing.
- Confidence may be missing or inconsistent across platforms. Treat it as
  optional.
- Image preprocessing might be needed later for small or low-contrast text, but
  it should not block the first service abstraction.
