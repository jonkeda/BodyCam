# RCA-02: Action Variants And Capture Circle

## Problem

When the user chooses a top-level camera action such as `Look`, `Find`, `Read`,
or `Scan`, the UI should behave the same way for every action:

1. The top-level action is selected.
2. A second row of sub-buttons appears for that selected action.
3. The user taps one sub-button to capture the current frame and run the
   command.

For `Look`, the expected sub-buttons are:

- `Overview`
- `Summary`
- `Detail`

The same interaction rule should apply to the other camera actions. Some
actions may have several variants, while actions with only one useful mode
should still show a single sub-button rather than executing immediately from
the top-level row.

The purple circular capture button can be removed from this camera-action
surface. In the M50 flow, the sub-button is the capture trigger.

## Current Implementation

The intended M50 path is generic:

1. Tap a top-level `CameraActionItemViewModel`.
2. `ActivateCameraActionAsync(...)` marks that action active.
3. It fills `ActiveCameraActionVariants`.
4. `CameraActionVariantRail` becomes visible through
   `HasActiveCameraActionVariants`.
5. Tapping a variant calls `ExecuteCameraActionVariantAsync(...)`, captures a
   still, adds it to the transcript, and runs the command with that frame.

The command/action metadata already supports this:

- `LookCommand` exposes `CameraActionVariants`.
- `ReadCommand` exposes `CameraActionVariants`, but currently only exposes one
  default `Read` variant even though it also has `Summary`, `Overview`, and
  `Full` prompt definitions.
- `ScanCommand` exposes one default `Scan` variant.
- `Find` is registered as a camera action backed by the `look` command with
  object-finding defaults.

There are still UI/path mismatches:

1. Older action paths execute commands directly through `LookCommand`,
   `ReadCommand`, `ScanCommand`, `FindCommand`, or
   `ExecuteCameraCommandAsync(...)`. Those paths do not select a
   `CameraActionItemViewModel`, so they do not populate
   `ActiveCameraActionVariants`.
2. Look's Overview variant is currently displayed as `Look`, because
   `LookCommandPrompts.Overview.DisplayName` is `"Look"`. That duplicates the
   top-level action label and makes the second row look missing or unchanged.
3. The circular `CaptureFrameButton` remains in `CameraTabView.xaml`, bound to
   `ShowManualCaptureButton`, even though variant buttons should be the only
   camera-action capture trigger.

## Root Cause

The UI still mixes two camera interaction models:

- The new generic M50 model: top-level action selects, sub-button executes.
- The previous model: top-level button executes a command immediately.
- The older manual capture model: a separate circle captures after a pending
  manual request.

Because the models are mixed, actions can behave differently depending on which
entry point triggered them. `Look` may select variants in the new rail but run
directly in the older path. `Read` and `Scan` may appear as if they do not have
sub-buttons because their variant list is only a single default option. `Find`
inherits the `look` command variants but needs to be presented as its own
selected action with the same second-row behavior.

## Fix Direction

1. Make the camera UI contract uniform:
   - Top-level `Look`, `Find`, `Read`, `Scan`, and future registered camera
     actions only select the action.
   - Sub-buttons execute the action.
   - There is no camera-action-specific direct execute path from the top-level
     rail.
2. Ensure every registered camera action produces visible variants:
   - `Look`: `Overview`, `Summary`, `Detail`.
   - `Find`: same generic selection/variant behavior, using its object-finding
     defaults.
   - `Read`: expose meaningful variants such as `Summary`, `Overview`, and
     `Full`, or intentionally show one `Read` sub-button if only one mode is
     desired.
   - `Scan`: show one `Scan` sub-button unless additional scan modes are added.
3. Rename Look's Overview variant display from `Look` to `Overview`.
4. Remove `CaptureFrameButton` from the M50 camera action surface.
5. Keep the shared variant execution behavior:
   - User taps a sub-button.
   - App captures the current frame.
   - Captured still is added to the transcript.
   - Command result replaces the busy dots.

## Implemented Fix

The M50 camera action surface now uses one interaction model for the visible
camera actions:

- `Look`, `Find`, `Read`, and `Scan` top-level UI commands select their
  registered camera action instead of executing immediately.
- The selected action fills `ActiveCameraActionVariants`; the second row is the
  execution/capture row.
- `Look` now displays `Overview`, `Summary`, and `Detail`.
- `Find` uses the same variant mechanism as its backing `look` command while
  preserving its object-finding defaults.
- `Read` now exposes `Summary`, `Overview`, and `Full` variants.
- `Scan` continues to expose a single `Scan` variant.
- The old circular `CaptureFrameButton` was removed from
  `CameraTabView.xaml`; variant buttons are the camera-action capture trigger.

## Test Coverage

Add or update focused view-model tests that prove:

- Constructor builds registered camera actions but does not preselect one.
- Activating each top-level action sets `ActiveCameraAction`.
- Activating each top-level action fills `ActiveCameraActionVariants`.
- Look variant labels are `Overview`, `Summary`, and `Detail`.
- Read and Scan follow the same select-then-sub-button execution model.
- Executing any variant captures one frame, adds the still to the transcript,
  and runs the command with that same frame.

For UI/XAML verification, confirm `CameraTabView.xaml` no longer exposes the
old circular `CaptureFrameButton` in the M50 action surface.

## Files to Check

- `src/BodyCam/ViewModels/MainViewModel.cs`
- `src/BodyCam/Pages/Main/Views/CameraTabView.xaml`
- `src/BodyCam/Services/Camera/Commands/LookCommand.cs`
- `src/BodyCam/Services/Camera/Commands/ReadCommand.cs`
- `src/BodyCam/Services/Camera/Commands/ScanCommand.cs`
- `src/BodyCam/ServiceExtensions.cs`
- `src/BodyCam.Tests/ViewModels/MainViewModelCameraButtonsTests.cs`

## Verification

- Start the app.
- Open the camera action surface.
- Tap each top-level action: `Look`, `Find`, `Read`, and `Scan`.
- Confirm each action highlights/selects and shows its second-row sub-buttons.
- Confirm top-level action buttons do not immediately execute the command.
- Confirm the purple circular capture button is gone.
- Tap each sub-button and verify a captured still plus command result appears in
  the transcript.
