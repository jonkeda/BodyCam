# Phase 3 - Settings UI

**Status:** Planned

## Goal

Integrate A9 camera setup into the current M37 Settings flow:

Settings > Devices -> **+ Connect Device** -> `AddDevicesPage` -> **Add A9 Camera**.

A9 configuration must not become a standalone inline section on
`DeviceSettingsPage`. That page should continue to show connected-device cards,
Source, Camera/Microphone/Speaker controls, and Button Mappings.

## UX

`AddDevicesPage` gains a second add-device card after **Add Cyan Glasses**.

```markui
# Add Devices

v------------------------------------------------------v
| #glasses Add Cyan Glasses                            |
| Connect Cyan glasses for camera, mic, speaker, and   |
| button input.                                        |
v------------------------------------------------------v

v------------------------------------------------------v
| #camera Add A9 Camera                                |
| Connect an A9/X5 IP camera over iLnkP2P/PPPP.        |
v------------------------------------------------------v
```

Tapping **Add A9 Camera** opens an A9 setup page.

```markui
# A9 Camera

v------------------------------------------------------v
| IP Address                                           |
| < 192.168.1.1                                    >   |
|                                                      |
| UID (optional)                                      |
| <                                                  > |
|                                                      |
| Username                                             |
| < admin                                          >   |
|                                                      |
| Password                                             |
| < admin                                          >   |
|                                                      |
| [ Test Connection ]                    [ Save ]      |
| Ready                                                |
v------------------------------------------------------v
```

## Implementation

1. Add **Add A9 Camera** to `AddDevicesViewModel.DeviceOptions`.
2. Add `AddA9CameraCommand` and route it to a new A9 setup page.
3. Add `A9CameraSettingsPage` and `A9CameraSettingsViewModel`.
4. Bind fields to:
   - `A9CameraIp`
   - `A9CameraUid`
   - `A9CameraUsername`
   - `A9CameraPassword`
5. Default username/password to `admin` / `admin` when the user leaves them blank.
6. Add Save and Test Connection commands.
7. Test Connection should validate settings by starting `A9CameraProvider` or a
   short-lived `A9Session`, then report success/failure in the page.
8. After save/test, either return to Settings > Devices or keep the user on the
   setup page with a clear connected/ready status.
9. Show A9 in the unified Connected Devices list as a camera card when
   `a9-camera` is configured and streaming/available.
10. Ensure `A9 Camera` can be selected from the Custom Camera Source picker once
    configured.
11. Decide whether to add an `A9 Camera` source profile. If added, keep it
    camera-only and preserve the current microphone/speaker choices.

## Files

- `src/BodyCam/ViewModels/Settings/AddDevicesViewModel.cs`
- `src/BodyCam/Pages/Settings/AddDevicesPage.xaml`
- `src/BodyCam/Pages/Settings/A9CameraSettingsPage.xaml`
- `src/BodyCam/Pages/Settings/A9CameraSettingsPage.xaml.cs`
- `src/BodyCam/ViewModels/Settings/A9CameraSettingsViewModel.cs`
- `src/BodyCam/ViewModels/Settings/DeviceViewModel.cs`
- `src/BodyCam/Pages/Settings/DeviceSettingsPage.xaml`
- `src/BodyCam/AppShell.xaml.cs`
- `src/BodyCam/ServiceExtensions.cs`
- `src/BodyCam.Tests/ViewModels/Settings/A9CameraSettingsViewModelTests.cs`
- `src/BodyCam.UITests/Pages/AddDevicesPage.cs`
- `src/BodyCam.UITests/Pages/A9CameraSettingsPage.cs`
- `src/BodyCam.UITests/Tests/SettingsPage/A9CameraSettingsTests.cs`

## Automation IDs

- `AddA9CameraButton`
- `A9CameraSettingsPage`
- `A9CameraIpEntry`
- `A9CameraUidEntry`
- `A9CameraUsernameEntry`
- `A9CameraPasswordEntry`
- `A9CameraTestConnectionButton`
- `A9CameraSaveButton`
- `A9CameraStatusLabel`
- `ConnectedDeviceCard`

## Acceptance Criteria

- Settings > Devices **+ Connect Device** still opens `AddDevicesPage`.
- `AddDevicesPage` shows **Add Cyan Glasses** and **Add A9 Camera**.
- Tapping **Add A9 Camera** opens the A9 setup page.
- Saving persists A9 settings through `ISettingsService`.
- Blank username/password persist or resolve as `admin` / `admin`.
- Test Connection reports clear success/failure without crashing the page.
- A configured, streaming A9 provider appears in Connected Devices as a camera
  card.
- `A9 Camera` is selectable in Custom Camera Source.
- No separate A9 configuration section is added to `DeviceSettingsPage`.
- Unit and UI tests cover the new settings flow.
