# RCA-03: Hide Top-Level Row After Action Selection

## Problem

After selecting a camera action, the camera surface shows two button rows:

1. The top-level action row: `Look`, `Find`, `Read`, `Scan`.
2. The selected action's sub-button row, for example `Overview`, `Summary`,
   `Detail`.

Expected behavior: once a top-level action is selected, only the second row
should remain visible. The selected action's sub-buttons are the active control
surface.

## Current Implementation

`CameraTabView.xaml` has two rails:

```xml
<FlexLayout AutomationId="CameraActionRail"
            IsVisible="{Binding ShowCameraActionRail}" />

<FlexLayout AutomationId="CameraActionVariantRail"
            IsVisible="{Binding HasActiveCameraActionVariants}" />
```

`ShowCameraActionRail` currently remains true whenever registered camera actions
exist and the inline camera preview is open:

```csharp
public bool ShowCameraActionRail => HasCameraActions && ShowInlineCameraPreview;
```

When `ActivateCameraActionAsync(...)` runs, it sets `ActiveCameraAction` and
fills `ActiveCameraActionVariants`, but the top-level row remains visible
because `ShowCameraActionRail` does not consider `ActiveCameraAction`.

## Root Cause

The top-level action rail visibility is tied only to preview state, not to the
action-selection state.

After selection, the state becomes:

- `ShowInlineCameraPreview == true`
- `HasCameraActions == true`
- `ActiveCameraAction != null`
- `HasActiveCameraActionVariants == true`

That makes both `ShowCameraActionRail` and `HasActiveCameraActionVariants` true,
so both rows render.

## Fix Direction

Make the top-level row visible only before an action is selected:

```csharp
public bool ShowCameraActionRail =>
    HasCameraActions && ShowInlineCameraPreview && ActiveCameraAction is null;
```

Also raise `OnPropertyChanged(nameof(ShowCameraActionRail))` whenever
`ActiveCameraAction` changes.

This keeps the flow:

1. Open camera action surface.
2. Show top-level row.
3. User selects `Look`, `Find`, `Read`, or `Scan`.
4. Hide top-level row.
5. Show only the selected action's sub-button row.

## Implemented Fix

`ShowCameraActionRail` now returns true only when the inline preview is open and
no action is active:

```csharp
public bool ShowCameraActionRail =>
    HasCameraActions && ShowInlineCameraPreview && ActiveCameraAction is null;
```

`ActiveCameraAction` now raises `ShowCameraActionRail` change notifications, so
selecting `Look`, `Find`, `Read`, or `Scan` hides the top-level row as soon as
the variant row is populated.

## Follow-Up Consideration

Once the top-level row is hidden, the user needs a way to choose a different
top-level action. Options:

- Let the existing bottom `Actions` button reopen/reset the top-level row.
- Add a compact back/change-action affordance outside the sub-button row.
- Clear `ActiveCameraAction` after a variant runs, returning to the top-level
  row.

Choose one interaction before implementing if switching actions after selection
needs to be supported immediately.

## Files to Check

- `src/BodyCam/ViewModels/MainViewModel.cs`
- `src/BodyCam/Pages/Main/Views/CameraTabView.xaml`
- `src/BodyCam.Tests/ViewModels/MainViewModelCameraButtonsTests.cs`

## Verification

- Open the camera action surface.
- Confirm only the top-level row is visible before selection.
- Tap `Look`.
- Confirm only `Overview`, `Summary`, and `Detail` are visible.
- Repeat for `Find`, `Read`, and `Scan`.
