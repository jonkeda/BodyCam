# Phase 8 - Connected Devices List

**Status:** Implemented

## Goal

Change Settings > Devices so **Connected Devices** is one unified vertical list of
device cards instead of a special inline glasses panel plus a separate collection for
other devices.

The list must support at least these device categories:

- Glasses: HeyCyan now, Meta later
- Headphones/audio devices: Bluetooth, wired, platform audio routes
- Button/input devices: glasses buttons, BT remotes, keyboard shortcuts, clickers
- Cameras: phone/laptop camera, USB camera, virtual camera, glasses camera

## UX

Connected Devices appears directly under **+ Connect Device**. Every connected device
uses the same card/list-container shape, with `v` corners in MarkUI mockups.

```markui
## Connected Devices

v------------------------------------------------------v
| v #glasses HeyCyan Glasses        #battery 72% #bolt |
|   Camera + mic + speaker + buttons                   |
|                                                      |
|   MAC          AA:BB:CC:DD:EE:FF                     |
|   Firmware     1.2.3                                 |
|   Hardware     rev-B                                 |
|   Media        #camera 12   #video 3   #mic 5        |
|                                                      |
|   [ Disconnect ]                         [ Remove ]  |
v------------------------------------------------------v

v------------------------------------------------------v
| > #headphones AirPods Pro              #battery 65%  |
|   Bluetooth microphone + speaker                     |
|   Slots: Microphone, Speaker                         |
v------------------------------------------------------v

v------------------------------------------------------v
| > #buttons BT Remote                                 |
|   Button device - 3 buttons                          |
|   Gestures: press, double press, long press          |
v------------------------------------------------------v

v------------------------------------------------------v
| > #camera OBS Virtual Camera                         |
|   External camera - photo + video                    |
|   Slot: Camera Source                                |
v------------------------------------------------------v
```

## Implementation

1. Replace the special glasses-only XAML section with a single connected-device
   `CollectionView`.
2. Add glasses to the same `ConnectedDevices` list currently used for Bluetooth audio
   devices.
3. Extend `ConnectedDeviceInfo` or introduce a `ConnectedDeviceCardViewModel` with:
   `DeviceId`, `DisplayName`, `DeviceType`, `Icon`, `Summary`, `BatteryPct`,
   `IsCharging`, `IsExpanded`, `DetailRows`, `SlotTags`, `CanDisconnect`, and
   optional commands.
4. Render cards by `DeviceType`, but keep one shared card template where possible.
   Device-specific detail rows should come from the view model, not XAML branches.
5. Include button-only and camera-only devices, even when they do not participate in
   the active Source profile.
6. Keep Button Mappings as its own bottom section. The Connected Devices card should
   summarize button capabilities, not duplicate the mapping UI.

## Files

- `src/BodyCam/Pages/Settings/DeviceSettingsPage.xaml`
- `src/BodyCam/ViewModels/Settings/DeviceViewModel.cs`
- `src/BodyCam/Models/ConnectedDeviceInfo.cs`
- `src/BodyCam.Tests/ViewModels/Settings/DeviceViewModelTests.cs`
- `src/BodyCam.UITests/Pages/DeviceSettingsPage.cs`
- `src/BodyCam.UITests/Tests/SettingsPage/DeviceSettingsTests.cs`

## Acceptance Criteria

- Connected Devices is always one visible list area.
- Glasses, headphones/audio devices, button/input devices, and cameras can all appear
  as cards in that list.
- Glasses no longer render through a separate hardcoded panel.
- Cards can be collapsed or expanded independently.
- The collapsed card shows type, name, battery, and charging state when available.
- The expanded card shows useful details and device actions for that device type.
- Source profile behavior is unchanged.
- Button mappings remain at the bottom and continue to render dynamically.
- Unit tests cover list composition for glasses, audio, button, and camera devices.
- UI tests identify the list and at least one card by stable automation IDs.
