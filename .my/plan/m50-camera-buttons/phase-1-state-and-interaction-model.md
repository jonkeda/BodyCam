# Phase 1 - State And Interaction Model

**Goal:** Drive the camera action UI from registered action and command
metadata, not from a hardcoded `CameraActionPanel` enum or separate
Look/Read/Scan visibility properties.

## Current Problem

`ActionsDrawerView` exposes all camera actions at once and `MainViewModel`
currently owns individual commands for Look, Detail, Summary, Read, Scan, and
Product. That makes the UI easy to hardcode, but it does not scale with the
registered action classes already in the app.

M50 should make the camera action UI generic:

- top-level camera actions come from registered action descriptors;
- selected action details come from the selected registered action and its
  linked camera command;
- sub-buttons come from command prompt/option metadata where available;
- actions with no variants get a single default sub-button.

Do not add:

- `CameraActionPanel`;
- `ShowLookActionPanel`, `ShowReadActionPanel`, `ShowScanActionPanel`, etc.;
- previous/next action controls;
- a separate "select panel" UI command.

## Existing Registries To Use

The current code already has the important contracts:

| Contract | File | Use In M50 |
| --- | --- | --- |
| `IAssistiveActionRegistry` | `src/BodyCam/Services/Actions/AssistiveActionContracts.cs` | Source of top-level registered actions. |
| `AssistiveActionDescriptor` | `src/BodyCam/Services/Actions/AssistiveActionContracts.cs` | Provides action id, display name, camera requirement. |
| `ICameraCommandRegistry` | `src/BodyCam/Services/Camera/Commands/CameraCommandContracts.cs` | Source of registered camera commands and metadata. |
| `ICameraCommand.Options` | `src/BodyCam/Services/Camera/Commands/CameraCommandContracts.cs` | Generic command options. |
| `ICommandPromptProvider.PromptDefinitions` | `src/BodyCam/Services/Camera/Commands/CameraCommandContracts.cs` | Source of sub-buttons such as Summary, Look, Detail. |

If a camera action cannot be mapped from `AssistiveActionDescriptor` to an
`ICameraCommand`, extend the registry contract with metadata rather than adding
UI-specific switch statements. For example, `CameraAssistiveAction` could expose
its command id through a descriptor extension or a UI adapter.

## Proposed View Models

Use generic action view models:

```csharp
public sealed class CameraActionItemViewModel
{
    public required string ActionId { get; init; }
    public required string Label { get; init; }
    public required bool RequiresCamera { get; init; }
    public IReadOnlyList<CameraActionVariantViewModel> Variants { get; init; } = [];
}

public sealed class CameraActionVariantViewModel
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public object? Options { get; init; }
    public string? Query { get; init; }
}
```

Suggested `MainViewModel` surface:

```csharp
public IReadOnlyList<CameraActionItemViewModel> CameraActions { get; }
public CameraActionItemViewModel? ActiveCameraAction { get; }
public IReadOnlyList<CameraActionVariantViewModel> ActiveCameraActionVariants { get; }
public bool HasActiveCameraAction => ActiveCameraAction is not null;
public bool HasActiveCameraActionVariants => ActiveCameraActionVariants.Count > 0;
```

The UI can set `ActiveCameraAction` when the user taps a registered top-level
action. That is not a separate previous/next/select navigation model; it is the
natural result of tapping an action in the list.

## Variant Rules

Build variants generically:

- If the linked command implements `ICommandPromptProvider`, use its
  `PromptDefinitions` as sub-buttons.
- If the command has persisted option definitions that map cleanly to an enum
  detail level, build one variant per enum value.
- If no metadata produces variants, create a single default variant using the
  action display name.

Examples:

| Registered action | Variants |
| --- | --- |
| Look | Summary, Look, Detail from prompt/detail metadata. |
| Read | Read, or OCR detail variants when M49 exposes them. |
| Scan | Single Scan variant unless scan-specific modes are added. |

Product lookup should appear only if it is represented as a registered camera
action. M50 should not special-case a Product button in the camera UI.

## Desired Flow

```text
User taps Look / Read / Scan from the registered action list
  -> sub-buttons for that registered action are shown under the camera view
  -> camera preview is shown
  -> user taps a sub-button
  -> camera preview captures one frame
  -> captured still is displayed in the transcript
  -> command runs using that captured frame
  -> wait visual cycles: . .. ...
  -> transcript receives the result
```

## Interaction Contract

- The camera UI is backed by `CameraActions`, not by hardcoded action names.
- Only the selected registered action's sub-buttons are visible.
- Tapping a top-level action shows the preview and its sub-buttons; it does not
  capture yet.
- Tapping a sub-button captures and executes.
- The captured still is added to the transcript, not only shown in a camera
  overlay.
- A waiting transcript row uses the existing busy visual behavior (`.`, `..`,
  `...`) while the command runs.
- Non-touch triggers keep M44 full-auto behavior.

## Acceptance Criteria

- Phase 1 introduces no `CameraActionPanel` enum.
- The top-level camera action list is built from registered actions.
- The active action's sub-buttons are built from action/command metadata.
- There are no previous/next action controls.
- Selecting a top-level action does not invoke Look/Read/Scan/Product.
- Unit tests cover registry-driven action list creation and variant generation.
