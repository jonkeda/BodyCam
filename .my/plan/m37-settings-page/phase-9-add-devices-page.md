# Phase 9 - Add Devices Page

**Status:** Implemented

## Goal

Change the **+ Connect Device** action on Settings > Devices so it opens a dedicated
**AddDevices** page instead of jumping directly into the glasses flow.

The first supported add-device option is **Add Cyan Glasses**. Tapping it opens the
existing Cyan glasses connection flow.

## UX

Settings > Devices keeps the same top action, but the action becomes a device-picker
entry point.

```markui
# Devices

[ + Connect Device ]

## Connected Devices

v------------------------------------------------------v
| > #glasses HeyCyan Glasses        #battery 72% #bolt |
v------------------------------------------------------v
```

Tapping **+ Connect Device** opens AddDevices:

```markui
# Add Devices

v------------------------------------------------------v
| #glasses Add Cyan Glasses                            |
| Connect Cyan glasses for camera, mic, speaker, and   |
| button input.                                        |
v------------------------------------------------------v
```

Tapping **Add Cyan Glasses** opens the existing Cyan glasses page/flow.

## Implementation

1. Add a new Settings page named `AddDevicesPage`.
2. Add an `AddDevicesViewModel` with an `AddCyanGlassesCommand`.
3. Register the page and route in app startup/Shell routing.
4. Change `DeviceViewModel.ConnectGlassesCommand` or replace it with a
   `ConnectDeviceCommand` that navigates to `AddDevicesPage`.
5. On `AddDevicesPage`, render one MarkUI 3-style list-container card/button for
   **Add Cyan Glasses**.
6. Wire **Add Cyan Glasses** to the existing Cyan glasses route (`glasses`) so the
   existing connection behavior is reused.
7. Keep room in the page structure for later add-device options such as headphones,
   cameras, button remotes, and Meta glasses.

## Files

- `src/BodyCam/AppShell.xaml.cs`
- `src/BodyCam/ServiceExtensions.cs`
- `src/BodyCam/Pages/Settings/DeviceSettingsPage.xaml`
- `src/BodyCam/ViewModels/Settings/DeviceViewModel.cs`
- `src/BodyCam/Pages/Settings/AddDevicesPage.xaml`
- `src/BodyCam/Pages/Settings/AddDevicesPage.xaml.cs`
- `src/BodyCam/ViewModels/Settings/AddDevicesViewModel.cs`
- `src/BodyCam.Tests/ViewModels/Settings/AddDevicesViewModelTests.cs`
- `src/BodyCam.UITests/Pages/AddDevicesPage.cs`
- `src/BodyCam.UITests/Tests/SettingsPage/AddDevicesTests.cs`

## Acceptance Criteria

- Settings > Devices **+ Connect Device** opens the AddDevices page.
- AddDevices shows a first option labeled **Add Cyan Glasses**.
- The Add Cyan Glasses option is a list-container/card-style button, not a plain
  inline text link.
- Tapping Add Cyan Glasses opens the existing Cyan glasses connection flow.
- The route is registered and works from Shell navigation.
- Stable automation IDs exist for the page, list, and Add Cyan Glasses option.
- Unit tests cover the Add Cyan Glasses command route.
- UI tests cover Settings > Devices to AddDevices navigation and the Add Cyan
  Glasses option existence.
