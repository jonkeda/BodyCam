# RCA-04: Speaker Test Sound Should Use Selected Source

## Problem

### 4a. Test sound does not use the selected speaker

**Current:** When the user selects a different speaker/output in the picker and clicks "Test Sound", the audio may play through the previously active output rather than the one currently selected in the UI.

**Expected:** The test sound action must play audio through whichever speaker provider is currently selected in the picker, even if the selection was just changed.

## Root Cause

The `TestSoundAsync()` method in `DeviceViewModel` uses `_audioOutputManager.Active` to get the provider for playback. If the user changes the picker but the manager's active hasn't switched yet (or there's a race), the wrong speaker is used.

From the existing code:
```csharp
private async Task TestSoundAsync()
{
    var provider = _audioOutputManager.Active;  // ← uses manager's active, not picker selection
    if (provider is null) return;
    ...
}
```

## Fix

1. Change `TestSoundAsync()` to use `SelectedAudioOutputProvider` (the ViewModel's property bound to the picker) instead of `_audioOutputManager.Active`
2. If the provider requires `StartAsync` before playback, ensure it's initialized for the selected provider
3. Add a test verifying that changing the picker and immediately testing plays through the new selection

## Files to Change

- `src/BodyCam/ViewModels/Settings/DeviceViewModel.cs` — `TestSoundAsync()` method: use `SelectedAudioOutputProvider` instead of `_audioOutputManager.Active`
