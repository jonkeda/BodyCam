# M41 - USB Camera

**Status:** Windows provider implemented and build verified
**Goal:** Add a standard USB/UVC camera as a BodyCam camera source. The camera
should connect directly over USB to each supported platform: Windows first,
Android later, and iOS if the platform allows it.

**Depends on:** M11 (Camera Architecture), M38 lessons for hardware probes and
camera-provider integration

---

## Why

The current physical device is handled as a normal USB Video Class camera on
Windows. That lets M41 avoid device-specific protocol work and
build a generic USB camera path instead.

The proven Windows path is now:

```text
USB camera -> Windows usbvideo/UVC -> BodyCam
```

M41 turns the standard USB camera behavior into a repeatable BodyCam camera
provider. It does not use the Android phone as a relay.

---

## Key Difference From M38

The Vue990/A9 camera needed protocol reverse engineering. This device does not
appear to need that on Windows: it is visible as a standard `usbvideo` camera.

The later Android and iOS targets are direct USB targets:

```text
USB camera -> Android USB host / Camera2 -> BodyCam Android
USB camera -> iOS USB-C / AVFoundation if supported -> BodyCam iOS
```

No Android-to-Windows Wi-Fi bridge is planned.

---

## Current Assumptions

- The camera is a standard USB Video Class device on Windows.
- Windows exposes it through the Microsoft `usbvideo` service.
- The provider should use generic USB camera naming: `USB Camera`, provider id
  `usb-camera`.
- The currently observed hardware advertises YUY2, so the provider must return
  JPEG bytes after capture/encoding.
- Android and iOS are later direct-USB platform branches, not part of the first
  Windows integration.

---

## Planned Architecture

### Windows Path

```text
WindowsUsbCameraClient
  - direct Windows camera path when the USB camera is plugged into the laptop
  - uses the existing BodyCam camera-provider abstraction
```

### BodyCam Integration

```text
UsbCameraProvider
  - display name: USB Camera
  - provider id: usb-camera
  - supports direct Windows USB mode first
  - returns JPEG bytes from CaptureFrameAsync()
```

### Deferred Platform Branches

```text
Android: USB camera -> Android USB host / Camera2 -> BodyCam Android
iOS: USB camera -> iOS USB-C / AVFoundation if supported -> BodyCam iOS
```

---

## Phase Documents

- [Roadmap](./roadmap.md)
- [Phase 1A - Windows Direct USB/UVC Probe](./phase-1a-windows-direct-usb-uvc-probe.md) - complete
- [Phase 3 - Windows C# Client And BodyCam Provider](./phase-3-windows-client-and-provider.md) - implemented
- [Realtests Log](./realtests-log.md)

Deferred platform work is intentionally not split into phase docs yet. When
Android or iOS direct USB becomes active, create fresh phase docs from the
current provider shape instead of carrying old investigation scaffolding.

---

## Initial Checklist

- [x] Create M41 planning folder.
- [x] Document the direct USB camera topology.
- [x] Define first hardware proof steps.
- [x] Add direct Windows USB probe phase after the USB camera was plugged into
      the laptop.
- [x] Prove direct Windows USB visibility.
- [x] Capture one still image from Windows C#.
- [x] Define Windows C# consumer/provider path.
- [x] Track iOS as feasibility-first, not implementation-first.
- [x] Add the Windows `UsbCameraProvider` to BodyCam.
- [ ] Add Android direct USB later.
- [ ] Decide whether iOS can use the camera directly over USB later.

## Current Evidence - 2026-05-30

The direct Windows USB path works.

Windows exposes the USB camera as:

```text
Friendly name: HD camera
Class: Camera
VID/PID: VID_349C&PID_0411
Service: usbvideo
Formats: YUY2 640x480 / 320x240 at 5, 15, and 30 fps
```

The new C# probe saved a valid JPEG still image from the device:

```text
.my\plan\m41-usb-camera\captures\windows-direct-usb\usb-camera-windows-still.jpg
.my\plan\m41-usb-camera\captures\windows-direct-usb\phase-1a-2026-05-30-usb-camera-still.jpg
```

The first BodyCam implementation is now in place: direct Windows USB integration
with `UsbCameraProvider`.

Implemented app surface:

```text
Provider id: usb-camera
Display name: USB Camera
Default device match: VID_349C&PID_0411
Settings route: UsbCameraSettingsPage
Add Devices card: Add USB Camera
```

The provider uses Windows MediaCapture through `WindowsUsbCameraClient`. The
USB Camera card and provider registration are Windows-only until Android/iOS
direct USB support is intentionally implemented.

---

## Open Questions

- Should the Windows provider select the camera by VID/PID by default, by
  friendly name, or expose both in settings?
- Does the BodyCam settings UI need a separate USB Camera page, or is provider
  selection enough for the first version?
- Later: which Android API path is most reliable, Camera2 external camera or
  USB Host UVC?
- Later: can iOS use the camera directly over USB-C through AVFoundation on the
  target device?
