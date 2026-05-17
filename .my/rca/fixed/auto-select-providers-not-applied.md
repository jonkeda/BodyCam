# RCA: Glasses connect does not auto-select microphone & speaker

**Date**: 2026-05-16
**Severity**: UX bug — user must manually change Microphone & Speaker after glasses connect
**Status**: Fixed

## Symptom

After connecting HeyCyan glasses from the Devices page, the Microphone and Speaker pickers still show the platform defaults ("platform" mic, "windows-speaker" speaker) instead of switching to the glasses ("heycyan-glasses").

## Root Cause

Two bugs in `DeviceViewModel.AutoSelectGlassesProviders()`:

### Bug 1 — Fire-and-forget `SetActiveAsync` with immediate `OnPropertyChanged`

```csharp
// BROKEN — fire-and-forget, PropertyChanged fires before active is updated
_ = _audioInputManager.SetActiveAsync(id);
_ = _audioOutputManager.SetActiveAsync(id);

OnPropertyChanged(nameof(SelectedAudioInputProvider));   // reads OLD active
OnPropertyChanged(nameof(SelectedAudioOutputProvider));  // reads OLD active
```

`SetActiveAsync` is async because it must `await _active.StopAsync()` on the
previous provider before setting `_active` to the new one. The `_ =` discards
the Task, so `OnPropertyChanged` fires immediately while `_active` still points
to the old provider. The UI binding reads the stale value. When the Task
eventually completes and `_active` is updated, no `PropertyChanged` fires.

### Bug 2 — `RevertToDefaultProviders` uses wrong speaker ID on Windows

```csharp
_ = _audioOutputManager.SetActiveAsync("platform");  // ← wrong
```

The Windows speaker provider ID is `"windows-speaker"`, not `"platform"`.
`SetActiveAsync` finds no provider matching `"platform"` and silently no-ops.
This means disconnecting glasses would leave the speaker stuck on
`"heycyan-glasses"`.

(iOS/Android use `"phone-speaker"`, so `"platform"` is wrong there too.)

## Why It Wasn't Caught

- `AutoSelectGlassesProviders` was added in Phase 8 but never integration-tested
  with a real glasses connect/disconnect cycle.
- The unit tests create a DeviceViewModel but don't exercise the state-change
  handler with async provider switching.
- `SetActiveAsync` returning early (provider not found) is silent — no log, no
  exception.

## Fix

1. Make `AutoSelectGlassesProviders` and `RevertToDefaultProviders` async.
2. Await all `SetActiveAsync` calls before firing `OnPropertyChanged`.
3. Fix `RevertToDefaultProviders` to use the first available non-glasses
   provider ID from each manager instead of hardcoding platform IDs.
4. Update `OnGlassesStateChanged` to use `async () =>` in
   `MainThread.BeginInvokeOnMainThread`.

## Files Changed

- `src/BodyCam/ViewModels/Settings/DeviceViewModel.cs`
