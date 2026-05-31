# M41 - USB Camera Roadmap

**Status:** Windows provider implemented

## Purpose

M41 should stay small. Phase 1A proved the device is a standard Windows USB
camera, and Phase 3 added the Windows BodyCam provider.

The boundaries are:

1. USB camera to Windows.
2. Windows to saved image.
3. Windows to BodyCam provider.
4. Deferred: USB camera to Android.
5. Deferred: USB camera to iOS.

Because the human plugged the USB camera directly into Windows, the direct
Windows probe in [Phase 1A](./phase-1a-windows-direct-usb-uvc-probe.md) is now
the primary route.

Current evidence says Windows sees the device as a standard `usbvideo` camera:

```text
Friendly name: HD camera
VID/PID: VID_349C&PID_0411
Formats: YUY2 640x480 / 320x240
Capture: Windows C# probe saved a valid JPEG still
```

## Recommended Order

### 1. Direct Windows USB Probe

Phase 1A is complete.

Goal:

- See how Windows classifies the device.
- Capture VID/PID and friendly names.
- Check whether Windows exposes it as a standard UVC camera.
- Try one C# still-image capture.

Outcome:

- Direct Windows capture works, so M41 should prioritize a direct Windows
  `UsbCameraProvider`.

### 2. Add Windows BodyCam Provider

Phase 3 is implemented.

Goal:

- Promote the working MediaCapture probe into a reusable Windows client.
- Add `UsbCameraProvider`.
- Register it in BodyCam.
- Make it selectable as **USB Camera**.

Outcome:

- BodyCam can use the USB camera as a camera source through the existing camera
  abstraction.

### 3. Android Direct USB, Deferred

Goal:

- Keep the camera physically connected to Android USB.
- Capture through Camera2 external camera if available, otherwise USB Host/UVC.
- Do not bridge the feed to Windows.
- Create a fresh phase doc only when this branch becomes active.

### 4. iOS Direct USB, Deferred

Goal:

- Check current Apple platform rules and APIs before committing to direct iOS
  USB camera support.
- Create a fresh feasibility phase only when this branch becomes active.

Outcome:

- Clear yes/no/maybe decision for iOS, with the likely implementation path.

## Decision Tree

```text
Start
  |
  v
Phase 1A: Windows captures JPEG
  |
  +-- yes -------------------------------> Phase 3 BodyCam provider
  |
  +-- Android requested later -----------> new Android direct USB phase
  |
  +-- iOS requested later ---------------> new iOS direct USB feasibility phase
```

## Required vs Optional

Required:

- Phase 1A
- Phase 3

Optional until proven useful:

- Android direct USB support
- iOS direct USB support

## Current Recommendation

Do next:

1. Run the BodyCam UI against the plugged-in USB camera and test
   Settings > Devices > Add USB Camera > Test Capture.
2. Select **USB Camera** as the camera source and use the existing Take Picture
   flow.
3. Add Android direct USB capture/provider only after Windows UI behavior is
   confirmed.
4. Revisit iOS direct USB only after the platform feasibility answer is clear.
