# Phase 2 - Camera Layout And Button Rail

**Goal:** Move registered camera action controls underneath the video and render
the selected action's sub-buttons from metadata.

## Target Layout

`CameraTabView.xaml` should own the camera action rail:

```text
CameraTabView
  Border
    Grid
      Row 0 - live video
      Row 1 - registered camera action list
      Row 2 - selected action sub-buttons
```

The action list and sub-buttons should be visually close to the provided
reference:

- selected or primary option: primary purple background and white text;
- inactive option: muted grey surface and text primary;
- 8px corner radius;
- stable height around 48-56px;
- even spacing;
- no nested cards.

## Registered Action List

The top-level list should be generated from registered action descriptors:

```text
[Look] [Read] [Scan] ...
```

Filtering rule:

- include actions with `AssistiveActionDescriptor.RequiresCamera == true`;
- exclude session-only actions;
- include Product only if Product has been registered as a camera action;
- avoid hardcoded action names in the XAML or view model.

Tapping an action:

1. sets that registered action as the active camera action;
2. reveals the camera preview;
3. shows that action's generated sub-buttons;
4. does not capture yet.

## Sub-Button Rail

The sub-button rail is generated from the selected action's metadata.

Examples:

```text
Selected action: Look
[Summary] [Look] [Detail]

Selected action: Scan
[Scan]
```

Variant source order:

1. `ICommandPromptProvider.PromptDefinitions` when the linked command exposes
   prompt definitions;
2. option metadata such as enum detail levels when prompt definitions are not
   enough;
3. one default sub-button using the action display name.

The Look row should be produced by this generic variant process, not by a
separate Look-specific XAML block.

## Drawer Rule

`ActionsDrawerView` may continue to exist for non-camera actions or discovery,
but it should not be the primary camera control while the preview is visible.
Camera actions should be reachable from the under-video registry-driven rail.

## Acceptance Criteria

- Camera action buttons are rendered directly below `CameraView`.
- Top-level buttons are generated from registered camera actions.
- Sub-buttons are generated from the active action's command metadata.
- The old actions drawer does not overlay the camera preview during the camera
  action flow.
- Only the active action's sub-button rail is visible.
- The Look sub-button row matches the reference style closely.
- The layout works on narrow mobile widths without text overflow.
