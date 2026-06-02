# Phase 2b - Look Command and Command Settings

## Status

Draft design for the next Look command iteration.

This phase builds on the camera-command layer that already exists in:

- `src/BodyCam/Services/Camera/Commands/LookCommand.cs`
- `src/BodyCam/Services/Camera/Commands/CameraCommandService.cs`
- `src/BodyCam/ViewModels/MainViewModel.cs`
- `src/BodyCam/Models/TranscriptEntry.cs`
- `src/BodyCam/Pages/Main/Views/TranscriptView.xaml`

## Goals

1. Add the captured Look image to the transcript.
2. Add prompt definitions with `Text` and `Prompt` properties for each Look variant.
3. Replace the single Look action with three side-by-side actions: Look, Detail, Summary.
4. Add command settings pages:
   - a button/card on the main Settings page,
   - a command list page,
   - a command detail page,
   - read-only prompt preview for each command.
5. Decide how command settings should be stored.

## Non-Goals

- Prompt editing. Prompts are visible but not editable in Phase 2b.
- Persisting captured Look images. The image is a transcript attachment for the current app session only.
- Reworking all legacy tools. Phase 2b should focus on the camera-command path first.

## Look Transcript Behavior

Current behavior:

- `TranscriptEntry` already supports `Image`, `ImageCaption`, and `HasImage`.
- `TranscriptView.xaml` already renders entry images.
- `SendVisionCommandAsync()` adds a user entry with the prompt and captured image.
- `ExecuteCameraCommandAsync()` currently creates only an AI thinking/answer entry, so Look results do not show the captured frame.

Desired behavior:

```text
You: Look. Give an overview.
[captured frame]

AI: There is a hallway ahead with a door on the left...
```

The image must be the same frame that `LookCommand` sends to the vision model. Do not capture a second frame for the transcript because it can disagree with the model's answer.

Recommended implementation:

- Add transcript metadata to `CameraCommandResult`, outside the JSON `Data` object:

```csharp
public sealed record CameraCommandTranscriptInput(
    string Text,
    byte[]? ImageBytes,
    string? ImageCaption);

public sealed record CameraCommandResult(
    string CommandId,
    bool Success,
    string TranscriptText,
    object? Data,
    string? Error,
    CameraCommandTranscriptInput? TranscriptInput = null);
```

- `LookCommand.ExecuteAsync()` captures the frame once, sends it to `VisionAgent`, and returns the same frame in `TranscriptInput`.
- `MainViewModel.ExecuteCameraCommandAsync()` inserts a `You` entry with the selected prompt definition's `Text` and image before the AI answer entry.
- The image should not be serialized in `ToolResult` JSON. Tool calls can continue returning text/metadata only.

Failure behavior:

- If no frame is captured, show the existing camera error.
- If the vision call fails after a frame was captured, still show the user prompt/image and show the AI error entry.
- For manual aim, attach the manually captured frame, then close the inline preview after the command completes.

## Prompt Definitions

Create first-class prompt definitions now instead of only hard-coded prompt builder methods. This keeps Phase 2b read-only, but makes the model ready for:

- user overrides in settings,
- multiple languages,
- a future list of selectable prompts,
- stable enum keys that the UI can bind to.

Each prompt option should expose both:

- `Text` - the short user-facing text shown in the transcript, buttons, and settings preview.
- `Prompt` - the full model-facing instruction sent to the vision model.

Recommended shape:

```csharp
public enum LookPromptVariant
{
    Summary,
    Overview,
    Detailed,
    Full,
}

public sealed record CommandPromptDefinition(
    string Key,
    string DisplayName,
    string Text,
    string Prompt);

public sealed class LookCommandPrompts
{
    public CommandPromptDefinition Summary { get; init; } = new(
        Key: nameof(LookPromptVariant.Summary),
        DisplayName: "Summary",
        Text: "Look. Give a short summary.",
        Prompt: """
            You are helping a blind or visually impaired user understand a camera frame.
            Safety-relevant observations come first.
            Say when the image appears dark, blurry, blocked, too close, too far away, or ambiguous.
            Do not infer hidden facts or identities.

            Give the shortest useful answer in one or two sentences.
            Lead with the main thing or direct answer.
            If there is any immediate hazard, mention it first.
            """);

    public CommandPromptDefinition Overview { get; init; } = new(
        Key: nameof(LookPromptVariant.Overview),
        DisplayName: "Look",
        Text: "Look. Give an overview.",
        Prompt: """
            You are helping a blind or visually impaired user understand a camera frame.
            Safety-relevant observations come first.
            Say when the image appears dark, blurry, blocked, too close, too far away, or ambiguous.
            Do not infer hidden facts or identities.

            Give an orientation-first overview that is easy to listen to while moving.
            Include people, obstacles, entrances, exits, signs, and major objects.
            Use spatial language such as left, right, ahead, above, below, near, and far.
            """);

    public CommandPromptDefinition Detailed { get; init; } = new(
        Key: nameof(LookPromptVariant.Detailed),
        DisplayName: "Detail",
        Text: "Look at the image in detail.",
        Prompt: """
            You are helping a blind or visually impaired user understand a camera frame.
            Safety-relevant observations come first.
            Say when the image appears dark, blurry, blocked, too close, too far away, or ambiguous.
            Do not infer hidden facts or identities.

            Give a structured scene description.
            Include important objects, relationships, visible text snippets, confidence, uncertainty, and possible next actions.
            Use consistent spatial language such as left, right, ahead, above, below, near, and far.
            """);

    public CommandPromptDefinition Full { get; init; } = new(
        Key: nameof(LookPromptVariant.Full),
        DisplayName: "Full",
        Text: "Look at the image as fully as possible.",
        Prompt: """
            You are helping a blind or visually impaired user understand a camera frame.
            Safety-relevant observations come first.
            Say when the image appears dark, blurry, blocked, too close, too far away, or ambiguous.
            Do not infer hidden facts or identities.

            Give the most complete reasonable description of the frame.
            Include visible text, layout, colors, object details, hazards, and uncertainty.
            Avoid claims that are not supported by the image.
            """);

    public IReadOnlyList<CommandPromptDefinition> All =>
        [Summary, Overview, Detailed, Full];
}
```

Expose those definitions through a command property or small optional interface so the settings pages can render prompts generically:

```csharp
public interface ICommandPromptProvider
{
    IReadOnlyList<CommandPromptDefinition> PromptDefinitions { get; }
}

public sealed class LookCommand : CameraCommandBase<LookCommandOptions>, ICommandPromptProvider
{
    public LookCommandPrompts Prompts { get; } = new();

    public IReadOnlyList<CommandPromptDefinition> PromptDefinitions =>
        Prompts.All;
}
```

Use both an enum and a list:

- The enum gives stable IDs for settings, overrides, tests, and localization.
- The list gives the UI a simple ordered collection to display.

The settings page should render prompt previews as flat, phone-first sections. Do not use visual indentation to show hierarchy. Use full-width rows with labels instead:

```text
Short
Text
Look. Give a short summary.
Prompt
[read-only generated or overridden prompt]

Full
Text
Look at the image as fully as possible.
Prompt
[read-only generated or overridden prompt]
```

The names above are display labels. The implementation should still use stable enum keys such as `Summary`, `Overview`, `Detailed`, and `Full`.

`LookCommand.BuildPrompt()` can remain as a compatibility wrapper in Phase 2b, but internally it should resolve the selected `CommandPromptDefinition` and use its `Prompt` property. The transcript should use the same definition's `Text` property.

## Action Drawer Design

Replace the current single Look button with a horizontal row of three buttons:

```text
+--------------------------------------+
| [ Look ] [ Detail ] [ Summary ]      |
| [ Read ]                             |
| [ Scan ]                             |
+--------------------------------------+
```

Button behavior:

| Button | Prompt variant | Text | Notes |
| --- | --- | --- | --- |
| Look | `Overview` | `Look. Give an overview.` | Default selected action. |
| Detail | `Detailed` | `Look at the image in detail.` | Runs Look with more structure. |
| Summary | `Summary` | `Look. Give a short summary.` | Fastest, shortest response. |

Phase 2b should change the default Look detail from `Summary` to `Overview`, because the requested selected state is the Look/overview button.

Implementation notes:

- Add commands such as `LookOverviewCommand`, `LookDetailCommand`, and `LookSummaryCommand`, or a parameterized `LookCommand` that accepts a detail level.
- Keep button labels exactly: `Look`, `Detail`, `Summary`.
- Suggested automation IDs:
  - `LookOverviewButton`
  - `LookDetailButton`
  - `LookSummaryButton`
- Use selected styling for the current default detail. On first launch this should be `Look`.
- If a Look variant is tapped, execute immediately with an explicit `LookCommandOptions`.
- The button text should come from the prompt definition `DisplayName` where possible.
- Optional: persist the tapped detail as `DefaultLookDetailLevel`. If this is done, the selected styling always mirrors the most recently selected Look variant.

## Command Settings Navigation

Add a Commands entry to the Settings hub.

Phone layout rule:

- Use one full-width row/card per settings destination.
- Do not indent child labels or nested settings.
- Use section labels, dividers, and vertical spacing instead of horizontal nesting.

```text
Settings
Connection
Voice & AI
Devices
Commands
Advanced
```

The Commands page lists camera commands from `ICameraCommandRegistry.Commands`.

```text
Commands
Look
Default: Overview
Tool: look

Read
Default: Full
Tool: read_text

Scan
Confirm external actions: On
Tool: scan_qr_code
```

Each row navigates to a command detail page.

## Command Detail Page

The detail page should be specific enough for the current command, while still using the shared command metadata where possible.

Look page:

```text
Look
Command ID: look
Tool name: look

Settings
Touch behavior
Manual aim / Full auto

Default response
Look / Detail / Summary

Prompts
Summary
Text
Look. Give a short summary.
Prompt
[read-only generated or overridden prompt]

Look
Text
Look. Give an overview.
Prompt
[read-only generated or overridden prompt]

Detail
Text
Look at the image in detail.
Prompt
[read-only generated or overridden prompt]

Full
Text
Look at the image as fully as possible.
Prompt
[read-only generated or overridden prompt]
```

Generic prompt display format:

```text
[Prompt Display Name]
Text
[user-facing transcript/button text]
Prompt
[model-facing prompt]
```

Read page:

- Default response: Summary / Overview / Full.
- Show generated OCR prompt definitions with `Text` and `Prompt` properties.

Scan page:

- Confirm external actions: On/Off.
- Show non-editable scan behavior text rather than model prompts, because Scan does not use a vision prompt.

Prompt sections are read-only in Phase 2b. Use selectable/copyable text if practical, but no editing controls.

Even though prompts are read-only, the UI should bind to prompt definition properties instead of calling prompt builder methods directly.

Phone-first layout requirements:

- Keep settings pages as a single-column vertical flow.
- Avoid indented subitems and tree-like layouts.
- Prefer flat section headers followed by full-width setting rows.
- For prompt previews, show `Text` and `Prompt` as stacked labeled fields inside the same prompt section.

## Storage Decision

Recommendation for Phase 2b: store the small command settings as separate `Preferences` keys through `ISettingsService`.

Keep these as explicit settings:

- `DefaultTouchCommandMode`
- `DefaultLookDetailLevel`
- `DefaultReadDetailLevel`
- `ConfirmExternalScanActions`

Change the default for `DefaultLookDetailLevel` from `Summary` to `Overview`.

Reasons:

- This matches the current code style in `SettingsService`.
- These settings are scalar values and map cleanly to MAUI `Preferences`.
- It avoids a migration for Phase 2b.
- Unit tests can continue stubbing strongly typed properties on `ISettingsService`.
- Default prompt definitions are code/resource-defined in Phase 2b. Only future user overrides should be persisted.

Use JSON later when command settings become nested or user-authored, for example prompt overrides, localized prompt overrides, per-command advanced settings, per-device command defaults, or user-created commands.

Possible future JSON shape:

```json
{
  "version": 1,
  "defaults": {
    "touchCommandMode": "ManualAim"
  },
  "commands": {
    "look": {
      "defaultDetailLevel": "Overview",
      "promptOverrides": {
        "Summary": {
          "en": {
            "text": "Look. Give a short summary.",
            "prompt": "Custom user override..."
          }
        }
      }
    },
    "read": {
      "defaultDetailLevel": "Full"
    },
    "scan": {
      "confirmExternalActions": true
    }
  }
}
```

## MAUI Storage Options

.NET MAUI's normal storage choices are:

| Option | Use for | BodyCam examples |
| --- | --- | --- |
| `Preferences` | Small app preferences in a key/value store. Supports Boolean, numeric types, string, and DateTime. | Most `SettingsService` properties. |
| JSON string in `Preferences` | Small structured settings owned by the app. | `DeviceSettings`, `ButtonMappingStore`. |
| `SecureStorage` | Secrets and tokens. | API keys via `ApiKeyService`. |
| `FileSystem.Current.AppDataDirectory` | Larger app-private files, histories, sidecars, exported metadata. | `MemoryStore`, media sidecars. |
| SQLite or another local DB | Queryable relational data or larger offline stores. | Not needed for Phase 2b. |

References:

- MAUI Preferences: https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/preferences
- MAUI Secure Storage: https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage
- MAUI File System helpers: https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/file-system-helpers

## Implementation Checklist

- Add `CameraCommandTranscriptInput`.
- Add `TranscriptInput` to `CameraCommandResult`.
- Add prompt definition records/classes with `Text` and `Prompt` properties.
- Add stable prompt variant enums, starting with Look prompt variants.
- Expose prompt definitions as a command property/list for settings pages.
- Update `LookCommand` to resolve a prompt definition and use `Prompt`.
- Return the captured Look frame and prompt `Text` from `LookCommand`.
- Update `MainViewModel.ExecuteCameraCommandAsync()` to add a transcript image entry for command results with `TranscriptInput`.
- Replace the single Look button with the `Look`, `Detail`, `Summary` row.
- Change `DefaultLookDetailLevel` defaults from `Summary` to `Overview` in production code and test fakes.
- Add Commands card/button to `SettingsPage`.
- Add `CommandsSettingsPage` and route registration.
- Add command detail page route, view model, and query parameter for command ID.
- Keep command settings pages phone-first: single column, full-width rows, no indented child settings.
- Show command prompt definitions read-only, including both `Text` and `Prompt`.
- Add focused unit tests for prompt `Text`, transcript image metadata, and settings defaults.
- Add focused unit tests for prompt definition resolution.
- Add UI tests for the action drawer and settings navigation.

## Acceptance Criteria

- Opening the actions drawer shows `Look`, `Detail`, and `Summary` side by side.
- `Look` is visually selected by default and maps to `LookDetailLevel.Overview`.
- Running any Look variant adds a transcript entry with the selected prompt `Text` and the exact captured image.
- The AI answer appears after the image-bearing transcript entry.
- The command settings page is reachable from Settings.
- The command list page shows Look, Read, and Scan.
- The Look detail page shows settings and read-only prompt definitions.
- Each prompt preview shows both `Text` and `Prompt`.
- Settings pages use a flat phone layout with no indented nested rows.
- No captured image bytes are stored in Preferences, JSON settings, or tool result JSON.
