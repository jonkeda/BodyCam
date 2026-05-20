# RCA-03: Microphone Test Recording Should Use Selected Source

## Problem

### 3a. Test recording does not use the selected microphone

**Current:** When the user selects a different microphone in the picker and clicks "Test Recording", the recording may use the previously active microphone rather than the one currently selected in the UI.

**Expected:** The test recording action must capture audio from whichever microphone provider is currently selected in the picker, even if the selection was just changed.

## Root Cause

The `TestRecordingCommand` in `DeviceViewModel` likely uses `_audioInputManager.Active` (the manager's current active provider) rather than the ViewModel's `SelectedAudioInputProvider`. If the user changes the picker selection but the async `SetActiveAsync` hasn't completed or the test fires before the manager switches, the wrong mic is used.

Alternatively, the test may directly call into a recording service that independently picks a mic without consulting the selected provider.

## Fix

1. Ensure `TestRecordingAsync()` explicitly starts recording from the currently selected `IAudioInputProvider` (ViewModel's `SelectedAudioInputProvider`), not just `_audioInputManager.Active`
2. If the recording service requires a provider ID, pass the selected provider's ID at invocation time
3. Add a test verifying that changing the picker and immediately testing uses the new selection

## Files to Change

- `src/BodyCam/ViewModels/Settings/DeviceViewModel.cs` — `TestRecordingAsync()` method: pass selected provider to recording logic
- Possibly `src/BodyCam/Services/Audio/AudioInputManager.cs` — if test method needs to accept an explicit provider ID override
