# M44 Phase 4 - Scan Command

Goal: rebuild Scan for QR codes and barcodes, including streepjescodes, with
content-aware actions and safe confirmation.

Scan should be implemented as a registered command class, not as a branch in a
central command switch.

```csharp
public sealed class ScanCommand : CameraCommandBase<ScanCommandOptions>
{
    public override string Id => "scan";
    public override string DisplayName => "Scan";

    public override Task<CameraCommandResult> ExecuteAsync(
        CameraCommandContext context,
        CancellationToken ct);
}
```

`ScanCommand` owns decoding, content classification, action suggestions, and
confirmation requirements.

## Behavior

Full auto:

1. Capture one frame.
2. Decode QR codes and barcodes.
3. Classify the decoded content.
4. Add a concise result to the transcript.
5. Offer suggested actions.

Manual aim:

1. Open inline camera preview.
2. Wait for capture, or optionally scan frames until a code is detected.
3. Decode and classify the result.

Button, wake-word, shortcut, and LLM scan invocations should capture
immediately unless the user explicitly asks for manual scan.

## Content Actions

Scan should classify at least:

- website URLs;
- plain text;
- Wi-Fi credentials;
- contact cards;
- email addresses;
- SMS links;
- phone numbers;
- calendar events;
- map/location links;
- product barcodes.

External actions require confirmation:

- open website;
- join Wi-Fi;
- save contact;
- call phone number;
- send email or SMS;
- open maps;
- run product lookup if it may use network services.

The confirmation message should be short, specific, and screen-reader friendly.
For URLs, announce the domain before asking.

## Product Barcodes

Product barcode flow:

1. Decode barcode.
2. Show code and format.
3. Ask or use setting before online lookup.
4. Lookup product information when available.
5. Summarize product name, brand, category, and warnings when present.

## Safety

- Never silently open a website.
- Never silently send or call.
- Warn about shortened URLs and unknown domains.
- Keep raw decoded content available in the transcript.

## Acceptance

- Scan is a separate registered command class.
- QR and barcode scan use the same command pipeline.
- Scan can run full-auto or manual aim.
- Website and external actions ask first.
- Product barcode lookup still works through existing barcode services.
- Tests cover content classification and confirmation requirements.
