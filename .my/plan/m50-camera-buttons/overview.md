# M50 - Camera Buttons

**Status:** Implemented
**Goal:** Redesign the camera-screen action controls so the live video is the
primary surface, registered camera actions sit directly underneath it, only the
selected action's sub-buttons are shown, and the captured picture is added to
the transcript before the command result arrives.

**Depends on:** M44 Command Redesign

## User Requirements

1. The camera action list should be driven by registered action classes.
2. The selected action should expose its own sub-buttons generically.
3. After a sub-button is selected, the captured picture should be shown in the
   transcript.
4. The camera buttons should be shown underneath the video.

The intended visual direction is a compact segmented control style under the
camera preview: selected or primary action in the app's purple style, inactive
choices in muted grey, with 8px rounded corners and stable button heights.

## Current State

Relevant files:

| Area | Current file | Current behavior |
| --- | --- | --- |
| Main layout | `src/BodyCam/Pages/Main/MainPage.xaml` | Transcript is primary, camera preview is row 3, actions drawer overlays rows 1-3. |
| Camera preview | `src/BodyCam/Pages/Main/Views/CameraTabView.xaml` | Shows video plus a round capture button. Snapshot display is an overlay. |
| Action list | `src/BodyCam/Pages/Main/Views/ActionsDrawerView.xaml` | Shows Look, Detail, Summary, Read, Scan, Product all at once in a drawer. |
| Registered actions | `src/BodyCam/Services/Actions/AssistiveActionRegistry.cs` | Orders registered `IAssistiveAction` descriptors. |
| Registered camera commands | `src/BodyCam/Services/Camera/Commands/CameraCommandRegistry.cs` | Orders registered `ICameraCommand` classes and metadata. |
| Action state | `src/BodyCam/ViewModels/MainViewModel.cs` | Individual commands close the drawer and execute camera commands. |

The existing drawer works for command discovery, but it competes with the video
and puts too many choices on screen at once. M50 turns the camera screen into a
simple capture-first flow backed by registered action metadata.

## Target Interaction

```text
Camera screen
  live video
  registered camera action list under video

User taps Look / Read / Scan from the registered action list
  -> sub-buttons for the selected action are shown with the camera view
User taps a sub-button
  -> camera preview captures one frame
  -> captured still is displayed in the transcript
  -> command runs using that captured frame
  -> wait visual cycles: . .. ...
  -> transcript receives the result
```

Only the selected action's sub-buttons should be visible. For example, tapping
Look can expose Summary / Look / Detail, but the UI should not hardcode separate
Look, Read, Scan, or Product panels.

## Generic Action Model

| Source | UI output |
| --- | --- |
| `IAssistiveActionRegistry.Actions` filtered to camera actions | Top-level camera action buttons such as Look, Read, Scan. |
| Linked `ICameraCommand` metadata | Command id, display name, capabilities, options. |
| `ICommandPromptProvider.PromptDefinitions` or option metadata | Sub-buttons such as Summary, Look, Detail. |
| No variants available | One default sub-button using the action display name. |

Do not add a `CameraActionPanel` enum, previous/next action controls, or
hardcoded `ShowLookActionPanel` / `ShowReadActionPanel` properties.

Product lookup should appear only if it is represented as a registered camera
action. M50 should not special-case a Product button in the camera UI.

## UI Rules

- Buttons live in `CameraTabView` directly below the `CameraView`.
- The actions drawer should no longer overlay the camera/video area for camera
  actions.
- The selected action should use the existing primary color.
- Inactive variants should use the muted button style shown in the reference.
- Button dimensions should be stable on mobile and desktop.
- Text must not resize the button row or wrap awkwardly.
- The captured picture should be visible in the transcript after sub-button
  selection, before or while the command result is produced.
- The command's waiting row should use the existing `.`, `..`, `...` visual.
- Voice, hardware-button, wake-word, keyboard, and LLM-triggered command flows
  should keep their existing full-auto behavior unless explicitly started from
  the camera UI.

## Phases

1. [State And Interaction Model](phase-1-state-and-interaction-model.md)
2. [Camera Layout And Button Rail](phase-2-camera-layout-and-button-rail.md)
3. [Capture And Snapshot Flow](phase-3-capture-and-snapshot-flow.md)
4. [Tests And Accessibility](phase-4-tests-and-accessibility.md)

## Success Criteria

- The camera preview has registered camera action buttons directly underneath
  the video.
- The old overlay action list no longer shows all camera actions at once.
- Only the selected registered action's sub-buttons are visible.
- Tapping a top-level action only opens its sub-buttons and camera preview.
- Tapping a sub-button captures a still and adds it to the transcript.
- Look/Detail/Summary retain their behavior through generic variant metadata,
  not through hardcoded Look-specific UI state.
- Read and Scan remain reachable from the camera UI through the same generic
  action list.
- Results still register in the transcript.
- Screen-reader labels and keyboard focus match the new flow.

## Out Of Scope

- Replacing the camera provider architecture.
- Changing full-auto behavior for physical buttons, wake words, keyboard
  shortcuts, or LLM tool calls.
- Reworking OCR or barcode lookup internals.
- Adding a product history or media gallery.
