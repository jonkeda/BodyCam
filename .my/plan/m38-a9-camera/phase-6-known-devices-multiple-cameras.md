# Phase 6 - Known A9 Devices

**Status:** Planned

## Goal

Support more than one A9/X5 camera without overloading the single
`A9CameraIp` setting.

The user should be able to save discovered or manually configured A9 cameras,
choose one as active, and see saved cameras in the Settings connected-device
experience.

## UX

```markui
# A9 Camera

v------------------------------------------------------v
| Saved Cameras                                       |
|                                                      |
| v--------------------------------------------------v |
| | v #camera Garage A9                  192.168.1.1   |
| |   UID A9X542TEST                                  |
| |   [ Use ]                            [ Remove ]   |
| v--------------------------------------------------v |
|                                                      |
| v--------------------------------------------------v |
| | > #camera Desk A9                    10.0.0.42     |
| v--------------------------------------------------v |
v------------------------------------------------------v
```

Connected Devices should show the active or streaming A9 camera as a camera card.

## Implementation

1. Introduce a persistent known-device model for A9 cameras.
2. Store the list in `DeviceSettings.KnownDevices` or a new A9-specific settings
   JSON field, depending on which fits the M37 settings model best.
3. Preserve backward compatibility by migrating existing flat settings:
   - `A9CameraIp`
   - `A9CameraUid`
   - `A9CameraUsername`
   - `A9CameraPassword`
4. Add a display name field for saved cameras.
5. Add Save As New, Use, Remove, and Update actions in `A9CameraSettingsViewModel`.
6. Decide provider model:
   - Preferred first step: one `A9CameraProvider` bound to the active saved A9
     camera.
   - Later option: one provider id per saved camera, such as `a9-camera:{uid}`.
7. Show saved cameras in A9 setup as list-container cards.
8. Show the active/streaming camera in the unified Connected Devices list.

## Files

- `src/BodyCam/Models/DeviceSettings.cs`
- `src/BodyCam/Models/KnownDevice.cs`
- `src/BodyCam/Services/KnownDeviceService.cs`
- `src/BodyCam/Services/Camera/A9/A9CameraProvider.cs`
- `src/BodyCam/ViewModels/Settings/A9CameraSettingsViewModel.cs`
- `src/BodyCam/ViewModels/Settings/DeviceViewModel.cs`
- `src/BodyCam/Pages/Settings/A9CameraSettingsPage.xaml`
- `src/BodyCam.Tests/ViewModels/Settings/A9CameraSettingsViewModelTests.cs`
- `src/BodyCam.Tests/ViewModels/Settings/DeviceViewModelTests.cs`

## Acceptance Criteria

- Existing single-camera A9 settings migrate into one saved A9 device.
- Users can save multiple A9 camera entries.
- Users can select which saved A9 camera is active.
- Removing a saved A9 camera does not break other saved devices.
- The active A9 camera remains selectable in Custom Camera Source.
- Connected Devices shows a useful A9 camera card for the active/streaming
  provider.
- Unit tests cover migration, add/update/remove/use behavior, and active-provider
  selection.
