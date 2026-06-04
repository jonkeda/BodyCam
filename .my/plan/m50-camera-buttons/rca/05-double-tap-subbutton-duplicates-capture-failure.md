# RCA-05: Double-Tapping Sub-Button Adds Duplicate Capture Failure

## Problem

When the user double-taps a camera action sub-button such as `Overview`,
the transcript can show duplicate failure rows:

```text
AI: Camera capture failed.
AI: Camera capture failed.
```

This is not caused by clicking the message row. It happens because the
sub-button was pressed twice.

## Expected Behavior

The first sub-button tap should claim the action.

Additional taps on the same camera action while the first tap is being handled
should be ignored. A failed capture should produce at most one transcript
message for that action attempt.

## What Is Going Wrong

`ExecuteCameraActionVariantAsync(...)` has an `_isExecutingCameraActionVariant`
guard, and each variant button also uses `AsyncRelayCommand`. Those prevent
simple concurrent re-entry while the action is still running.

The problem is the fast-failure path:

1. The first tap starts the camera action.
2. Frame capture fails quickly.
3. The catch block adds `AI: Camera capture failed.`
4. The `finally` block resets `_isExecutingCameraActionVariant = false`.
5. A second tap event from the user's double-tap can then be processed after
   the first failure has already completed.
6. Because the guard has been reset, the second tap is treated as a brand-new
   action attempt and adds the same failure row again.

So the duplicate row is not an AI response and not a transcript rendering bug.
It is the UI accepting two physical taps as two separate camera action
attempts because the first attempt ends very quickly.

## Root Cause

The camera sub-button flow has a concurrency guard, but it does not have a
tap debounce or one-shot claim for the visible sub-button interaction.

The guard answers: "Is a camera variant running right now?"

It does not answer: "Did the user just tap this sub-button and should queued
double-tap events be ignored?"

For fast failures, those are different things.

## Fix Direction

Add a short-lived double-tap suppression for camera action variants.

Reasonable options:

1. Keep `_isExecutingCameraActionVariant` true until the next UI turn after
   the camera surface has been hidden.
2. Track the last variant tap timestamp/key and ignore repeated taps for a
   short debounce window, for example 500-750 ms.
3. Disable variant button commands immediately at tap time and raise
   `CanExecuteChanged` before any camera work starts.

The safest UX fix is a small debounce at the view-model level, because it
does not depend on MAUI button visual state updating before the second tap
event is delivered.

## Verification

- Open the camera action surface.
- Select `Look`.
- Double-tap `Overview`.
- Force or reproduce a capture failure.
- Confirm only one `AI: Camera capture failed.` row is added.
- Confirm normal single-tap success still captures one frame and runs one
  command.
- Repeat for `Read`, `Find`, and `Scan`.

