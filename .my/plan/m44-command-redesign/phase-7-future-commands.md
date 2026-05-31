# M44 Phase 7 - Future Helpful Commands

Goal: collect high-level ideas for future registered camera commands beyond
Look, Read, and Scan.

These should not be built as one large command with many modes. Each useful
behavior should become its own registered `ICameraCommand` class when it is
ready.

## Command Ideas

| Command | Helpful For | High-Level Behavior |
| --- | --- | --- |
| Find | Finding a named object, person, door, sign, shelf item, or landmark. | Ask what to find, capture or stream frames, then guide the user with spatial directions. |
| Where Am I | Orientation in unfamiliar spaces. | Describe location clues, signage, room type, exits, hazards, and likely navigation options. |
| What Changed | Comparing the current view to the previous view. | Capture a frame and explain what is new, missing, moved, or different. |
| Watch | Hands-free monitoring while walking, cooking, working, or waiting. | Periodically inspect frames and only interrupt for important changes, hazards, or requested targets. |
| Guide Me | Navigation-style assistance. | Combine repeated Look/Find passes with concise spatial guidance such as "slightly left" or "door ahead". |
| Describe Person | Social or identification support where appropriate. | Describe visible clothing, position, gestures, and non-sensitive visible details without identity claims. |
| Describe Product | Shopping or household tasks. | Read labels, identify product type, summarize warnings, compare package fronts, and optionally use barcode lookup. |
| Read Screen | Computer, kiosk, TV, phone, or appliance displays. | Extract visible UI text and explain controls or current state. |
| Read Document | Letters, forms, receipts, menus, medicine leaflets. | Capture, structure, summarize, and optionally read section by section. |
| Count | Counting items in view. | Count visible objects with confidence and say when occlusion makes the count uncertain. |
| Color | Clothing, cables, pills, lights, labels, status indicators. | Identify prominent colors and positions, with uncertainty under poor lighting. |
| Light / Status | Appliance lights, charging lights, traffic lights, LEDs. | Detect visible indicator state and explain likely meaning when known. |
| Remember This | Save a visual memory. | Capture frame, summarize it, store a timestamped memory, and optionally attach location/device metadata. |
| Recall | Retrieve previous visual memories or scan history. | Search stored summaries, scans, and captures by query. |
| Compare | Compare two captures or current view versus a saved reference. | Useful for matching products, documents, outfits, labels, or setup instructions. |
| Translate Text | Multilingual signs, menus, labels, letters. | Read visible text, identify language, translate, and preserve original where useful. |
| Explain | Understanding a scene, object, diagram, label, or warning. | Capture and explain what it means, not only what is visible. |
| Verify | Safety and confidence checks. | Confirm whether a target condition seems true, such as "is this the right medicine?" with strong uncertainty warnings. |
| Capture Note | Fast visual note-taking. | Take a picture, add a short description, and save it for later retrieval. |

## Accessibility Priorities

Future commands should be optimized for blind and visually impaired users:

- make full-auto available for every command;
- give short spoken results first, with optional details;
- announce uncertainty clearly;
- prefer spatial directions over visual-only language;
- avoid requiring preview unless the user explicitly asks for manual aim;
- ask before external actions such as navigation, calls, messages, websites,
  saved contacts, or purchases;
- keep all results in the transcript for review.

## Suggested Command Categories

### Orientation

Commands:

- Where Am I
- Guide Me
- Watch
- What Changed

These commands help the user understand a place and move through it. They may
need repeated capture or frame streaming, but should still be built as
registered commands with clear stop/cancel behavior.

### Object And Task Support

Commands:

- Find
- Count
- Color
- Light / Status
- Describe Product
- Verify

These commands help with practical tasks. They need careful uncertainty
language because lighting, occlusion, and camera angle can make answers wrong.

### Text And Information

Commands:

- Read Screen
- Read Document
- Translate Text
- Explain

These can share lower-level OCR/vision helpers with Read, but should be
separate commands if their prompts, output shape, and follow-up actions differ.

### Memory

Commands:

- Remember This
- Recall
- Compare
- Capture Note

These commands need storage and privacy rules. They should be explicit about
what is saved and let the user delete or review saved items.

## Registration Shape

Each future command should follow the same model:

```csharp
public sealed class FindCommand : CameraCommandBase<FindCommandOptions>
{
    public override string Id => "find";
    public override string DisplayName => "Find";
    public override string? ToolName => "find";

    public override Task<CameraCommandResult> ExecuteAsync(
        CameraCommandContext context,
        CancellationToken ct);
}
```

A command should define:

- its own option record;
- its own prompt builder;
- its capture mode defaults;
- whether it supports manual aim;
- whether it can use frame streaming;
- whether it needs confirmation before acting;
- transcript and speech formatting.

## Acceptance

- Future command ideas are documented without forcing them into Look/Read/Scan.
- Each proposed command can become an independent registered command class.
- The roadmap keeps accessibility, safety, and provider-neutral camera support
  as first-class requirements.

