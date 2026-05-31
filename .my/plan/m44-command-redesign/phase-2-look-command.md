# M44 Phase 2 - Look Command

Goal: rebuild Look around full-auto capture, optional manual aim, and selectable
detail levels.

Look should be implemented as a registered command class, not as a branch in a
central command switch.

Phase 2 is Look only. The old Read and Scan flows should remain disconnected
until Phase 3 and Phase 4 add `ReadCommand` and `ScanCommand`.

```csharp
public sealed class LookCommand : CameraCommandBase<LookCommandOptions>
{
    public override string Id => "look";
    public override string DisplayName => "Look";

    public override Task<CameraCommandResult> ExecuteAsync(
        CameraCommandContext context,
        CancellationToken ct);
}
```

`LookCommand` owns its own prompt builder and detail-level options.

The old Look tool may be replaced instead of adapted. No backward compatibility
layer is required; the active Look entry point should call `ICameraCommandService`
and the registered `LookCommand`.

## Behavior

Full auto:

1. Resolve active camera.
2. Capture one frame.
3. Run the vision pipeline with the selected detail prompt.
4. Add the result to the transcript.
5. Speak the result only when output mode is `Speak`.

Manual aim:

1. Open inline camera preview.
2. Wait for the capture button or mapped capture gesture.
3. Capture one frame.
4. Run the same Look processing.
5. Close or collapse preview according to the user's command setting.

If Look is triggered by a physical button, wake word, keyboard shortcut, or LLM
tool call, skip manual aim and capture immediately.

## Detail Prompts

`Summary`:

- shortest useful answer;
- one or two sentences;
- lead with the main thing or direct answer.

`Overview`:

- orientation-first;
- include people, obstacles, entrances, exits, signs, and major objects;
- use spatial language.

`Detailed`:

- structured scene description;
- include objects, relationships, visible text snippets, confidence, and
  possible next actions.

`Full`:

- most complete reasonable description;
- include visible text, layout, colors, object details, hazards, and
  uncertainty;
- avoid claims not supported by the frame.

## Accessibility Requirements

- `Summary` and `Overview` must be listenable while moving.
- Safety-relevant observations come first.
- Say when the frame is dark, blurry, blocked, too close, or too far away.
- Use consistent spatial vocabulary: left, right, ahead, above, below, near,
  far.
- Do not require the camera preview for blind-first use.

## Acceptance

- Look is a separate registered command class.
- Look supports `Summary`, `Overview`, `Detailed`, and `Full`.
- LLM and physical button Look invocations capture immediately.
- Manual Look opens preview and waits for capture.
- Look works with any active `ICameraProvider` that can capture still frames.
- The LLM-visible camera command surface exposes Look only during this phase.
- Read and Scan remain disconnected from legacy behavior until their phases.
- Tests verify prompt selection and mode behavior.
