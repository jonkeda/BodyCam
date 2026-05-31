# Phase 1A - Windows Direct USB/UVC Probe

**Status:** Complete

## Goal

Check whether the USB camera can be used directly from Windows when plugged
into the laptop.

This phase exists because the device is a standard USB camera on Windows, so the
first BodyCam integration should be direct USB.

## Starting Point

The human plugged the USB camera into the Windows laptop.

M41 will use direct USB per platform:

```text
USB camera -> Windows -> BodyCam Windows
USB camera -> Android -> BodyCam Android
USB camera -> iOS -> BodyCam iOS, if feasible
```

This phase tests the first topology:

```text
USB camera -> Windows -> BodyCam
```

## What We Need To Learn

- Does Windows detect the device?
- Does Windows classify it as a camera, image device, USB device, or unknown
  device?
- What are the vendor id and product id?
- Does the device expose a standard UVC camera interface?
- Does Windows list usable capture formats such as MJPEG or YUY2?
- Can a C# probe capture one still JPEG from it?
- Can the existing BodyCam camera abstraction use it without a custom protocol?

## Probe Order

1. Enumerate PnP devices from Windows.
2. Search for camera, image, USB video, UVC, and USB-camera-related
   names.
3. Record VID/PID and friendly names.
4. Try OS-level camera enumeration through available .NET/Windows APIs.
5. Capture one still image with a small C# probe or an existing BodyCam camera
   path.
6. If direct capture works, promote direct Windows USB mode to the first
   provider implementation.

## Candidate Commands

```powershell
Get-PnpDevice -PresentOnly
Get-PnpDevice -Class Camera
Get-PnpDevice -Class Image
Get-CimInstance Win32_PnPEntity
```

## Acceptance Criteria

- [x] Windows device visibility is documented.
- [x] VID/PID and friendly name are recorded if available.
- [x] The phase clearly says whether the USB camera looks like a standard USB/UVC
  camera on Windows.
- [x] If possible, one Windows-captured image artifact is saved.
- [x] If not possible, the blocker is specific enough to decide what direct USB
  fix is needed.

## Outcome Template

```text
Date:
Device visible:
Windows class:
Friendly name:
VID/PID:
Likely protocol:
Capture result:
Next step:
```

## Risks

- Windows may need a driver before the camera appears as a capture device.
- The device may appear under a generic USB name rather than a clear camera
  name.
- Some USB cameras advertise UVC but only support formats that the current
  BodyCam capture path does not yet handle.
- If the camera needs extra LEDs or controls, those may require a separate
  USB control phase later.

## Outcome - 2026-05-30

Direct Windows USB works.

Device evidence:

```text
Device visible: yes
Windows class: Camera
Friendly name: HD camera
VID/PID: VID_349C&PID_0411
Composite device: USB\VID_349C&PID_0411\20201212000000
Camera interface: USB\VID_349C&PID_0411&MI_00\...
Windows service: usbvideo
Likely protocol: standard USB Video Class / UVC
```

C# probe evidence:

```text
Tool: tools/BodyCam.UsbCameraProbe
Command: dotnet run --project tools\BodyCam.UsbCameraProbe\BodyCam.UsbCameraProbe.csproj -- enumerate
VideoCapture device name: HD camera
Device id: \\?\USB#VID_349C&PID_0411&MI_00#6&297fdf76&0&0000#{e5323777-f976-4f5b-9b55-b94699c46e44}\GLOBAL
Formats: YUY2 only
Resolutions: 640x480 and 320x240
Frame rates: 5, 15, and 30 fps
```

Capture evidence:

```text
Command: dotnet run --project tools\BodyCam.UsbCameraProbe\BodyCam.UsbCameraProbe.csproj -- capture --vidpid "VID_349C&PID_0411" --output .my\plan\m41-usb-camera\captures\windows-direct-usb\usb-camera-windows-still.jpg
Output: .my\plan\m41-usb-camera\captures\windows-direct-usb\usb-camera-windows-still.jpg
Bytes: 79628
JPEG: true
SHA256: 2373D5E6C5E01A20DE31179DCE55477B537E3C38B649D2CF618E0B2141011273
Visual check: valid USB camera close-up image
```

Conclusion:

- Direct Windows support should be the first M41 BodyCam implementation.
- The provider can likely use Windows MediaCapture directly.
- Android should be implemented as direct USB-to-Android later, not as a relay.

Next step:

- Promote the C# probe into a reusable Windows USB camera capture client.
- Add a `UsbCameraProvider` that selects the `VID_349C&PID_0411`
  device and returns JPEG bytes from `CaptureFrameAsync()`.

## Live Run - 2026-05-30

Phase 1A was rerun with the renamed generic USB camera probe.

Enumeration command:

```powershell
dotnet run --project tools\BodyCam.UsbCameraProbe\BodyCam.UsbCameraProbe.csproj -- enumerate
```

Result:

```text
VideoCapture devices: 2
Selected target: HD camera
Device id: \\?\USB#VID_349C&PID_0411&MI_00#6&297fdf76&0&0000#{e5323777-f976-4f5b-9b55-b94699c46e44}\GLOBAL
Enabled: true
Preview formats: YUY2 640x480 and 320x240 at 5, 15, and 30 fps
Photo formats: YUY2 640x480 and 320x240 at 5, 15, and 30 fps
Record formats: YUY2 640x480 and 320x240 at 5, 15, and 30 fps
```

Capture command:

```powershell
dotnet run --project tools\BodyCam.UsbCameraProbe\BodyCam.UsbCameraProbe.csproj -- capture --vidpid "VID_349C&PID_0411" --output .my\plan\m41-usb-camera\captures\windows-direct-usb\phase-1a-2026-05-30-usb-camera-still.jpg
```

Capture result:

```text
Output: .my\plan\m41-usb-camera\captures\windows-direct-usb\phase-1a-2026-05-30-usb-camera-still.jpg
Bytes: 77538
JPEG: true
SHA256: C7165C2752C6908C4AD349463CD5FBD8198BDAD154020D56436A1640AE8CECE6
Visual check: valid USB camera close-up image
```

Phase 1A conclusion:

- The USB camera is a standard Windows video-capture device.
- C# MediaCapture can enumerate it and capture a still JPEG.
- The next implementation phase should build the Windows `UsbCameraProvider`.
