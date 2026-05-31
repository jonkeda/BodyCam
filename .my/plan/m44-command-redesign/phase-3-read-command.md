# M44 Phase 3 - Read Command

Goal: rebuild Read around text extraction with output levels that make sense
for listening and inspection.

Read should be implemented as a registered command class, not as a branch in a
central command switch.

```csharp
public sealed class ReadCommand : CameraCommandBase<ReadCommandOptions>
{
    public override string Id => "read";
    public override string DisplayName => "Read";

    public override Task<CameraCommandResult> ExecuteAsync(
        CameraCommandContext context,
        CancellationToken ct);
}
```

`ReadCommand` owns its own prompt builder, text-focus options, and output
formatting.

## Behavior

Full auto:

1. Capture one frame.
2. Ask the vision model to extract visible text.
3. Format according to the selected detail level.
4. Add the result to the transcript and speech output when enabled.

Manual aim:

1. Open inline camera preview.
2. Wait for capture.
3. Run the same Read pipeline.

Read should support an optional focus hint such as `sign`, `label`,
`document`, `screen`, `menu`, or `package`.

## Detail Levels

`Summary`:

- summarize what the text says;
- useful when the user wants the meaning, not every word.

`Overview`:

- identify the type of text or document;
- describe sections, headings, and important fields;
- call out important warnings, prices, dates, names, or instructions.

`Full`:

- read visible text as completely and exactly as possible;
- preserve order when possible;
- say when text is cut off, unreadable, or uncertain.

## Accessibility Requirements

- For long text, chunk speech into manageable sections.
- Provide "continue", "repeat", or "summarize" follow-up hooks later.
- For safety-critical labels such as medication, food allergens, or warning
  signs, report uncertainty clearly.
- Avoid saying unreadable text as if it were certain.

## Acceptance

- Read is a separate registered command class.
- Read supports `Summary`, `Overview`, and `Full`.
- Read can run full-auto from voice, button, shortcut, and LLM.
- Manual Read lets the user aim first.
- Long text is transcript-friendly and speech-friendly.
- Tests cover prompt construction, focus hints, and uncertainty language.
