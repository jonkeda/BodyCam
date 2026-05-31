# M41 USB Camera Realtests Log

This log is intentionally high level. Detailed commands and raw artifacts should
go in phase-specific notes or capture folders.

## 2026-05-30 - Planning Started

- Created M41 for a standard USB camera.
- Captured the corrected topology: the USB camera should connect directly over
  USB to each supported platform.
- Removed the Android-to-Windows relay route from the plan.
- Defined the first route as Windows C# direct USB capture, then BodyCam
  provider integration, followed by Android direct USB and iOS feasibility.
- Recorded direct iOS support as feasibility-first because USB camera support is
  platform dependent.

## 2026-05-30 - Direct Windows USB Proof

- Added Phase 1A for direct Windows USB/UVC probing after the USB camera was
  plugged into the laptop.
- Windows sees the USB camera as `HD camera`, class `Camera`, service
  `usbvideo`, `VID_349C&PID_0411`.
- Added `tools/BodyCam.UsbCameraProbe` to enumerate Windows video devices and
  capture a still image through C# MediaCapture.
- The probe reported YUY2 formats at `640x480` and `320x240`, with `5`, `15`,
  and `30` fps options.
- The probe saved a valid JPEG still image from the USB camera. Direct Windows
  USB is now the best first implementation path.

## 2026-05-30 - Phase 1A Rerun

- Reran `tools\BodyCam.UsbCameraProbe` after renaming M41 to generic USB Camera.
- Enumeration still found the target as `HD camera` with `VID_349C&PID_0411`.
- Captured a fresh valid JPEG through Windows C# MediaCapture.
- Saved artifact:
  `.my\plan\m41-usb-camera\captures\windows-direct-usb\phase-1a-2026-05-30-usb-camera-still.jpg`.
- Result: Phase 1A is complete; move to the Windows `UsbCameraProvider`.

## 2026-05-30 - BodyCam Windows Provider Integration

- Added `UsbCameraProvider` with provider id `usb-camera` and display name
  `USB Camera`.
- Added `WindowsUsbCameraClient`, which uses Windows MediaCapture to enumerate
  USB video devices and return JPEG bytes.
- Kept the USB Camera provider/card Windows-only so Android/iOS builds do not
  accidentally expose an unfinished direct-USB path.
- Added persisted setting `UsbCameraDeviceMatch`, defaulting in UI/provider to
  `VID_349C&PID_0411`.
- Added `UsbCameraSettingsPage` and an **Add USB Camera** card in Add Devices.
- Focused unit tests passed: `22/22` for `UsbCamera|AddDevices`.
- Windows app build passed.
- Android framework build also passed, confirming the Windows-only USB camera
  integration does not leak into shared Android code.
- Added UI tests for **Add USB Camera** navigation, USB Camera settings
  controls, and device-match entry editing.
- Focused UI tests passed: `10/10` for `UsbCameraSettingsTests|AddDevicesTests`.
- Added a skipped-by-default real-hardware UI test gated by
  `BODYCAM_REAL_USB_CAMERA_UI=1`.
- Real-hardware UI tests passed: BodyCam opened **Add USB Camera**, clicked
  **Test Capture**, and reported capture success from the plugged-in USB camera.
- Real-hardware UI tests also passed the full app path: selected **USB Camera**
  in Devices, clicked **Take Picture**, and reported `Captured ... bytes`.
- Result: the Windows USB camera path is now verified from probe, provider,
  settings UI, and the normal BodyCam capture UI.
