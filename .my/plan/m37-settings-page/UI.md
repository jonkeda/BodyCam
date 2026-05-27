# Device Settings Page

MarkUI 3 layout spec for Settings > Devices. The page is a narrow, scrollable settings
surface with connected devices first, source profile second, custom slot controls only
when needed, and button mappings anchored at the bottom.

## Main State: Custom Source

```markui
# Devices

+------------------------------------------------------+
| _Profile auto-switched to "HeyCyan Glasses"_         |
+------------------------------------------------------+

[ + Connect Device ]

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

---

## Source

< Custom                                             v >

---

## Camera Source

< Phone Camera                                      v >

[ Take Picture ]        [ Record Video ]

Captured 45,231 bytes in 820 ms
Latency: 820 ms

+------------------------------------------------------+
|                                                      |
|                     !==IMG==!                        |
|                                                      |
+------------------------------------------------------+

---

## Microphone

< HeyCyan Glasses Mic                               v >

HeyCyan microphone ready

[ Test Recording ]

Done - 24 chunks recorded

---

## Speaker

< Phone Speaker                                     v >

HeyCyan speaker ready

[ Test Sound ]

---

## Button Mappings

+------------------------------------------------------+
| v #glasses HeyCyan Glasses                           |
|                                                      |
|   Single Press    < Start Recording              v > |
|   Double Press    < Take Picture                 v > |
|   Long Press      < Toggle Mute                  v > |
+------------------------------------------------------+

+------------------------------------------------------+
| v #keyboard Keyboard Shortcuts                       |
|                                                      |
|   Ctrl+Shift+L    < Toggle Listening             v > |
|   Ctrl+Shift+P    < Take Picture                 v > |
+------------------------------------------------------+
```

## Profile State: Bundled Source

When Source is Phone, Laptop, HeyCyan Glasses, Meta Glasses, or Bluetooth Audio,
the individual Camera, Microphone, and Speaker pickers are hidden. The active bundle
is expressed by the selected source profile and the connected device summaries.

```markui
# Devices

[ + Connect Device ]

## Connected Devices

v------------------------------------------------------v
| > #glasses HeyCyan Glasses        #battery 72% #bolt |
v------------------------------------------------------v

v------------------------------------------------------v
| > #headphones AirPods Pro              #battery 65%  |
|   Bluetooth microphone + speaker                     |
v------------------------------------------------------v

v------------------------------------------------------v
| > #buttons BT Remote                                 |
|   Button device                                      |
v------------------------------------------------------v

v------------------------------------------------------v
| > #camera OBS Virtual Camera                         |
|   External camera                                    |
v------------------------------------------------------v

---

## Source

< HeyCyan Glasses                                  v >

+------------------------------------------------------+
| Camera       HeyCyan Glasses Camera                  |
| Microphone   HeyCyan Glasses Mic                     |
| Speaker      HeyCyan Glasses Speaker                 |
| Buttons      HeyCyan Glasses Button                  |
+------------------------------------------------------+

---

## Button Mappings

+------------------------------------------------------+
| > #glasses HeyCyan Glasses                           |
+------------------------------------------------------+
```

## Empty Devices State

```markui
# Devices

[ + Connect Device ]

## Connected Devices

v------------------------------------------------------v
| No external devices connected.                       |
| Phone/Laptop defaults are still available.           |
v------------------------------------------------------v

---

## Source

< Phone                                             v >
```

## Interaction Rules

- The auto-switch notification appears only after a fallback or smart profile switch
  and dismisses itself.
- Connected devices render as a vertical list of MarkUI 3 list-container cards using
  `v` as the corner character. The list must support at least glasses,
  headphones/audio devices, button/input devices, and cameras.
- Each device card is expandable. The collapsed card shows device type, name, battery,
  and charging state when available; the expanded card shows identifiers,
  firmware/hardware, media counts, status, profile slots, and device actions.
- Source profiles are bundled presets. Switching Source immediately applies the whole
  camera, microphone, speaker, and default-button bundle.
- Switching any individual Camera, Microphone, or Speaker picker changes Source to
  Custom.
- Individual pickers only show in Custom mode. Available external providers appear
  with device-specific names.
- Take Picture, Test Recording, and Test Sound must use the currently selected picker
  value immediately, even if the selection changed moments before tapping the test
  button.
- Record Video is visible only when the selected camera provider reports video support.
- Button mappings are independent of Source and always remain at the bottom. Render one
  expandable section per active button provider, then one row per supported gesture.
- Keyboard shortcut providers expose only single-press rows; no double-press or
  long-press rows are shown for keyboard shortcuts.
