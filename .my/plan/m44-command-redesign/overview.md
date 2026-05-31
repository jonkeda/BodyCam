# M44 - Command Redesign

**Status:** Proposed
**Scope:** Rebuild Look, Read, and Scan as first-class camera commands with
auto and manual capture modes.

M44 redoes the command model so every camera command works from the same
concepts:

- the active camera source can be glasses, bodycam, phone camera, USB camera,
  or future providers;
- a command can run fully automatically or help the user aim first;
- physical buttons, wake words, keyboard shortcuts, and LLM tool calls should
  be fast and immediate;
- touchscreen/manual use can open the live preview and wait for a capture
  button;
- output must be useful for blind and visually impaired people first.

## No Backward Compatibility Requirement

M44 may break the old command APIs, tool registrations, and UI action wiring.
The command registry becomes the source of truth.

Phase 1 and Phase 2 connect Look first. Current Read and Scan flows can be
disconnected, hidden, or disabled while the new architecture lands. Read and
Scan return in Phase 3 and Phase 4 as registered command classes, not as
compatibility wrappers over the old behavior.

## Core UX Rules

### Full Auto

Full auto is the default for non-touch trigger origins.

For Look, full auto means:

1. Capture one frame from the active camera.
2. Send the frame to the vision pipeline / LLM.
3. Return the answer in the transcript.
4. Speak the answer only when output mode is `Speak`.

The same pattern applies to Read and Scan, with command-specific processing.

### Manual Aim

Manual aim is for cases where the user wants to point the camera before capture.

Manual aim means:

1. Open the camera preview inline.
2. Keep transcript visible, but let the preview push it up.
3. Wait for a round capture button or mapped hardware capture action.
4. Capture the frame.
5. Run the command.
6. Return to the normal transcript-first layout.

Manual aim must be explicitly chosen from the UI or a command option. It should
not surprise a blind user who invoked a command by voice, button, shortcut, or
LLM.

### Trigger Origins

The trigger origin decides whether it is appropriate to wait for a second user
action.

| Trigger origin | Default behavior | Why |
| --- | --- | --- |
| LLM tool call | Full auto | The model already decided the command is needed. |
| Physical button | Full auto | The button press is the user's capture intent. |
| Wake word / voice command | Full auto | Blind and hands-free use should not require screen interaction. |
| Keyboard shortcut | Full auto | Shortcut is an explicit command. |
| Actions drawer command | Configurable, default manual aim on touch devices | The user may want to point first. |
| Explicit "manual look/read/scan" command | Manual aim | User asked to aim first. |
| Test automation | Explicit mode | Tests should declare intent. |

If a command is started by a physical button or by the LLM, it goes
immediately. No preview pause.

## Commands

### Look

Look describes the visible environment, object, scene, or target.

Detail levels:

| Level | Behavior |
| --- | --- |
| `Summary` | One or two short sentences with the most useful answer. |
| `Overview` | Orientation-first description: what is around, where important things are, hazards, exits, or main objects. |
| `Detailed` | Structured description with important objects, positions, text snippets, people, uncertainty, and possible next actions. |
| `Full` | Most complete description reasonable for the frame, including visible text, objects, layout, colors, safety notes, and uncertainty. |

Look should be especially careful with blind and visually impaired use:

- lead with safety-relevant information;
- say when the image is blurry, dark, blocked, or ambiguous;
- use spatial language such as left, right, ahead, near, far, top, bottom;
- avoid pretending to know hidden facts;
- keep `Summary` short enough to listen to while moving.

### Read

Read extracts visible text.

Detail levels:

| Level | Behavior |
| --- | --- |
| `Summary` | Summarize what the text says. |
| `Overview` | Explain the document or sign type and the main sections. |
| `Full` | Read the visible text as completely and exactly as possible. |

Read should support common targets:

- signs;
- labels;
- documents;
- menus;
- screens;
- mail;
- package text;
- medication or product labels, with a warning when confidence is low.

### Scan

Scan detects QR codes and barcodes, including streepjescodes.

Scan result handling should be content-aware:

| Content | Suggested behavior |
| --- | --- |
| Website URL | Read domain and ask before opening. |
| Wi-Fi QR | Explain network name and ask before joining or copying. |
| Contact card | Summarize contact and ask before saving. |
| Email / SMS / phone | Ask before launching or sending. |
| Location | Ask before opening maps. |
| Product barcode | Look up product information when available. |
| Unknown content | Read decoded content and offer copy/share actions. |

For safety, scan should not automatically open websites or perform external
actions without confirmation. Blind users must hear what action will happen and
be able to cancel.

## Proposed Command Model

Add registered command classes that are not tied to UI buttons. Command
identity should not be an enum. Each command should own its own metadata,
options, prompts, capture requirements, and execution method.

This avoids a large central switch statement such as:

```csharp
switch (request.Kind)
{
    case Look:
    case Read:
    case Scan:
}
```

Instead, the command service looks up a registered command and calls it.

```csharp
public enum CameraCommandMode
{
    FullAuto,
    ManualAim
}

public enum CommandTriggerOrigin
{
    ActionsDrawer,
    PhysicalButton,
    WakeWord,
    KeyboardShortcut,
    LlmToolCall,
    Automation,
    ExplicitManual
}

public interface ICameraCommand
{
    string Id { get; }
    string DisplayName { get; }
    string? ToolName { get; }
    CameraCommandCapabilities Capabilities { get; }
    IReadOnlyList<CommandOptionDefinition> Options { get; }

    CameraCommandMode ResolveMode(CameraCommandRequest request, CameraCommandContext context);
    Task<CameraCommandResult> ExecuteAsync(CameraCommandContext context, CancellationToken ct);
}

public sealed record CameraCommandRequest(
    string CommandId,
    CameraCommandMode? Mode,
    CommandTriggerOrigin Origin,
    object? Options,
    string? Query);

public sealed record CameraCommandContext(
    CameraCommandRequest Request,
    CameraCommandMode ResolvedMode,
    CameraManager Cameras,
    ISettingsService Settings,
    Func<CancellationToken, Task<byte[]?>> CaptureFrame,
    Func<CancellationToken, Task<byte[]?>> WaitForManualCapture);

public sealed record CameraCommandCapabilities(
    bool SupportsFullAuto,
    bool SupportsManualAim,
    bool RequiresStillFrame,
    bool CanUseFrameStream,
    bool RequiresConfirmationForExternalActions);

public sealed record CommandOptionDefinition(
    string Name,
    Type ValueType,
    object? DefaultValue,
    bool PersistLastSelectedValue);

public sealed record CameraCommandResult(
    string CommandId,
    bool Success,
    string TranscriptText,
    object? Data,
    string? Error);

public interface ICameraCommandRegistry
{
    IReadOnlyList<ICameraCommand> Commands { get; }
    bool TryGet(string id, out ICameraCommand command);
    bool TryGetTool(string toolName, out ICameraCommand command);
    ICameraCommand GetRequired(string id);
}
```

Concrete commands are classes:

```csharp
public sealed class LookCommand : CameraCommandBase<LookCommandOptions>
{
    public override string Id => "look";
    public override string DisplayName => "Look";
    public override string? ToolName => "look";

    public override Task<CameraCommandResult> ExecuteAsync(
        CameraCommandContext context,
        CancellationToken ct)
    {
        // Capture, build the Look-specific prompt, call vision, format result.
    }
}

public sealed record LookCommandOptions(
    LookDetailLevel DetailLevel,
    string? Focus,
    string? Question);

public enum LookDetailLevel
{
    Summary,
    Overview,
    Detailed,
    Full
}
```

`ReadCommand` and `ScanCommand` define their own option records and prompts.
Read can have `ReadDetailLevel` and a text focus hint. Scan can have
continuous/manual scan settings, allowed code formats, and confirmation policy.

The command service resolves cross-cutting defaults, then delegates to the
registered command:

- physical button, wake word, shortcut, LLM -> `FullAuto`;
- explicit manual -> `ManualAim`;
- actions drawer -> user setting, defaulting to manual aim on touch-first
  devices and full auto on wearable/button-first devices;
- command-specific default options come from the command and settings.

Registration should happen in DI:

```csharp
// Phase 1/2
services.AddSingleton<ICameraCommand, LookCommand>();
services.AddSingleton<ICameraCommandRegistry, CameraCommandRegistry>();
services.AddSingleton<ICameraCommandService, CameraCommandService>();
```

Later phases register their own commands:

```csharp
// Phase 3
services.AddSingleton<ICameraCommand, ReadCommand>();

// Phase 4
services.AddSingleton<ICameraCommand, ScanCommand>();
```

Adding a future command should mean adding a new class and registering it, not
editing a central switch statement.

## Camera Source Contract

M44 should preserve the provider split. Commands should not know whether the
image comes from a phone camera, USB camera, bodycam, or glasses camera.

If the current `ICameraProvider` contract is missing enough metadata for manual
aim, extend it with capability properties rather than checking provider IDs.

Suggested capability shape:

```csharp
public sealed record CameraProviderCapabilities(
    bool SupportsStillCapture,
    bool SupportsLivePreview,
    bool SupportsFrameStream,
    bool IsWearable,
    bool IsHandsFreePreferred,
    TimeSpan TypicalCaptureLatency);
```

Manual aim requires `SupportsLivePreview` or a provider-specific preview
adapter. If preview is unavailable, manual aim should fall back to a guided
full-auto capture with a clear transcript message.

## Accessibility Contract

This feature is for blind and visually impaired users, so acceptance must cover
more than visual polish:

- every command can run without looking at the screen;
- full-auto command completion is announced through transcript and speech mode;
- manual preview has a large round capture button with screen-reader label and
  hint;
- focus moves predictably into and out of the preview;
- scan confirmations are reachable by keyboard, screen reader, and hardware
  buttons;
- dangerous external actions require confirmation;
- output can be short enough for movement and detailed enough for inspection;
- failures say what happened and what the user can do next.

## Implementation Phases

1. [Command Contracts And Defaults](phase-1-command-contracts.md)
2. [Look Command](phase-2-look-command.md)
3. [Read Command](phase-3-read-command.md)
4. [Scan Command](phase-4-scan-command.md)
5. [UI And Accessibility](phase-5-ui-accessibility.md)
6. [Provider Coverage And Tests](phase-6-provider-coverage-tests.md)
7. [Future Helpful Commands](phase-7-future-commands.md)

## Out Of Scope

- Replacing the existing camera provider architecture.
- Auto-opening websites or performing external actions without confirmation.
- Making preview mandatory for wearable or blind-first flows.
- Building product lookup for every barcode database in this milestone.
