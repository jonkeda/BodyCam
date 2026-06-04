# RCA-01: Startup Camera Buttons Visibility

## Problem

On app startup, the camera button area is visible below the message entry and
shows the compact camera action buttons such as `Look`, `Find`, `Read`, and
`Scan`.

This can look like the camera sub-buttons are already expanded before the user
has selected an action.

Expected startup behavior must be explicit:

- The top-level camera actions may be visible at startup if the camera action
  rail is intended to be a persistent launch surface.
- The second-level sub-buttons or variants for a selected action must not be
  visible until the user taps a top-level camera action.

## Current Implementation

`MainViewModel` calls `InitializeCameraActions()` in the constructor. That
populates `CameraActions` immediately from the registered camera action and
camera command metadata.

`CameraTabView.xaml` binds the outer camera action panel to:

```xml
IsVisible="{Binding ShowCameraActionsSection}"
```

`ShowCameraActionsSection` is currently:

```csharp
public bool ShowCameraActionsSection => HasCameraActions || ShowInlineCameraPreview;
```

Because `HasCameraActions` becomes true during construction, the panel becomes
visible on first render even when no camera preview is showing and no action is
selected.

The actual variant rail is separately guarded by:

```xml
IsVisible="{Binding HasActiveCameraActionVariants}"
```

and `ActiveCameraActionVariants` starts empty. It is only filled by
`ActivateCameraActionAsync(...)`.

## Root Cause

The startup visibility is caused by mixing two meanings in
`ShowCameraActionsSection`:

1. `HasCameraActions` means registered camera actions exist.
2. `ShowCameraActionsSection` means the camera action surface should be shown.

Since `HasCameraActions` is true as soon as the view model is constructed, the
camera action surface is shown at app startup by design. This is why the
top-level `Look` / `Find` / `Read` / `Scan` row appears immediately.

The second-level sub-button state itself is not preselected: `ActiveCameraAction`
is null and `ActiveCameraActionVariants` is empty at startup.

## Fix Direction

If top-level camera actions should be visible at startup, keep the current
`HasCameraActions` path and make the distinction clear in tests and naming:

- `CameraActionRail` is the top-level startup rail.
- `CameraActionVariantRail` is the selected action's sub-button rail.
- Add or keep a constructor test proving `ActiveCameraAction` is null and
  `ActiveCameraActionVariants` is empty on startup.

If no camera buttons should be visible at startup, split registration state from
display state:

- Add a display property such as `ShowCameraActionRail`.
- Make `ShowCameraActionsSection` depend on display state, not only on
  `HasCameraActions`.
- Update `CameraActionRail.IsVisible` to bind to the new display property.
- Add a startup test asserting the action/variant rails are hidden until the
  user opens the camera action surface or selects an action.

## Implemented Fix

Use the second approach. Registered camera actions are still built during
`MainViewModel` construction, but registration no longer makes the camera
section visible.

- `ShowCameraActionsSection` now depends on the camera surface state:
  `ShowInlineCameraPreview || ShowSnapshot`.
- `ShowCameraActionRail` controls the top-level action rail and only returns
  true when registered camera actions exist and the inline camera preview is
  open.
- `CameraActionRail.IsVisible` now binds to `ShowCameraActionRail`.
- Startup tests assert that actions are registered, but both the camera section
  and action rail are hidden until an action opens the camera surface.

## Files to Check

- `src/BodyCam/ViewModels/MainViewModel.cs`
- `src/BodyCam/Pages/Main/Views/CameraTabView.xaml`
- `src/BodyCam.Tests/ViewModels/MainViewModelCameraButtonsTests.cs`

## Verification

- Start the app fresh.
- Confirm whether the visible startup row is the intended top-level
  `CameraActionRail`.
- Confirm `CameraActionVariantRail` is hidden until a top-level action is
  selected.
- Tap `Look`, `Read`, or `Scan` and verify only that selected action's variants
  appear.
