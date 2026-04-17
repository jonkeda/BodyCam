# RCA: Camera Tab View Not Appearing

**Date**: 2026-04-17  
**Status**: FIXED  
**Symptom**: Clicking the Camera tab button sometimes shows nothing — camera view doesn't appear.

## Summary

The binding logic was correct. `ShowCameraTab` properly becomes `true` when the Camera tab is clicked. The real issue: the native `CameraView` control renders at 0x0 when the camera preview stream isn't started. It only appeared when a session was already active.

## Root Cause: CameraView Not Started on Tab Switch

`CameraView.StartCameraPreview()` was only called in `SetLayerAsync()` when escalating to `ActiveSession`. Without an active session, switching to the Camera tab made the grid "visible" but the CameraView had zero intrinsic size — the entire content area collapsed.

**Why it "sometimes" worked**: If the user activated a session first (Active/💬), then clicked Camera tab, the preview was already started and the CameraView had non-zero layout size.

**Location**: `MainViewModel.cs` — `SwitchToCameraCommand` (line ~60)

## Fix Applied

Changed `SwitchToCameraCommand` from `RelayCommand` to `AsyncRelayCommand` that starts the camera preview on tab switch. Also added stop on switch back to save resources:

```csharp
SwitchToTranscriptCommand = new RelayCommand(() =>
{
    ShowTranscriptTab = true;
    if (CurrentLayer != ListeningLayer.ActiveSession)
        _cameraView?.StopCameraPreview();
});
SwitchToCameraCommand = new AsyncRelayCommand(async () =>
{
    ShowTranscriptTab = false;
    if (_cameraView is not null)
        await _cameraView.StartCameraPreview(CancellationToken.None);
});
```

Also added a `CameraPlaceholder` Label in `MainPage.xaml` so the camera Grid always has non-zero layout size (the native `CameraView` control doesn't create a UIA automation peer).

## FlaUI/UIA Findings

During testing, discovered that:
- MAUI `Frame` elements don't create UIA automation peers
- MAUI `Grid` elements containing only native controls (CameraView) may have zero UIA bounds
- `BoxView` doesn't create UIA automation peers either
- `Label` elements reliably create UIA peers and can serve as sentinel elements for visibility testing
|------|-------------|
| 1 | User clicks Camera tab → `SwitchToCameraCommand` |
| 2 | Sets `ShowTranscriptTab = false` |
| 3 | `SetProperty` fires `PropertyChanged("ShowTranscriptTab")` |
| 4 | Setter also fires `OnPropertyChanged(nameof(ShowCameraTab))` |
| 5 | XAML bindings update: Frame hides, Camera Grid shows |

The property notification chain is correct. The camera Grid does become visible — it just has nothing to show because the camera isn't started, and may have zero layout size.

## Verification Steps

1. Apply both fixes
2. Launch app, click Camera tab without starting a session
3. Camera preview should appear immediately
4. Switch back to Transcript, then back to Camera — preview should still work
