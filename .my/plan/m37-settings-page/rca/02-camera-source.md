# RCA-02: Camera Source Issues

## Problem

Multiple issues with camera source selection and testing:

### 2a. Laptop camera should be selectable on laptop

**Current:** On Windows (laptop), the camera picker may not show the built-in laptop camera as an option.

**Expected:** The laptop's built-in camera should appear as a selectable option in the Camera source picker on Windows.

### 2b. Phone camera should be selectable on phone

**Current:** On Android/iOS, the phone camera should be available. Verify it appears in the picker.

**Expected:** The device's rear/front camera should appear as a selectable option on mobile.

### 2c. Other cameras only selectable when connected

**Current:** Camera providers like "HeyCyan Glasses Camera" may appear in the picker even when the glasses are disconnected.

**Expected:** Camera sources for external devices should only appear (or be enabled) when the device is actually connected. Use `IsAvailable` to filter or disable them in the picker UI.

### 2d. Test capture should use the selected camera source

**Current:** When the user changes the camera source and clicks "Test Capture", it may not use the newly selected provider — it could still use the previously active camera.

**Expected:** The test capture action must use whichever camera is currently selected in the picker, even if the selection just changed.

### 2e. "Test Capture" should be renamed to "Take Picture"

**Current:** Button text says "Test Capture".

**Expected:** Button text should say "Take Picture".

### 2f. "Record Video" visibility depends on camera capabilities

**Current:** Record Video may always be visible regardless of whether the selected camera supports video recording.

**Expected:** "Record Video" button should only be visible when the selected camera provider supports video recording. For example, HeyCyan glasses cannot record video, so the button should be hidden when HeyCyan is the selected camera.

## Root Cause

1. Camera providers are registered unconditionally — no filtering by `IsAvailable` in the picker
2. The test capture command may use `_cameraManager.Active` rather than the picker's selected value at the moment of click
3. Button text is hardcoded as "Test Capture"
4. No capability check (e.g. `SupportsVideoRecording`) on the camera provider interface to conditionally show "Record Video"

## Fix

1. Filter or disable unavailable cameras in the picker (bind `IsEnabled` to `IsAvailable`)
2. Ensure `TestCaptureCommand` uses the currently selected provider (from ViewModel, not manager's active)
3. Rename button text from "Test Capture" to "Take Picture"
4. Add `bool SupportsVideoRecording` to `ICameraProvider` interface; bind "Record Video" button visibility to it
5. HeyCyan camera provider returns `SupportsVideoRecording = false`

## Files to Change

- `src/BodyCam/Services/Camera/ICameraProvider.cs` — add `SupportsVideoRecording` property
- `src/BodyCam/Services/Camera/PhoneCameraProvider.cs` — `SupportsVideoRecording = true`
- `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanCameraProvider.cs` — `SupportsVideoRecording = false`
- `src/BodyCam/ViewModels/Settings/DeviceViewModel.cs` — fix test capture to use selected provider; add `ShowRecordVideo` property
- `src/BodyCam/ViewModels/Settings/GlassesCameraSectionViewModel.cs` — rename command/text
- `src/BodyCam/Pages/Settings/DeviceSettingsPage.xaml` — rename button, bind Record Video visibility, disable unavailable cameras
